using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using System.Collections.ObjectModel;

namespace LibraryManagementSystem.ViewModels;

public partial class MemberPortalViewModel(MemberPortalService service, ReservationService reservationService) : ObservableObject
{
    public ObservableCollection<Loan> ActiveLoans { get; } = [];
    public ObservableCollection<Loan> LoanHistory { get; } = [];
    public ObservableCollection<Reservation> Reservations { get; } = [];
    [ObservableProperty] private string memberName = string.Empty;
    [ObservableProperty] private string memberCode = string.Empty;
    [ObservableProperty] private DateTime membershipExpiresAt;
    [ObservableProperty] private int activeLoanCount;
    [ObservableProperty] private int reservationCount;
    [ObservableProperty] private int booksRead;
    [ObservableProperty] private string? profileImagePath;
    [ObservableProperty] private Reservation? selectedReservation;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool isBusy;

    [RelayCommand]
    private async Task LoadAsync()
    {
        var snapshot = await service.GetAsync();
        MemberName = snapshot.Member.FullName;
        MemberCode = snapshot.Member.MemberCode;
        MembershipExpiresAt = snapshot.Member.ExpiresAt;
        ProfileImagePath = snapshot.Member.PhotoPath;
        ActiveLoanCount = snapshot.ActiveLoans.Count;
        ReservationCount = snapshot.Reservations.Count(x => x.Status is ReservationStatus.Waiting or ReservationStatus.ReadyForPickup);
        BooksRead = snapshot.LoanHistory.Count(x => x.ReturnedAt is not null);
        Replace(ActiveLoans, snapshot.ActiveLoans);
        Replace(LoanHistory, snapshot.LoanHistory);
        Replace(Reservations, snapshot.Reservations);
    }

    [RelayCommand]
    private async Task CancelReservationAsync()
    {
        if (IsBusy || SelectedReservation is null) return;
        IsBusy = true;
        try
        {
            var result = await reservationService.CancelForCurrentMemberAsync(SelectedReservation.ReservationId);
            StatusMessage = result.Message;
            if (result.Success) await LoadAsync();
        }
        finally { IsBusy = false; }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear(); foreach (var value in values) target.Add(value);
    }
}

public partial class BrowseCatalogViewModel(CatalogService catalog, ReservationService reservationService) : ObservableObject
{
    public ObservableCollection<Book> Books { get; } = [];
    [ObservableProperty] private string searchTerm = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private Book? selectedBook;
    [ObservableProperty] private string message = "Select an unavailable title to join its reservation queue.";

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Books.Clear();
            foreach (var book in await catalog.SearchAsync(SearchTerm, 1, 100)) Books.Add(book);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ReserveSelectedAsync()
    {
        if (IsBusy || SelectedBook is null)
        {
            Message = "Select a book first.";
            return;
        }
        IsBusy = true;
        try
        {
            var result = await reservationService.CreateForCurrentMemberAsync(SelectedBook.BookId);
            Message = result.Message;
            if (result.Success)
            {
                Books.Clear();
                foreach (var book in await catalog.SearchAsync(SearchTerm, 1, 100)) Books.Add(book);
            }
        }
        finally { IsBusy = false; }
    }
}
