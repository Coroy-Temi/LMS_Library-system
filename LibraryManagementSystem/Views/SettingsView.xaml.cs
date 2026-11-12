using LibraryManagementSystem.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;

namespace LibraryManagementSystem.Views;

public partial class SettingsView : UserControl
{
    public SettingsView(SettingsViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.ChangePasswordRequested = () =>
        {
            var window = services.GetRequiredService<ChangePasswordWindow>();
            window.Owner = Window.GetWindow(this);
            window.ShowDialog();
        };
        viewModel.LoadCommand.Execute(null);
    }
}
