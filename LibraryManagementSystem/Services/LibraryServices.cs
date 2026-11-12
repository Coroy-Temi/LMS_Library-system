using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LibraryManagementSystem.Services;

public record ServiceResult(bool Success, string Message);
public record ReturnResult(bool Success, string Message, decimal Fine);
public record DashboardStats(int TotalBooks, int AvailableBooks, int ActiveMembers, int OverdueLoans);

public sealed class CatalogService(
    IDbContextFactory<LibraryDbContext> factory,
    AuditService audit,
    AuthorizationService authorization)
{
    public async Task<List<Book>> SearchAsync(string? term, int page = 1, int size = 20)
    {
        authorization.Demand(Permission.ViewCatalog);
        await using var db = await factory.CreateDbContextAsync();
        var query = db.Books.AsNoTracking().Include(x => x.Genre).AsQueryable();
        if (!string.IsNullOrWhiteSpace(term))
        {
            term = term.Trim();
            query = query.Where(x => x.Title.Contains(term) || x.Author.Contains(term) ||
                (x.Isbn != null && x.Isbn.Contains(term)) ||
                (x.Genre != null && x.Genre.Name.Contains(term)));
        }
        return await query.OrderBy(x => x.Title).Skip((page - 1) * size).Take(size).ToListAsync();
    }

    public async Task<List<Genre>> GetGenresAsync()
    {
        authorization.Demand(Permission.ManageCatalog);
        await using var db = await factory.CreateDbContextAsync();
        return await db.Genres.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
    }

    public async Task<ServiceResult> SaveAsync(Book item)
    {
        authorization.Demand(Permission.ManageCatalog);
        item.Title = item.Title.Trim(); item.Author = item.Author.Trim(); item.Isbn = string.IsNullOrWhiteSpace(item.Isbn) ? null : item.Isbn.Trim();
        if (item.Title.Length is < 1 or > 200 || item.Author.Length is < 1 or > 150)
            return new(false, "Title and author are required.");
        if (item.TotalCopies < 1 || item.AvailableCopies < 0 || item.AvailableCopies > item.TotalCopies)
            return new(false, "Available copies must be between zero and total copies.");
        if (item.PublicationYear is { } year && (year < 1000 || year > DateTime.UtcNow.Year + 1))
            return new(false, "Enter a valid publication year.");
        await using var db = await factory.CreateDbContextAsync();
        if (item.Isbn is not null && await db.Books.AnyAsync(x => x.Isbn == item.Isbn && x.BookId != item.BookId))
            return new(false, "Another book already uses that ISBN.");
        var created = item.BookId == 0;
        Book saved;
        if (created) { saved = item; db.Books.Add(saved); }
        else
        {
            saved = await db.Books.SingleOrDefaultAsync(x => x.BookId == item.BookId) ?? throw new InvalidOperationException("The book no longer exists.");
            var totalCopies=saved.TotalCopies; var availableCopies=saved.AvailableCopies;
            db.Entry(saved).CurrentValues.SetValues(item);
            saved.TotalCopies=totalCopies; saved.AvailableCopies=availableCopies;
        }
        await db.SaveChangesAsync();
        if(created)
        {
            for(var index=1;index<=saved.TotalCopies;index++)
            {
                var copy=new BookCopy{BookId=saved.BookId,AccessionCode=$"LF-{saved.BookId:D6}-{index:D4}",Condition=Enum.TryParse<BookCopyCondition>(saved.Condition,true,out var condition)?condition:BookCopyCondition.Good};
                db.BookCopies.Add(copy);
                await db.SaveChangesAsync();
                db.InventoryEvents.Add(new InventoryEvent{BookCopyId=copy.BookCopyId,EventType=InventoryEventType.Added,ToStatus=copy.Status,ToCondition=copy.Condition,Notes="Created with catalog title."});
            }
        }
        audit.Add(db, created ? "BookCreated" : "BookUpdated", nameof(Book), saved.BookId, $"Title={saved.Title}");
        await db.SaveChangesAsync();
        item.BookId = saved.BookId;
        return new(true, created ? "Book created." : "Book updated.");
    }

    public async Task<ServiceResult> DeleteAsync(int id)
    {
        authorization.Demand(Permission.ManageCatalog);
        await using var db = await factory.CreateDbContextAsync();
        var book = await db.Books.FindAsync(id);
        if (book is null) return new(false, "The book no longer exists.");
        if (await db.Loans.AnyAsync(x => x.BookId == id) || await db.Reservations.AnyAsync(x => x.BookId == id))
            return new(false, "Books with loan or reservation history cannot be deleted. Set copies to zero or archive the title instead.");
        db.Books.Remove(book);
        audit.Add(db, "BookDeleted", nameof(Book), id, $"Title={book.Title}");
        await db.SaveChangesAsync();
        return new(true, "Book deleted.");
    }
}

