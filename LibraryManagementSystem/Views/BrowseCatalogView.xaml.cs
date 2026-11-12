using LibraryManagementSystem.ViewModels;
using System.Windows.Controls;

namespace LibraryManagementSystem.Views;

public partial class BrowseCatalogView : UserControl
{
    public BrowseCatalogView(BrowseCatalogViewModel viewModel)
    {
        InitializeComponent(); DataContext = viewModel; viewModel.SearchCommand.Execute(null);
    }
}
