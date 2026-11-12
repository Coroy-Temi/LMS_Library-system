using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LibraryManagementSystem.Tests;

public sealed class CatalogManagementTests
{
    [Fact]
    public async Task SaveAsync_RejectsInvalidCopyCountsAndDuplicateIsbn()
    {
        await using var test = await CatalogDatabase.CreateAsync();
        var invalid = await test.Catalog.SaveAsync(new Book
        {
            Title = "Invalid", Author = "Author", TotalCopies = 1, AvailableCopies = 2
        });
        var first = await test.Catalog.SaveAsync(new Book
        {
            Title = "First", Author = "Author", Isbn = "9780000000001", TotalCopies = 1, AvailableCopies = 1
        });
        var duplicate = await test.Catalog.SaveAsync(new Book
        {
            Title = "Second", Author = "Author", Isbn = "9780000000001", TotalCopies = 1, AvailableCopies = 1
        });

        Assert.False(invalid.Success);
        Assert.True(first.Success);
        Assert.False(duplicate.Success);
        await using var db = await test.Factory.CreateDbContextAsync();
        Assert.Single(await db.Books.ToListAsync());
    }

    [Fact]
    public async Task CsvImport_HandlesQuotedCommasAndSkipsInvalidRows()
    {
        await using var test = await CatalogDatabase.CreateAsync();
        var path = Path.Combine(Path.GetTempPath(), $"libraryforge-{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(path,
            "Title,Author,ISBN,Genre,TotalCopies,AvailableCopies\n\"A, Great Book\",Ada Author,9780000000002,Fiction,3,3\nBroken,,123,,1,1");
        try
        {
            var result = await test.Importer.ImportBooksCsvAsync(path);
            Assert.True(result.Success);
            Assert.Equal(1, result.Imported);
            Assert.Equal(1, result.Skipped);
            await using var db = await test.Factory.CreateDbContextAsync();
            var book = await db.Books.Include(x => x.Genre).SingleAsync();
            Assert.Equal("A, Great Book", book.Title);
            Assert.Equal("Fiction", book.Genre?.Name);
        }
        finally { File.Delete(path); }
    }

    private sealed class CatalogDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public TestFactory Factory { get; }
        public CatalogService Catalog { get; }
        public CatalogImportService Importer { get; }

        private CatalogDatabase(SqliteConnection connection, TestFactory factory)
        {
            this.connection = connection; Factory = factory;
            var session = new SessionService();
            session.SignIn(new User { UserId = 1, Username = "admin", DisplayName = "Admin", Role = UserRole.Admin }, TimeSpan.FromHours(1));
            var authorization = new AuthorizationService(session);
            var audit = new AuditService(factory, session, authorization);
            Catalog = new CatalogService(factory, audit, authorization);
            Importer = new CatalogImportService(factory, authorization, audit);
        }

        public static async Task<CatalogDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var factory = new TestFactory(new DbContextOptionsBuilder<LibraryDbContext>().UseSqlite(connection).Options);
            var result = new CatalogDatabase(connection, factory);
            await using var db = await factory.CreateDbContextAsync(); await db.Database.EnsureCreatedAsync();
            return result;
        }
        public async ValueTask DisposeAsync() => await connection.DisposeAsync();
    }

    private sealed class TestFactory(DbContextOptions<LibraryDbContext> options) : IDbContextFactory<LibraryDbContext>
    {
        public LibraryDbContext CreateDbContext() => new(options);
        public Task<LibraryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