public sealed class MemberService(
    IDbContextFactory<LibraryDbContext> factory,
    AuditService audit,
    AuthorizationService authorization)
{
    public async Task<List<Member>> SearchAsync(string? term)
    {
        authorization.Demand(Permission.ViewMembers);
        await using var db = await factory.CreateDbContextAsync();
        var query = db.Members.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.FullName.Contains(term) || x.Email.Contains(term) ||
                (x.Phone != null && x.Phone.Contains(term)));
        return await query.OrderBy(x => x.FullName).ToListAsync();
    }

    public async Task<ServiceResult> SaveAsync(Member item)
    {
        authorization.Demand(Permission.ManageMembers);
        item.FullName = item.FullName.Trim(); item.Email = item.Email.Trim().ToLowerInvariant();
        if (item.FullName.Length is < 2 or > 120 || !System.Net.Mail.MailAddress.TryCreate(item.Email, out _))
            return new(false, "Enter a valid member name and email address.");
        if (item.ExpiresAt.Date < item.JoinedAt.Date) return new(false, "Membership expiry cannot be before the join date.");
        await using var db = await factory.CreateDbContextAsync();
        if (await db.Members.AnyAsync(x => x.Email == item.Email && x.MemberId != item.MemberId))
            return new(false, "Another member already uses that email address.");
        var created = item.MemberId == 0;
        Member saved;
        if (created) { saved = item; db.Members.Add(saved); }
        else
        {
            saved = await db.Members.SingleOrDefaultAsync(x => x.MemberId == item.MemberId) ?? throw new InvalidOperationException("The member no longer exists.");
            db.Entry(saved).CurrentValues.SetValues(item);
        }
        await db.SaveChangesAsync();
        audit.Add(db, created ? "MemberCreated" : "MemberUpdated", nameof(Member), saved.MemberId,
            $"MemberCode={saved.MemberCode}; Name={saved.FullName}");
        await db.SaveChangesAsync();
        item.MemberId = saved.MemberId;
        return new(true, created ? "Member created." : "Member updated.");
    }

    public async Task<List<Loan>> HistoryAsync(int memberId)
    {
        authorization.Demand(Permission.ViewMembers);
        await using var db = await factory.CreateDbContextAsync();
        return await db.Loans.AsNoTracking().Include(x => x.Book)
            .Where(x => x.MemberId == memberId).OrderByDescending(x => x.IssuedAt).ToListAsync();
    }
}

