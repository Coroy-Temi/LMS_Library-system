using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using System.Collections.ObjectModel;

namespace LibraryManagementSystem.ViewModels;

public partial class BookEditorViewModel(CatalogService catalog) : ObservableObject
{
    public ObservableCollection<Genre> Genres { get; } = [];
    public IReadOnlyList<string> Conditions { get; } = ["New", "Good", "Fair", "Damaged", "Lost"];
    [ObservableProperty] private Book item = new();
    [ObservableProperty] private string dialogTitle = "Add book";
    [ObservableProperty] private string message = string.Empty;
    [ObservableProperty] private bool isBusy;
    public Action<bool>? Close { get; set; }

    public async Task LoadAsync(Book? source)
    {
        Genres.Clear(); foreach (var genre in await catalog.GetGenresAsync()) Genres.Add(genre);
        Item = source is null ? new Book { TotalCopies = 1, AvailableCopies = 1, Condition = "Good" } : Clone(source);
        DialogTitle = source is null ? "Add a new book" : "Edit book";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var result = await catalog.SaveAsync(Item);
            Message = result.Message;
            if (result.Success) Close?.Invoke(true);
        }
        finally { IsBusy = false; }
    }

    private static Book Clone(Book x) => new()
    {
        BookId = x.BookId, Title = x.Title, Author = x.Author, Isbn = x.Isbn,
        Publisher = x.Publisher, PublicationYear = x.PublicationYear, Location = x.Location,
        Description = x.Description, Condition = x.Condition, TotalCopies = x.TotalCopies,
        AvailableCopies = x.AvailableCopies, CoverImagePath = x.CoverImagePath, GenreId = x.GenreId
    };
}

public partial class MemberEditorViewModel(MemberService members) : ObservableObject
{
    [ObservableProperty] private Member item = new();
    [ObservableProperty] private string dialogTitle = "Add member";
    [ObservableProperty] private string message = string.Empty;
    [ObservableProperty] private bool isBusy;
    public Action<bool>? Close { get; set; }

    public void Load(Member? source)
    {
        Item = source is null ? new Member() : Clone(source);
        DialogTitle = source is null ? "Register a member" : "Edit member";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var result = await members.SaveAsync(Item);
            Message = result.Message;
            if (result.Success) Close?.Invoke(true);
        }
        finally { IsBusy = false; }
    }

    private static Member Clone(Member x) => new()
    {
        MemberId = x.MemberId, MemberCode = x.MemberCode, FullName = x.FullName, Email = x.Email,
        Phone = x.Phone, Address = x.Address, PhotoPath = x.PhotoPath, JoinedAt = x.JoinedAt,
        ExpiresAt = x.ExpiresAt, IsActive = x.IsActive
    };
}
