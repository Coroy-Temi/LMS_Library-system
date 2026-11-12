using LibraryManagementSystem.Models;
using Microsoft.EntityFrameworkCore;
namespace LibraryManagementSystem.Data;
public sealed class DatabaseInitializer(IDbContextFactory<LibraryDbContext> factory)
{
 public async Task InitializeAsync()
 {
  await using var db=await factory.CreateDbContextAsync(); await db.Database.EnsureCreatedAsync();
  await UpgradeIdentitySchemaAsync(db);
  if(await db.Users.AnyAsync()) { await BackfillBookCopiesAsync(db); return; }
  var genres=new[]{"Fiction","Science","History","Technology","Biography"}.Select(x=>new Genre{Name=x}).ToArray(); db.Genres.AddRange(genres);
  var data=new[]{("The Great Gatsby","F. Scott Fitzgerald","9780743273565",0,4),("To Kill a Mockingbird","Harper Lee","9780061120084",0,3),("A Brief History of Time","Stephen Hawking","9780553380163",1,2),("Sapiens","Yuval Noah Harari","9780062316097",2,3),("Clean Code","Robert C. Martin","9780132350884",3,4),("The Pragmatic Programmer","David Thomas","9780135957059",3,2),("Steve Jobs","Walter Isaacson","9781451648539",4,2),("1984","George Orwell","9780451524935",0,5),("The Selfish Gene","Richard Dawkins","9780198788607",1,2),("The Innovators","Walter Isaacson","9781476708706",4,2)};
  db.Books.AddRange(data.Select(x=>new Book{Title=x.Item1,Author=x.Item2,Isbn=x.Item3,Genre=genres[x.Item4],TotalCopies=x.Item5,AvailableCopies=x.Item5}));
  db.Members.AddRange(Enumerable.Range(1,5).Select(i=>new Member{FullName=$"Sample Member {i}",Email=$"member{i}@library.local",Phone=$"+25470000000{i}"}));
  var now=DateTime.UtcNow;
  db.Users.AddRange(
   new User{Username="admin",DisplayName="Library Administrator",Role=UserRole.Admin,PasswordHash=BCrypt.Net.BCrypt.HashPassword("admin123"),PasswordChangedAt=now},
   new User{Username="librarian",DisplayName="Library Staff",Role=UserRole.Librarian,PasswordHash=BCrypt.Net.BCrypt.HashPassword("lib123"),PasswordChangedAt=now},
   new User{Username="viewer",DisplayName="Catalog Viewer",Role=UserRole.Viewer,PasswordHash=BCrypt.Net.BCrypt.HashPassword("Viewer@12345"),PasswordChangedAt=now});
  await db.SaveChangesAsync();
  await BackfillBookCopiesAsync(db);
 }

