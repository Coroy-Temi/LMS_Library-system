using LibraryManagementSystem.Models;
using LibraryManagementSystem.ViewModels;
using System.Windows;

namespace LibraryManagementSystem.Views;

public partial class BookEditorWindow : Window
{
    private readonly BookEditorViewModel viewModel;
    public BookEditorWindow(BookEditorViewModel viewModel)
    {
        InitializeComponent(); DataContext = this.viewModel = viewModel;
        viewModel.Close = result => { DialogResult = result; Close(); };
    }
    public Task LoadAsync(Book? book) => viewModel.LoadAsync(book);
}
