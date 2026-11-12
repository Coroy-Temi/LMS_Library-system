using LibraryManagementSystem.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace LibraryManagementSystem.Views;

public partial class LoginWindow : Window
{
    public LoginWindow(LoginViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PasswordProvider = () => Password.Password;
        viewModel.Close = result => { DialogResult = result; Close(); };
        viewModel.RegisterRequested = () =>
        {
            var registration = services.GetRequiredService<RegistrationWindow>();
            registration.Owner = this;
            if (registration.ShowDialog() == true)
            {
                DialogResult = true;
                Close();
            }
        };
    }
}
