using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Windows;

namespace LibraryManagementSystem.ViewModels;

public partial class BooksViewModel(CatalogService service, ReportService reports, CatalogImportService importer,
    IFileDialogService dialogs, IServiceProvider services) : ObservableObject
{
    public ObservableCollection<Book> Books { get; } = [];
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private Book? selectedBook;
    [ObservableProperty] private int page = 1;
    [ObservableProperty] private string statusMessage = string.Empty;

    [RelayCommand] private async Task LoadAsync() { Books.Clear(); foreach (var x in await service.SearchAsync(SearchText, Page)) Books.Add(x); }
    [RelayCommand] private async Task SearchAsync() { Page = 1; await LoadAsync(); }
    [RelayCommand] private async Task NextAsync() { Page++; await LoadAsync(); }
    [RelayCommand] private async Task PreviousAsync() { if (Page > 1) Page--; await LoadAsync(); }
    [RelayCommand] private Task NewBookAsync() => OpenEditorAsync(null);
    [RelayCommand] private async Task EditBookAsync() { if (SelectedBook is not null) await OpenEditorAsync(SelectedBook); else StatusMessage = "Select a book to edit."; }
    [RelayCommand] private async Task ManageCopiesAsync()
    {
        if(SelectedBook is null){StatusMessage="Select a book to manage its physical copies.";return;}
        var window=services.GetRequiredService<BookCopiesWindow>();await window.LoadAsync(SelectedBook);window.Owner=Application.Current.MainWindow;window.ShowDialog();await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedBook is null) { StatusMessage = "Select a book to delete."; return; }
        if (MessageBox.Show($"Delete '{SelectedBook.Title}'?", "Confirm deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var result = await service.DeleteAsync(SelectedBook.BookId);
        StatusMessage = result.Message;
        if (result.Success) { SelectedBook = null; await LoadAsync(); }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var dialog = new SaveFileDialog { Filter = "Excel workbook|*.xlsx", FileName = "inventory.xlsx" };
        if (dialog.ShowDialog() == true) await reports.ExportInventoryExcelAsync(dialog.FileName);
    }

    [RelayCommand]
    private async Task ImportCsvAsync()
    {
        var path = dialogs.PickBookCsv();
        if (path is null) return;
        var result = await importer.ImportBooksCsvAsync(path);
        StatusMessage = result.Message;
        if (result.Success) { Page = 1; await LoadAsync(); }
    }

    private async Task OpenEditorAsync(Book? book)
    {
        var window = services.GetRequiredService<BookEditorWindow>();
        await window.LoadAsync(book);
        window.Owner = Application.Current.MainWindow;
        if (window.ShowDialog() == true) { StatusMessage = book is null ? "Book created." : "Book updated."; await LoadAsync(); }
    }
}
