using LibraryManagementSystem.ViewModels;
using System.Windows;

namespace LibraryManagementSystem.Views;

public partial class ChangePasswordWindow : Window
{
    public ChangePasswordWindow(ChangePasswordViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CurrentPasswordProvider = () => CurrentPassword.Password;
        viewModel.NewPasswordProvider = () => NewPassword.Password;
        viewModel.ConfirmationProvider = () => Confirmation.Password;
        viewModel.Close = result => { DialogResult = result; Close(); };
    }
}
