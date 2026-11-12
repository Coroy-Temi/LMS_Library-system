using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Services;

public sealed record QrLabelResult(bool Success, string Message, byte[]? Png = null, string? Token = null, BookCopy? Copy = null);

public sealed class InventoryService(
    IDbContextFactory<LibraryDbContext> factory,
    AuthorizationService authorization,
    AuditService audit,
    SessionService session,
    SecureQrCodeService qr)
{
    public async Task<List<BookCopy>> GetCopiesAsync(int bookId)
    {
        authorization.Demand(Permission.ManageCatalog);
        await using var db = await factory.CreateDbContextAsync();
        return await db.BookCopies.AsNoTracking().Where(x => x.BookId == bookId).OrderBy(x => x.AccessionCode).ToListAsync();
    }

    public async Task<List<InventoryEvent>> GetHistoryAsync(int copyId)
    {
        authorization.Demand(Permission.ManageCatalog);
        await using var db = await factory.CreateDbContextAsync();
        return await db.InventoryEvents.AsNoTracking().Where(x => x.BookCopyId == copyId).OrderByDescending(x => x.CreatedAt).ToListAsync();
    }

    public async Task<ServiceResult> AddCopyAsync(int bookId, BookCopyCondition condition = BookCopyCondition.Good, string? notes = null)
    {
        authorization.Demand(Permission.ManageCatalog);
        await using var db = await factory.CreateDbContextAsync();
        if (!await db.Books.AnyAsync(x => x.BookId == bookId)) return new(false, "The book no longer exists.");
        var sequence = await NextSequenceAsync(db, bookId);
        var copy = new BookCopy { BookId = bookId, AccessionCode = $"LF-{bookId:D6}-{sequence:D4}", Condition = condition, Notes = notes?.Trim() };
        db.BookCopies.Add(copy);
        await db.SaveChangesAsync();
        db.InventoryEvents.Add(Event(copy, InventoryEventType.Added, null, copy.Status, null, copy.Condition, notes));
        audit.Add(db, "BookCopyAdded", nameof(BookCopy), copy.BookCopyId, $"Accession={copy.AccessionCode}; BookId={bookId}");
        await RecalculateBookCountsAsync(db, bookId);
        await db.SaveChangesAsync();
        return new(true, $"Copy {copy.AccessionCode} added.");
    }

    public async Task<ServiceResult> ChangeStateAsync(int copyId, BookCopyStatus status, BookCopyCondition? condition, string? notes)
    {
        authorization.Demand(Permission.ManageCatalog);
        await using var db = await factory.CreateDbContextAsync();
        var copy = await db.BookCopies.SingleOrDefaultAsync(x => x.BookCopyId == copyId);
        if (copy is null) return new(false, "The physical copy no longer exists.");
        if (copy.Status is BookCopyStatus.Loaned or BookCopyStatus.Reserved)
            return new(false, "A loaned or reserved copy must be changed through circulation.");
        if (status is BookCopyStatus.Loaned or BookCopyStatus.Reserved)
            return new(false, "Loaned and reserved states are controlled by circulation.");
        var fromStatus = copy.Status; var fromCondition = copy.Condition;
        copy.Status = status;
        copy.Condition = condition ?? (status == BookCopyStatus.Lost ? BookCopyCondition.Lost : copy.Condition);
        copy.Notes = string.IsNullOrWhiteSpace(notes) ? copy.Notes : notes.Trim();
        var type = status switch { BookCopyStatus.Lost => InventoryEventType.Lost, BookCopyStatus.Maintenance => InventoryEventType.Maintenance, BookCopyStatus.Retired => InventoryEventType.Retired, BookCopyStatus.Available => InventoryEventType.Restored, _ => InventoryEventType.StatusChanged };
        db.InventoryEvents.Add(Event(copy, type, fromStatus, copy.Status, fromCondition, copy.Condition, notes));
        audit.Add(db, "BookCopyStateChanged", nameof(BookCopy), copyId, $"Accession={copy.AccessionCode}; {fromStatus}->{copy.Status}; {fromCondition}->{copy.Condition}");
        await db.SaveChangesAsync();
        await RecalculateBookCountsAsync(db, copy.BookId);
        await db.SaveChangesAsync();
        return new(true, $"{copy.AccessionCode} is now {copy.Status}.");
    }

    public async Task<QrLabelResult> GenerateLabelAsync(int copyId)
    {
        authorization.Demand(Permission.ManageCatalog);
        await using var db = await factory.CreateDbContextAsync();
        var copy = await db.BookCopies.Include(x => x.Book).SingleOrDefaultAsync(x => x.BookCopyId == copyId);
        if (copy is null) return new(false, "The physical copy no longer exists.");
        var token = qr.CreateBookCopyToken(copy.BookCopyId, copy.AccessionCode);
        var png = qr.RenderPng(token);
        copy.LastQrIssuedAt = DateTime.UtcNow;
        db.InventoryEvents.Add(Event(copy, InventoryEventType.QrIssued, copy.Status, copy.Status, copy.Condition, copy.Condition, "Permanent encrypted QR label generated locally."));
        audit.Add(db, "BookCopyQrIssued", nameof(BookCopy), copyId, $"Accession={copy.AccessionCode}");
        await db.SaveChangesAsync();
        return new(true, "Encrypted QR label generated locally.", png, token, copy);
    }

    public async Task<QrLabelResult> ValidateAndFindAsync(string token)
    {
        authorization.Demand(Permission.ManageCatalog);
        var decoded = qr.Decode(token.Trim());
        if (!decoded.Success || decoded.Payload is null) return new(false, decoded.Message);
        await using var db = await factory.CreateDbContextAsync();
        var copy = await db.BookCopies.AsNoTracking().Include(x => x.Book).SingleOrDefaultAsync(x => x.BookCopyId == decoded.Payload.EntityId);
        if (copy is null || !string.Equals(copy.AccessionCode, decoded.Payload.Code, StringComparison.Ordinal))
            return new(false, "The QR code is authentic, but its inventory record no longer matches.");
        return new(true, $"Validated {copy.AccessionCode}: {copy.Book.Title} ({copy.Status}).", Copy: copy);
    }

    public static async Task RecalculateBookCountsAsync(LibraryDbContext db, int bookId)
    {
        var book = await db.Books.SingleAsync(x => x.BookId == bookId);
        book.TotalCopies = await db.BookCopies.CountAsync(x => x.BookId == bookId && x.Status != BookCopyStatus.Retired);
        book.AvailableCopies = await db.BookCopies.CountAsync(x => x.BookId == bookId && x.Status == BookCopyStatus.Available);
    }

    private static async Task<int> NextSequenceAsync(LibraryDbContext db, int bookId)
    {
        var prefix = $"LF-{bookId:D6}-";
        var codes = await db.BookCopies.Where(x => x.BookId == bookId).Select(x => x.AccessionCode).ToListAsync();
        return codes.Select(x => x.StartsWith(prefix) && int.TryParse(x[prefix.Length..], out var value) ? value : 0).DefaultIfEmpty().Max() + 1;
    }

    private InventoryEvent Event(BookCopy copy, InventoryEventType type, BookCopyStatus? fromStatus, BookCopyStatus? toStatus, BookCopyCondition? fromCondition, BookCopyCondition? toCondition, string? notes) =>
        new() { BookCopyId = copy.BookCopyId, EventType = type, FromStatus = fromStatus, ToStatus = toStatus, FromCondition = fromCondition, ToCondition = toCondition, Notes = notes, UserId = session.Current?.UserId };
}
