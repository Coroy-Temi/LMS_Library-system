using System.Windows.Controls;
using LibraryManagementSystem.ViewModels;

namespace LibraryManagementSystem.Views;

public partial class ReservationsView : UserControl
{
    public ReservationsView(ReservationsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
