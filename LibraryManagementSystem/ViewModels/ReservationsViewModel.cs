using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;

namespace LibraryManagementSystem.ViewModels;

public partial class ReservationsViewModel(ReservationService service) : ObservableObject
{
    public ObservableCollection<Reservation> Reservations { get; } = [];

    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private Reservation? selectedReservation;
    [ObservableProperty] private int bookId;
    [ObservableProperty] private int memberId;
    [ObservableProperty] private bool isBusy;

    public async Task InitializeAsync() => await LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Reservations.Clear();
            foreach (var item in await service.GetActiveAsync(SearchText))
                Reservations.Add(item);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SearchAsync() => await LoadAsync();

    [RelayCommand]
    private async Task ReserveAsync()
    {
        var result = await service.CreateAsync(BookId, MemberId);
        MessageBox.Show(result.Message, "Reservation", MessageBoxButton.OK,
            result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        if (result.Success)
        {
            BookId = 0;
            MemberId = 0;
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        if (SelectedReservation is null)
            return;
        var result = await service.CancelAsync(SelectedReservation.ReservationId);
        MessageBox.Show(result.Message, "Reservation", MessageBoxButton.OK,
            result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        if (result.Success)
        {
            SelectedReservation = null;
            await LoadAsync();
        }
    }
}
