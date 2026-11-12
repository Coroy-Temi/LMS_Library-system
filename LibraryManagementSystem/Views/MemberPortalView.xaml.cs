using LibraryManagementSystem.ViewModels;
using System.Windows.Controls;

namespace LibraryManagementSystem.Views;

public partial class MemberPortalView : UserControl
{
    public MemberPortalView(MemberPortalViewModel viewModel)
    {
        InitializeComponent(); DataContext = viewModel; viewModel.LoadCommand.Execute(null);
    }
}
