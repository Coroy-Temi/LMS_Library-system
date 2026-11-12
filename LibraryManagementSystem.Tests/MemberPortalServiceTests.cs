using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LibraryManagementSystem.Tests;

public sealed class MemberPortalServiceTests
{
    [Fact]
    public async Task UpdateProfileAsync_UpdatesOnlySignedInMembersContactProfile()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<LibraryDbContext>().UseSqlite(connection).Options;
        var factory = new TestFactory(options);
        int memberId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
            var member = new Member { FullName = "Before Name", Email = "member@test.local" };
            db.Members.Add(member);
            await db.SaveChangesAsync();
            memberId = member.MemberId;
        }
        var session = new SessionService();
        session.SignIn(new User
        {
            UserId = 7, Username = "member", DisplayName = "Before Name", Role = UserRole.Viewer,
            MemberId = memberId, SecurityStamp = Guid.NewGuid().ToString("N")
        }, TimeSpan.FromHours(1));
        var authorization = new AuthorizationService(session);
        var audit = new AuditService(factory, session, authorization);
        var reservations = new ReservationService(factory, audit, authorization, session);
        var portal = new MemberPortalService(factory, session, reservations, audit);

        var result = await portal.UpdateProfileAsync("After Name", "+234 800 000 0000", "12 Reading Lane");

        Assert.True(result.Success);
        await using var verify = await factory.CreateDbContextAsync();
        var memberAfter = await verify.Members.SingleAsync();
        Assert.Equal("After Name", memberAfter.FullName);
        Assert.Equal("+234 800 000 0000", memberAfter.Phone);
        Assert.Equal("12 Reading Lane", memberAfter.Address);
        Assert.Equal("After Name", session.Current?.DisplayName);
        Assert.Contains(await verify.AuditLogs.ToListAsync(), x => x.Action == "MemberProfileUpdated");
    }

    private sealed class TestFactory(DbContextOptions<LibraryDbContext> options)
        : IDbContextFactory<LibraryDbContext>
    {
        public LibraryDbContext CreateDbContext() => new(options);
        public Task<LibraryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
