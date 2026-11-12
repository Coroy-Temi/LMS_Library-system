using LibraryManagementSystem.Models;
using LibraryManagementSystem.ViewModels;
using System.Windows;

namespace LibraryManagementSystem.Views;

public partial class MemberEditorWindow : Window
{
    private readonly MemberEditorViewModel viewModel;
    public MemberEditorWindow(MemberEditorViewModel viewModel)
    {
        InitializeComponent(); DataContext = this.viewModel = viewModel;
        viewModel.Close = result => { DialogResult = result; Close(); };
    }
    public void Load(Member? member) => viewModel.Load(member);
}
