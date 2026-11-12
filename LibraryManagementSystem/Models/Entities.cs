using System.ComponentModel.DataAnnotations;

namespace LibraryManagementSystem.Models;
public enum UserRole { Librarian = 0, Admin = 1, Viewer = 2 }
public enum LoanStatus { Active, Returned, Overdue }
public enum ReservationStatus { Waiting, ReadyForPickup, Fulfilled, Cancelled, Expired }
public enum BookCopyStatus { Available, Loaned, Reserved, Maintenance, Lost, Retired }
public enum BookCopyCondition { New, Good, Fair, Damaged, Lost }
public enum InventoryEventType { Added, StatusChanged, ConditionChanged, Maintenance, Lost, Restored, Retired, QrIssued }

public sealed class Book
{
 public int BookId { get; set; }
 [Required, StringLength(200)] public string Title { get; set; } = "";
 [Required, StringLength(150)] public string Author { get; set; } = "";
 [StringLength(20)] public string? Isbn { get; set; }
 [StringLength(100)] public string? Publisher { get; set; }
 public int? PublicationYear { get; set; }
 [StringLength(80)] public string? Location { get; set; }
 [StringLength(2000)] public string? Description { get; set; }
 [StringLength(30)] public string Condition { get; set; } = "Good";
 [Range(1,10000)] public int TotalCopies { get; set; } = 1;
 [Range(0,10000)] public int AvailableCopies { get; set; } = 1;
 [StringLength(500)] public string? CoverImagePath { get; set; }
 public int? GenreId { get; set; }
 public Genre? Genre { get; set; }
 public ICollection<Loan> Loans { get; set; } = new List<Loan>();
 public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
 public ICollection<BookCopy> Copies { get; set; } = new List<BookCopy>();
 public bool IsAvailable => AvailableCopies > 0;
}
public sealed class BookCopy
{
 public int BookCopyId { get; set; }
 public int BookId { get; set; }
 public Book Book { get; set; } = null!;
 [Required, StringLength(40)] public string AccessionCode { get; set; } = "";
 [StringLength(80)] public string? Barcode { get; set; }
 public BookCopyStatus Status { get; set; } = BookCopyStatus.Available;
 public BookCopyCondition Condition { get; set; } = BookCopyCondition.Good;
 public DateTime AcquiredAt { get; set; } = DateTime.UtcNow;
 [StringLength(1000)] public string? Notes { get; set; }
 public DateTime? LastQrIssuedAt { get; set; }
 public ICollection<Loan> Loans { get; set; } = new List<Loan>();
 public ICollection<InventoryEvent> Events { get; set; } = new List<InventoryEvent>();
}
public sealed class InventoryEvent
{
 public long InventoryEventId { get; set; }
 public int BookCopyId { get; set; }
 public BookCopy BookCopy { get; set; } = null!;
 public InventoryEventType EventType { get; set; }
 public BookCopyStatus? FromStatus { get; set; }
 public BookCopyStatus? ToStatus { get; set; }
 public BookCopyCondition? FromCondition { get; set; }
 public BookCopyCondition? ToCondition { get; set; }
 [StringLength(1000)] public string? Notes { get; set; }
 public int? UserId { get; set; }
 public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public sealed class Genre
{
 public int GenreId { get; set; }
 [Required, StringLength(80)] public string Name { get; set; } = "";
 public ICollection<Book> Books { get; set; } = new List<Book>();
}
public sealed class Member
{
 public int MemberId { get; set; }
 [Required, StringLength(30)] public string MemberCode { get; set; } = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
 [Required, StringLength(120)] public string FullName { get; set; } = "";
 [Required, EmailAddress, StringLength(200)] public string Email { get; set; } = "";
 [Phone, StringLength(30)] public string? Phone { get; set; }
 [StringLength(300)] public string? Address { get; set; }
 [StringLength(500)] public string? PhotoPath { get; set; }
 public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
 public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddYears(1);
 public bool IsActive { get; set; } = true;
 public ICollection<Loan> Loans { get; set; } = new List<Loan>();
 public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
 public User? User { get; set; }
}
public sealed class Loan
{
 public int LoanId { get; set; }
 public int BookId { get; set; }
 public Book Book { get; set; } = null!;
 public int? BookCopyId { get; set; }
 public BookCopy? BookCopy { get; set; }
 public int MemberId { get; set; }
 public Member Member { get; set; } = null!;
 public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
 public DateTime DueAt { get; set; }
 public DateTime? ReturnedAt { get; set; }
 public decimal FineAmount { get; set; }
 public LoanStatus Status { get; set; } = LoanStatus.Active;
 public bool IsOverdue => ReturnedAt is null && DueAt.Date < DateTime.UtcNow.Date;
}
public sealed class User
{
 public int UserId { get; set; }
 [Required, StringLength(80)] public string Username { get; set; } = "";
 [Required] public string PasswordHash { get; set; } = "";
 [Required, StringLength(120)] public string DisplayName { get; set; } = "";
 public UserRole Role { get; set; } = UserRole.Librarian;
 public bool IsActive { get; set; } = true;
 public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
 public DateTime? LastLogin { get; set; }
 public int FailedLoginAttempts { get; set; }
 public DateTime? LockoutEndUtc { get; set; }
 public DateTime? PasswordChangedAt { get; set; }
 public bool MustChangePassword { get; set; }
 public bool HasLocalPassword { get; set; } = true;
 public bool IsEmailVerified { get; set; } = true;
 [Required, StringLength(64)] public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
 public int? MemberId { get; set; }
 public Member? Member { get; set; }
 public ICollection<ExternalLogin> ExternalLogins { get; set; } = new List<ExternalLogin>();
 public EmailVerification? EmailVerification { get; set; }
}
public sealed class EmailVerification
{
 public int EmailVerificationId { get; set; }
 public int UserId { get; set; }
 public User User { get; set; } = null!;
 [Required, StringLength(64)] public string CodeHash { get; set; } = "";
 public DateTime ExpiresAtUtc { get; set; }
 public DateTime LastSentAtUtc { get; set; } = DateTime.UtcNow;
 public int AttemptCount { get; set; }
}
public sealed class ExternalLogin
{
 public int ExternalLoginId { get; set; }
 public int UserId { get; set; }
 public User User { get; set; } = null!;
 [Required, StringLength(40)] public string Provider { get; set; } = "";
 [Required, StringLength(200)] public string ProviderSubject { get; set; } = "";
 [Required, EmailAddress, StringLength(200)] public string Email { get; set; } = "";
 public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
 public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;
}
public sealed class Reservation
{
 public int ReservationId { get; set; }
 public int BookId { get; set; } public Book Book { get; set; } = null!;
 public int MemberId { get; set; } public Member Member { get; set; } = null!;
 public DateTime ReservedAt { get; set; } = DateTime.UtcNow;
 public DateTime? ExpiresAt { get; set; }
 public ReservationStatus Status { get; set; } = ReservationStatus.Waiting;
 public int QueuePosition { get; set; }
}
public sealed class AuditLog
{
 public long AuditLogId { get; set; }
 [Required, StringLength(80)] public string Action { get; set; } = "";
 [Required, StringLength(80)] public string EntityName { get; set; } = "";
 [StringLength(80)] public string? EntityId { get; set; }
 [StringLength(2000)] public string? Details { get; set; }
 public int? UserId { get; set; }
 public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
