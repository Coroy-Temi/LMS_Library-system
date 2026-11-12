using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Services;

public sealed class ReservationService(
    IDbContextFactory<LibraryDbContext> factory,
    AuditService audit,
    AuthorizationService authorization,
    SessionService session)
{
    private const int PickupWindowDays = 3;
    private static readonly ReservationStatus[] ActiveStatuses =
        [ReservationStatus.Waiting, ReservationStatus.ReadyForPickup];

    public async Task<ServiceResult> CreateAsync(int bookId, int memberId)
    {
        authorization.Demand(Permission.ManageReservations);
        return await CreateCoreAsync(bookId, memberId);
    }

    public async Task<ServiceResult> CreateForCurrentMemberAsync(int bookId)
    {
        session.EnsureActive();
        var memberId = session.Current?.MemberId ??
            throw new UnauthorizedAccessException("This account is not linked to a library membership.");
        return await CreateCoreAsync(bookId, memberId);
    }

    private async Task<ServiceResult> CreateCoreAsync(int bookId, int memberId)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        await ExpireAndPromoteAsync(db, bookId);
        var book = await db.Books.SingleOrDefaultAsync(x => x.BookId == bookId);
        var member = await db.Members.SingleOrDefaultAsync(x => x.MemberId == memberId);

        if (book is null || member is null)
            return new(false, "Book or member was not found.");
        if (!member.IsActive || member.ExpiresAt.Date < DateTime.UtcNow.Date)
            return new(false, "The member account is inactive or expired.");
        if (await db.Loans.AnyAsync(x => x.BookId == bookId && x.MemberId == memberId && x.ReturnedAt == null))
            return new(false, "This member already has an active loan for the title.");
        if (await db.Reservations.AnyAsync(x => x.BookId == bookId && x.MemberId == memberId && ActiveStatuses.Contains(x.Status)))
            return new(false, "This member already has an active reservation for the title.");

        var protectedCopies = await db.Reservations.CountAsync(x =>
            x.BookId == bookId && x.Status == ReservationStatus.ReadyForPickup);
        if (book.AvailableCopies > protectedCopies)
            return new(false, "A copy is available now; issue the book instead of reserving it.");

        var lastPosition = await db.Reservations
            .Where(x => x.BookId == bookId && ActiveStatuses.Contains(x.Status))
            .MaxAsync(x => (int?)x.QueuePosition) ?? 0;
        var reservation = new Reservation
        {
            BookId = bookId,
            MemberId = memberId,
            QueuePosition = lastPosition + 1,
            Status = ReservationStatus.Waiting
        };
        db.Reservations.Add(reservation);
        await db.SaveChangesAsync();
        audit.Add(db, "ReservationCreated", nameof(Reservation), reservation.ReservationId,
            $"BookId={bookId}; MemberId={memberId}; QueuePosition={reservation.QueuePosition}");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return new(true, $"Reservation added at queue position {reservation.QueuePosition}.");
    }

    public async Task<ServiceResult> CancelAsync(int reservationId)
    {
        authorization.Demand(Permission.ManageReservations);
        return await CancelCoreAsync(reservationId, null);
    }

    public async Task<ServiceResult> CancelForCurrentMemberAsync(int reservationId)
    {
        session.EnsureActive();
        var memberId = session.Current?.MemberId ??
            throw new UnauthorizedAccessException("This account is not linked to a library membership.");
        return await CancelCoreAsync(reservationId, memberId);
    }

    private async Task<ServiceResult> CancelCoreAsync(int reservationId, int? requiredMemberId)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var reservation = await db.Reservations.SingleOrDefaultAsync(x => x.ReservationId == reservationId);
        if (reservation is null || !ActiveStatuses.Contains(reservation.Status))
            return new(false, "The reservation is not active.");
        if (requiredMemberId.HasValue && reservation.MemberId != requiredMemberId.Value)
            return new(false, "You can cancel only your own reservations.");

        var releasedReadyCopy = reservation.Status == ReservationStatus.ReadyForPickup;
        reservation.Status = ReservationStatus.Cancelled;
        reservation.ExpiresAt = null;
        audit.Add(db, "ReservationCancelled", nameof(Reservation), reservation.ReservationId,
            $"BookId={reservation.BookId}; MemberId={reservation.MemberId}");
        await db.SaveChangesAsync();
        await ReorderQueueAsync(db, reservation.BookId);
        if (releasedReadyCopy)
            await PromoteNextAsync(db, reservation.BookId);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return new(true, "Reservation cancelled.");
    }

    public async Task<List<Reservation>> GetActiveAsync(string? search = null)
    {
        authorization.Demand(Permission.ManageReservations);
        await using var db = await factory.CreateDbContextAsync();
        await ExpireAndPromoteAsync(db);
        await db.SaveChangesAsync();

        var query = db.Reservations.AsNoTracking()
            .Include(x => x.Book)
            .Include(x => x.Member)
            .Where(x => ActiveStatuses.Contains(x.Status));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                x.Book.Title.Contains(term) ||
                x.Member.FullName.Contains(term) ||
                x.Member.MemberCode.Contains(term));
        }
        return await query.OrderBy(x => x.Book.Title)
            .ThenBy(x => x.QueuePosition)
            .ToListAsync();
    }

    public async Task ExpireAndPromoteAsync(LibraryDbContext db, int? bookId = null)
    {
        var now = DateTime.UtcNow;
        var expired = await db.Reservations.Where(x =>
            x.Status == ReservationStatus.ReadyForPickup &&
            x.ExpiresAt != null && x.ExpiresAt < now &&
            (!bookId.HasValue || x.BookId == bookId.Value)).ToListAsync();
        var affectedBooks = expired.Select(x => x.BookId).Distinct().ToList();
        foreach (var item in expired)
        {
            item.Status = ReservationStatus.Expired;
            audit.Add(db, "ReservationExpired", nameof(Reservation), item.ReservationId,
                $"BookId={item.BookId}; MemberId={item.MemberId}");
        }
        if (bookId.HasValue && !affectedBooks.Contains(bookId.Value))
            affectedBooks.Add(bookId.Value);
        foreach (var affectedBookId in affectedBooks)
        {
            await ReorderQueueAsync(db, affectedBookId);
            await PromoteNextAsync(db, affectedBookId);
        }
        await db.SaveChangesAsync();
    }

    public async Task PromoteNextAsync(LibraryDbContext db, int bookId)
    {
        var book = await db.Books.SingleOrDefaultAsync(x => x.BookId == bookId);
        if (book is null || book.AvailableCopies < 1)
            return;
        var readyCount = await db.Reservations.CountAsync(x =>
            x.BookId == bookId && x.Status == ReservationStatus.ReadyForPickup);
        var promotableCopies = Math.Max(0, book.AvailableCopies - readyCount);
        if (promotableCopies == 0)
            return;
        var waiting = await db.Reservations.Where(x =>
                x.BookId == bookId && x.Status == ReservationStatus.Waiting)
            .OrderBy(x => x.QueuePosition)
            .Take(promotableCopies)
            .ToListAsync();
        foreach (var item in waiting)
        {
            item.Status = ReservationStatus.ReadyForPickup;
            item.ExpiresAt = DateTime.UtcNow.AddDays(PickupWindowDays);
            audit.Add(db, "ReservationReady", nameof(Reservation), item.ReservationId,
                $"BookId={bookId}; MemberId={item.MemberId}; ExpiresAt={item.ExpiresAt:O}");
        }
        await ReorderQueueAsync(db, bookId);
    }

    private static async Task ReorderQueueAsync(LibraryDbContext db, int bookId)
    {
        var active = await db.Reservations.Where(x =>
                x.BookId == bookId && ActiveStatuses.Contains(x.Status))
            .OrderByDescending(x => x.Status == ReservationStatus.ReadyForPickup)
            .ThenBy(x => x.QueuePosition)
            .ThenBy(x => x.ReservedAt)
            .ToListAsync();
        for (var index = 0; index < active.Count; index++)
            active[index].QueuePosition = index + 1;
    }
}