 private static async Task UpgradeIdentitySchemaAsync(LibraryDbContext db)
 {
  var connection=db.Database.GetDbConnection();
  await connection.OpenAsync();
  try
  {
   var columns=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   await using(var command=connection.CreateCommand())
   {
    command.CommandText="PRAGMA table_info('Users')";
    await using var reader=await command.ExecuteReaderAsync();
    while(await reader.ReadAsync()) columns.Add(reader.GetString(1));
   }
   var additions=new Dictionary<string,string>
   {
    ["FailedLoginAttempts"]="INTEGER NOT NULL DEFAULT 0",
    ["LockoutEndUtc"]="TEXT NULL",
    ["PasswordChangedAt"]="TEXT NULL",
    ["MustChangePassword"]="INTEGER NOT NULL DEFAULT 0",
    ["SecurityStamp"]="TEXT NOT NULL DEFAULT ''",
    ["MemberId"]="INTEGER NULL",
    ["HasLocalPassword"]="INTEGER NOT NULL DEFAULT 1",
    ["IsEmailVerified"]="INTEGER NOT NULL DEFAULT 1"
   };
   foreach(var item in additions.Where(x=>!columns.Contains(x.Key)))
   {
    await using var command=connection.CreateCommand();
    command.CommandText=$"ALTER TABLE Users ADD COLUMN {item.Key} {item.Value}";
    await command.ExecuteNonQueryAsync();
   }
   await using var stampCommand=connection.CreateCommand();
   stampCommand.CommandText="UPDATE Users SET SecurityStamp = lower(hex(randomblob(16))) WHERE SecurityStamp IS NULL OR SecurityStamp = ''";
   await stampCommand.ExecuteNonQueryAsync();
   await using var identityCommand=connection.CreateCommand();
   identityCommand.CommandText="""
    CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_MemberId ON Users(MemberId) WHERE MemberId IS NOT NULL;
    CREATE TABLE IF NOT EXISTS ExternalLogins (
      ExternalLoginId INTEGER NOT NULL CONSTRAINT PK_ExternalLogins PRIMARY KEY AUTOINCREMENT,
      UserId INTEGER NOT NULL,
      Provider TEXT NOT NULL,
      ProviderSubject TEXT NOT NULL,
      Email TEXT NOT NULL,
      CreatedAt TEXT NOT NULL,
      LastUsedAt TEXT NOT NULL,
      CONSTRAINT FK_ExternalLogins_Users_UserId FOREIGN KEY (UserId) REFERENCES Users(UserId) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX IF NOT EXISTS IX_ExternalLogins_Provider_ProviderSubject ON ExternalLogins(Provider, ProviderSubject);
    CREATE INDEX IF NOT EXISTS IX_ExternalLogins_Provider_Email ON ExternalLogins(Provider, Email);
    CREATE INDEX IF NOT EXISTS IX_ExternalLogins_UserId ON ExternalLogins(UserId);
    CREATE TABLE IF NOT EXISTS EmailVerifications (
      EmailVerificationId INTEGER NOT NULL CONSTRAINT PK_EmailVerifications PRIMARY KEY AUTOINCREMENT,
      UserId INTEGER NOT NULL,
      CodeHash TEXT NOT NULL,
      ExpiresAtUtc TEXT NOT NULL,
      LastSentAtUtc TEXT NOT NULL,
      AttemptCount INTEGER NOT NULL DEFAULT 0,
      CONSTRAINT FK_EmailVerifications_Users_UserId FOREIGN KEY (UserId) REFERENCES Users(UserId) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX IF NOT EXISTS IX_EmailVerifications_UserId ON EmailVerifications(UserId);
    """;
   await identityCommand.ExecuteNonQueryAsync();

   var loanColumns=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   await using(var command=connection.CreateCommand())
   {
    command.CommandText="PRAGMA table_info('Loans')";
    await using var reader=await command.ExecuteReaderAsync();
    while(await reader.ReadAsync()) loanColumns.Add(reader.GetString(1));
   }
   if(!loanColumns.Contains("BookCopyId"))
   {
    await using var command=connection.CreateCommand();
    command.CommandText="ALTER TABLE Loans ADD COLUMN BookCopyId INTEGER NULL";
    await command.ExecuteNonQueryAsync();
   }
   await using var inventoryCommand=connection.CreateCommand();
   inventoryCommand.CommandText="""
    CREATE TABLE IF NOT EXISTS BookCopies (
      BookCopyId INTEGER NOT NULL CONSTRAINT PK_BookCopies PRIMARY KEY AUTOINCREMENT,
      BookId INTEGER NOT NULL,
      AccessionCode TEXT NOT NULL,
      Barcode TEXT NULL,
      Status INTEGER NOT NULL,
      Condition INTEGER NOT NULL,
      AcquiredAt TEXT NOT NULL,
      Notes TEXT NULL,
      LastQrIssuedAt TEXT NULL,
      CONSTRAINT FK_BookCopies_Books_BookId FOREIGN KEY (BookId) REFERENCES Books(BookId) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX IF NOT EXISTS IX_BookCopies_AccessionCode ON BookCopies(AccessionCode);
    CREATE UNIQUE INDEX IF NOT EXISTS IX_BookCopies_Barcode ON BookCopies(Barcode) WHERE Barcode IS NOT NULL;
    CREATE INDEX IF NOT EXISTS IX_BookCopies_BookId_Status ON BookCopies(BookId, Status);
    CREATE TABLE IF NOT EXISTS InventoryEvents (
      InventoryEventId INTEGER NOT NULL CONSTRAINT PK_InventoryEvents PRIMARY KEY AUTOINCREMENT,
      BookCopyId INTEGER NOT NULL,
      EventType INTEGER NOT NULL,
      FromStatus INTEGER NULL,
      ToStatus INTEGER NULL,
      FromCondition INTEGER NULL,
      ToCondition INTEGER NULL,
      Notes TEXT NULL,
      UserId INTEGER NULL,
      CreatedAt TEXT NOT NULL,
      CONSTRAINT FK_InventoryEvents_BookCopies_BookCopyId FOREIGN KEY (BookCopyId) REFERENCES BookCopies(BookCopyId) ON DELETE CASCADE
    );
    CREATE INDEX IF NOT EXISTS IX_InventoryEvents_BookCopyId_CreatedAt ON InventoryEvents(BookCopyId, CreatedAt);
    CREATE INDEX IF NOT EXISTS IX_Loans_BookCopyId ON Loans(BookCopyId);
    """;
   await inventoryCommand.ExecuteNonQueryAsync();
  }
  finally { await connection.CloseAsync(); }
 }

 private static async Task BackfillBookCopiesAsync(LibraryDbContext db)
 {
  var books=await db.Books.Include(x=>x.Copies).ToListAsync();
  foreach(var book in books.Where(x=>x.Copies.Count==0))
  {
   var activeLoans=await db.Loans.Where(x=>x.BookId==book.BookId && x.ReturnedAt==null).OrderBy(x=>x.LoanId).ToListAsync();
   var target=Math.Max(book.TotalCopies,activeLoans.Count);
   for(var index=1;index<=target;index++)
   {
    var loan=index<=activeLoans.Count?activeLoans[index-1]:null;
    var copy=new BookCopy
    {
     BookId=book.BookId,
     AccessionCode=$"LF-{book.BookId:D6}-{index:D4}",
     Status=loan is null?BookCopyStatus.Available:BookCopyStatus.Loaned,
     Condition=Enum.TryParse<BookCopyCondition>(book.Condition,true,out var condition)?condition:BookCopyCondition.Good,
     Notes="Created automatically from the legacy title-level inventory."
    };
    db.BookCopies.Add(copy);
    await db.SaveChangesAsync();
    db.InventoryEvents.Add(new InventoryEvent{BookCopyId=copy.BookCopyId,EventType=InventoryEventType.Added,ToStatus=copy.Status,ToCondition=copy.Condition,Notes=copy.Notes});
    if(loan is not null) loan.BookCopyId=copy.BookCopyId;
   }
   book.TotalCopies=target;
   book.AvailableCopies=target-activeLoans.Count;
  }
  await db.SaveChangesAsync();
 }
}
