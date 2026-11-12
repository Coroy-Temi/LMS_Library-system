using LibraryManagementSystem.Models;
using LibraryManagementSystem.ViewModels;
using System.Windows;

namespace LibraryManagementSystem.Views;
public partial class BookCopiesWindow : Window
{
    private readonly BookCopiesViewModel viewModel;
    public BookCopiesWindow(BookCopiesViewModel viewModel){InitializeComponent();DataContext=this.viewModel=viewModel;}
    public Task LoadAsync(Book book)=>viewModel.LoadAsync(book);
}