public sealed class LoanService(
    IDbContextFactory<LibraryDbContext> factory,
    IConfiguration config,
    ReservationService reservations,
    AuditService audit,
    AuthorizationService authorization)
{
    private readonly decimal finePerDay = config.GetValue("Library:FinePerDay", 0.50m);

    public async Task<ServiceResult> IssueBookAsync(int bookId, int memberId, int days)
    {
        authorization.Demand(Permission.ManageCirculation);
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await reservations.ExpireAndPromoteAsync(db, bookId);
        var book = await db.Books.SingleOrDefaultAsync(x => x.BookId == bookId);
        var member = await db.Members.SingleOrDefaultAsync(x => x.MemberId == memberId);
        if (book is null || member is null) return new(false, "Book or member was not found.");
        var copy = await db.BookCopies.Where(x=>x.BookId==bookId && x.Status==BookCopyStatus.Available).OrderBy(x=>x.AccessionCode).FirstOrDefaultAsync();
        if (copy is null) return new(false, "No physical copy is currently available.");
        if (!member.IsActive || member.ExpiresAt.Date < DateTime.UtcNow.Date)
            return new(false, "The member account is inactive or expired.");

        var memberReservation = await db.Reservations.Where(x => x.BookId == bookId && x.MemberId == memberId &&
            (x.Status == ReservationStatus.Waiting || x.Status == ReservationStatus.ReadyForPickup))
            .OrderBy(x => x.QueuePosition).FirstOrDefaultAsync();
        var protectedCopies = await db.Reservations.CountAsync(x => x.BookId == bookId &&
            x.Status == ReservationStatus.ReadyForPickup);
        if (memberReservation?.Status != ReservationStatus.ReadyForPickup && book.AvailableCopies - protectedCopies < 1)
            return new(false, "Available copies are reserved for members at the front of the waitlist.");

        copy.Status=BookCopyStatus.Loaned;
        db.InventoryEvents.Add(new InventoryEvent{BookCopyId=copy.BookCopyId,EventType=InventoryEventType.StatusChanged,FromStatus=BookCopyStatus.Available,ToStatus=BookCopyStatus.Loaned,Notes=$"Issued to member {member.MemberCode}."});
        if (memberReservation is not null)
        {
            memberReservation.Status = ReservationStatus.Fulfilled;
            memberReservation.ExpiresAt = null;
        }
        var loan = new Loan { BookId = bookId, BookCopyId=copy.BookCopyId, MemberId = memberId, DueAt = DateTime.UtcNow.Date.AddDays(Math.Max(1, days)) };
        db.Loans.Add(loan);
        await db.SaveChangesAsync();
        await InventoryService.RecalculateBookCountsAsync(db,bookId);
        await db.SaveChangesAsync();
        audit.Add(db, "BookIssued", nameof(Loan), loan.LoanId,
            $"BookId={bookId}; Copy={copy.AccessionCode}; MemberId={memberId}; DueAt={loan.DueAt:O}");
        if (memberReservation is not null)
            audit.Add(db, "ReservationFulfilled", nameof(Reservation), memberReservation.ReservationId, $"LoanId={loan.LoanId}");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return new(true, "Book issued successfully.");
    }

    public async Task<ReturnResult> ReturnBookAsync(int loanId)
    {
        authorization.Demand(Permission.ManageCirculation);
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var loan = await db.Loans.Include(x => x.Book).Include(x=>x.BookCopy).SingleOrDefaultAsync(x => x.LoanId == loanId);
        if (loan is null || loan.ReturnedAt != null) return new(false, "This loan is not active.", 0);
        var days = Math.Max(0, (DateTime.UtcNow.Date - loan.DueAt.Date).Days);
        loan.ReturnedAt = DateTime.UtcNow;
        loan.FineAmount = days * finePerDay;
        loan.Status = LoanStatus.Returned;
        if(loan.BookCopy is not null)
        {
            var previous=loan.BookCopy.Status;
            loan.BookCopy.Status=loan.BookCopy.Condition==BookCopyCondition.Damaged?BookCopyStatus.Maintenance:BookCopyStatus.Available;
            db.InventoryEvents.Add(new InventoryEvent{BookCopyId=loan.BookCopy.BookCopyId,EventType=InventoryEventType.StatusChanged,FromStatus=previous,ToStatus=loan.BookCopy.Status,Notes=$"Returned by member {loan.MemberId}."});
            await db.SaveChangesAsync();
            await InventoryService.RecalculateBookCountsAsync(db,loan.BookId);
            await db.SaveChangesAsync();
        }
        else loan.Book.AvailableCopies++;
        await reservations.PromoteNextAsync(db, loan.BookId);
        audit.Add(db, "BookReturned", nameof(Loan), loan.LoanId,
            $"BookId={loan.BookId}; Copy={loan.BookCopy?.AccessionCode ?? "legacy"}; MemberId={loan.MemberId}; Fine={loan.FineAmount:F2}");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return new(true, "Book returned successfully.", loan.FineAmount);
    }

    public async Task<List<Loan>> GetActiveAsync()
    {
        authorization.Demand(Permission.ManageCirculation);
        await using var db = await factory.CreateDbContextAsync();
        var loans = await db.Loans.Include(x => x.Book).Include(x=>x.BookCopy).Include(x => x.Member)
            .Where(x => x.ReturnedAt == null).OrderBy(x => x.DueAt).ToListAsync();
        foreach (var loan in loans) if (loan.IsOverdue) loan.Status = LoanStatus.Overdue;
        return loans;
    }
}

public sealed class DashboardService(
    IDbContextFactory<LibraryDbContext> factory,
    AuthorizationService authorization)
{
    public async Task<DashboardStats> GetAsync()
    {
        authorization.Demand(Permission.ViewDashboard);
        await using var db = await factory.CreateDbContextAsync();
        return new(
            await db.Books.SumAsync(x => x.TotalCopies),
            await db.Books.SumAsync(x => x.AvailableCopies),
            await db.Members.CountAsync(x => x.IsActive && x.ExpiresAt >= DateTime.UtcNow.Date),
            await db.Loans.CountAsync(x => x.ReturnedAt == null && x.DueAt < DateTime.UtcNow.Date));
    }
}
