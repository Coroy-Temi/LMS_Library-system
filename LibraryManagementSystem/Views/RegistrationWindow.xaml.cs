using LibraryManagementSystem.ViewModels;
using System.Windows;

namespace LibraryManagementSystem.Views;

public partial class RegistrationWindow : Window
{
    public RegistrationWindow(RegistrationViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PasswordProvider = () => Password.Password;
        viewModel.ConfirmationProvider = () => Confirmation.Password;
        viewModel.Close = result => { DialogResult = result; Close(); };
    }
}
