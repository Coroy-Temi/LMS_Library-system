using LibraryManagementSystem.Models;
using Microsoft.EntityFrameworkCore;
namespace LibraryManagementSystem.Data;

public sealed class LibraryDbContext(DbContextOptions<LibraryDbContext> options) : DbContext(options)
{
 public DbSet<Book> Books => Set<Book>();
 public DbSet<BookCopy> BookCopies => Set<BookCopy>();
 public DbSet<InventoryEvent> InventoryEvents => Set<InventoryEvent>();
 public DbSet<Genre> Genres => Set<Genre>();
 public DbSet<Member> Members => Set<Member>();
 public DbSet<Loan> Loans => Set<Loan>();
 public DbSet<User> Users => Set<User>();
 public DbSet<ExternalLogin> ExternalLogins => Set<ExternalLogin>();
 public DbSet<EmailVerification> EmailVerifications => Set<EmailVerification>();
 public DbSet<Reservation> Reservations => Set<Reservation>();
 public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
 protected override void OnModelCreating(ModelBuilder b)
 {
  b.Entity<Book>(e => { e.HasIndex(x=>x.Isbn).IsUnique().HasFilter("Isbn IS NOT NULL"); e.HasIndex(x=>new{x.Title,x.Author}); e.HasOne(x=>x.Genre).WithMany(x=>x.Books).HasForeignKey(x=>x.GenreId).OnDelete(DeleteBehavior.SetNull); e.ToTable(t=>t.HasCheckConstraint("CK_Book_Copies","AvailableCopies >= 0 AND TotalCopies >= AvailableCopies")); });
  b.Entity<Genre>().HasIndex(x=>x.Name).IsUnique();
  b.Entity<BookCopy>(e=>{e.HasIndex(x=>x.AccessionCode).IsUnique();e.HasIndex(x=>x.Barcode).IsUnique().HasFilter("Barcode IS NOT NULL");e.HasIndex(x=>new{x.BookId,x.Status});e.HasOne(x=>x.Book).WithMany(x=>x.Copies).HasForeignKey(x=>x.BookId).OnDelete(DeleteBehavior.Cascade);});
  b.Entity<InventoryEvent>(e=>{e.HasIndex(x=>new{x.BookCopyId,x.CreatedAt});e.HasOne(x=>x.BookCopy).WithMany(x=>x.Events).HasForeignKey(x=>x.BookCopyId).OnDelete(DeleteBehavior.Cascade);});
  b.Entity<Member>().HasIndex(x=>x.Email).IsUnique();
  b.Entity<Member>().HasIndex(x=>x.MemberCode).IsUnique();
  b.Entity<User>(e=>{e.HasIndex(x=>x.Username).IsUnique();e.HasIndex(x=>x.MemberId).IsUnique().HasFilter("MemberId IS NOT NULL");e.HasOne(x=>x.Member).WithOne(x=>x.User).HasForeignKey<User>(x=>x.MemberId).OnDelete(DeleteBehavior.SetNull);});
  b.Entity<ExternalLogin>(e=>{e.HasIndex(x=>new{x.Provider,x.ProviderSubject}).IsUnique();e.HasIndex(x=>new{x.Provider,x.Email});e.HasOne(x=>x.User).WithMany(x=>x.ExternalLogins).HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Cascade);});
  b.Entity<EmailVerification>(e=>{e.HasIndex(x=>x.UserId).IsUnique();e.HasOne(x=>x.User).WithOne(x=>x.EmailVerification).HasForeignKey<EmailVerification>(x=>x.UserId).OnDelete(DeleteBehavior.Cascade);});
  b.Entity<Loan>(e => { e.HasIndex(x=>new{x.Status,x.DueAt}); e.HasIndex(x=>x.BookCopyId); e.Property(x=>x.FineAmount).HasPrecision(10,2); e.HasOne(x=>x.Book).WithMany(x=>x.Loans).HasForeignKey(x=>x.BookId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x=>x.BookCopy).WithMany(x=>x.Loans).HasForeignKey(x=>x.BookCopyId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x=>x.Member).WithMany(x=>x.Loans).HasForeignKey(x=>x.MemberId).OnDelete(DeleteBehavior.Restrict); });
  b.Entity<Reservation>(e=>{e.HasIndex(x=>new{x.BookId,x.Status,x.QueuePosition});e.HasIndex(x=>new{x.BookId,x.MemberId,x.Status});e.HasOne(x=>x.Book).WithMany(x=>x.Reservations).HasForeignKey(x=>x.BookId).OnDelete(DeleteBehavior.Restrict);e.HasOne(x=>x.Member).WithMany(x=>x.Reservations).HasForeignKey(x=>x.MemberId).OnDelete(DeleteBehavior.Restrict);});
 }
}
