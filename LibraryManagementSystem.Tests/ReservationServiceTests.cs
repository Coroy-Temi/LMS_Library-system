using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LibraryManagementSystem.Tests;

public sealed class ReservationServiceTests
{
    [Fact]
    public async Task CreateAsync_AddsMemberToWaitlistAndAuditsAction()
    {
        await using var test = await TestDatabase.CreateAsync(availableCopies: 0);

        var result = await test.Reservations.CreateAsync(test.BookId, test.MemberOneId);

        Assert.True(result.Success);
        await using var db = await test.Factory.CreateDbContextAsync();
        var reservation = await db.Reservations.SingleAsync();
        Assert.Equal(ReservationStatus.Waiting, reservation.Status);
        Assert.Equal(1, reservation.QueuePosition);
        Assert.Contains(await db.AuditLogs.ToListAsync(), x => x.Action == "ReservationCreated");
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateActiveReservation()
    {
        await using var test = await TestDatabase.CreateAsync(availableCopies: 0);
        Assert.True((await test.Reservations.CreateAsync(test.BookId, test.MemberOneId)).Success);

        var duplicate = await test.Reservations.CreateAsync(test.BookId, test.MemberOneId);

        Assert.False(duplicate.Success);
        await using var db = await test.Factory.CreateDbContextAsync();
        Assert.Equal(1, await db.Reservations.CountAsync());
    }

    [Fact]
    public async Task ReturnBookAsync_PromotesFirstWaitingReservation()
    {
        await using var test = await TestDatabase.CreateAsync(availableCopies: 0);
        await using (var db = await test.Factory.CreateDbContextAsync())
        {
            var copyId=await db.BookCopies.Select(x=>x.BookCopyId).SingleAsync();
            db.Loans.Add(new Loan
            {
                BookId = test.BookId,
                BookCopyId=copyId,
                MemberId = test.MemberTwoId,
                DueAt = DateTime.UtcNow.AddDays(-2)
            });
            await db.SaveChangesAsync();
        }
        Assert.True((await test.Reservations.CreateAsync(test.BookId, test.MemberOneId)).Success);
        int loanId;
        await using (var db = await test.Factory.CreateDbContextAsync())
            loanId = await db.Loans.Select(x => x.LoanId).SingleAsync();

        var result = await test.Loans.ReturnBookAsync(loanId);

        Assert.True(result.Success);
        Assert.Equal(1.00m, result.Fine);
        await using var verify = await test.Factory.CreateDbContextAsync();
        var reservation = await verify.Reservations.SingleAsync();
        Assert.Equal(ReservationStatus.ReadyForPickup, reservation.Status);
        Assert.NotNull(reservation.ExpiresAt);
        var actions = await verify.AuditLogs.Select(x => x.Action).ToListAsync();
        Assert.Contains("ReservationReady", actions);
        Assert.Contains("BookReturned", actions);
    }

    [Fact]
    public async Task IssueBookAsync_ProtectsReadyCopyAndFulfillsForReservedMember()
    {
        await using var test = await TestDatabase.CreateAsync(availableCopies: 1);
        await using (var db = await test.Factory.CreateDbContextAsync())
        {
            db.Reservations.Add(new Reservation
            {
                BookId = test.BookId,
                MemberId = test.MemberOneId,
                QueuePosition = 1,
                Status = ReservationStatus.ReadyForPickup,
                ExpiresAt = DateTime.UtcNow.AddDays(2)
            });
            await db.SaveChangesAsync();
        }

        var wrongMember = await test.Loans.IssueBookAsync(test.BookId, test.MemberTwoId, 14);
        var reservedMember = await test.Loans.IssueBookAsync(test.BookId, test.MemberOneId, 14);

        Assert.False(wrongMember.Success);
        Assert.True(reservedMember.Success);
        await using var verify = await test.Factory.CreateDbContextAsync();
        Assert.Equal(0, (await verify.Books.SingleAsync()).AvailableCopies);
        Assert.Equal(ReservationStatus.Fulfilled, (await verify.Reservations.SingleAsync()).Status);
        Assert.Equal(test.MemberOneId, (await verify.Loans.SingleAsync()).MemberId);
    }

    [Fact]
    public async Task CurrentMember_CanCreateAndCancelOwnReservation()
    {
        await using var test = await TestDatabase.CreateAsync(availableCopies: 0);

        var created = await test.Reservations.CreateForCurrentMemberAsync(test.BookId);
        await using var db = await test.Factory.CreateDbContextAsync();
        var reservationId = await db.Reservations.Select(x => x.ReservationId).SingleAsync();
        var cancelled = await test.Reservations.CancelForCurrentMemberAsync(reservationId);

        Assert.True(created.Success);
        Assert.True(cancelled.Success);
        Assert.Equal(ReservationStatus.Cancelled, (await db.Reservations.SingleAsync()).Status);
    }

    [Fact]
    public async Task CurrentMember_CannotCancelAnotherMembersReservation()
    {
        await using var test = await TestDatabase.CreateAsync(availableCopies: 0);
        Assert.True((await test.Reservations.CreateAsync(test.BookId, test.MemberTwoId)).Success);
        await using var db = await test.Factory.CreateDbContextAsync();
        var reservationId = await db.Reservations.Select(x => x.ReservationId).SingleAsync();

        var result = await test.Reservations.CancelForCurrentMemberAsync(reservationId);

        Assert.False(result.Success);
        Assert.Equal(ReservationStatus.Waiting, (await db.Reservations.SingleAsync()).Status);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public TestFactory Factory { get; }
        public ReservationService Reservations { get; }
        public LoanService Loans { get; }
        public SessionService Session { get; } = new();
        public int BookId { get; private set; }
        public int MemberOneId { get; private set; }
        public int MemberTwoId { get; private set; }

        private TestDatabase(SqliteConnection connection, TestFactory factory)
        {
            this.connection = connection;
            Factory = factory;
            Session.SignIn(new User
            {
                UserId = 99, Username = "test", DisplayName = "Test", Role = UserRole.Admin,
                SecurityStamp = Guid.NewGuid().ToString("N")
            }, TimeSpan.FromHours(1));
            var authorization = new AuthorizationService(Session);
            var audit = new AuditService(factory, Session, authorization);
            Reservations = new ReservationService(factory, audit, authorization, Session);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Library:FinePerDay"] = "0.50" })
                .Build();
            Loans = new LoanService(factory, configuration, Reservations, audit, authorization);
        }

        public static async Task<TestDatabase> CreateAsync(int availableCopies)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<LibraryDbContext>().UseSqlite(connection).Options;
            var factory = new TestFactory(options);
            var test = new TestDatabase(connection, factory);
            await using var db = await factory.CreateDbContextAsync();
            await db.Database.EnsureCreatedAsync();
            var book = new Book
            {
                Title = "Reserved title",
                Author = "Test Author",
                TotalCopies = 1,
                AvailableCopies = availableCopies
            };
            var memberOne = new Member { FullName = "Member One", Email = "one@test.local" };
            var memberTwo = new Member { FullName = "Member Two", Email = "two@test.local" };
            db.AddRange(book, memberOne, memberTwo);
            await db.SaveChangesAsync();
            db.BookCopies.Add(new BookCopy{BookId=book.BookId,AccessionCode=$"LF-{book.BookId:D6}-0001",Status=availableCopies>0?BookCopyStatus.Available:BookCopyStatus.Loaned});
            await db.SaveChangesAsync();
            test.BookId = book.BookId;
            test.MemberOneId = memberOne.MemberId;
            test.MemberTwoId = memberTwo.MemberId;
            test.Session.SignIn(new User
            {
                UserId = 99, Username = "test", DisplayName = "Test", Role = UserRole.Admin,
                MemberId = memberOne.MemberId, SecurityStamp = Guid.NewGuid().ToString("N")
            }, TimeSpan.FromHours(1));
            return test;
        }

        public async ValueTask DisposeAsync() => await connection.DisposeAsync();
    }

    private sealed class TestFactory(DbContextOptions<LibraryDbContext> options)
        : IDbContextFactory<LibraryDbContext>
    {
        public LibraryDbContext CreateDbContext() => new(options);
        public Task<LibraryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
