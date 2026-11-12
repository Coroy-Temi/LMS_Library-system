using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;

namespace LibraryManagementSystem.ViewModels;

public partial class BookCopiesViewModel(InventoryService inventory) : ObservableObject
{
    public ObservableCollection<BookCopy> Copies { get; } = [];
    public ObservableCollection<InventoryEvent> History { get; } = [];
    [ObservableProperty] private int bookId;
    [ObservableProperty] private string bookTitle = "Physical copies";
    [ObservableProperty] private BookCopy? selectedCopy;
    [ObservableProperty] private string notes = "";
    [ObservableProperty] private string scanText = "";
    [ObservableProperty] private string statusMessage = "";

    public async Task LoadAsync(Book book) { BookId=book.BookId; BookTitle=$"{book.Title} — physical copies"; await RefreshAsync(); }
    [RelayCommand] private async Task RefreshAsync() { Copies.Clear(); foreach(var copy in await inventory.GetCopiesAsync(BookId)) Copies.Add(copy); }
    [RelayCommand] private async Task AddCopyAsync() { var result=await inventory.AddCopyAsync(BookId,BookCopyCondition.Good,Notes); StatusMessage=result.Message; if(result.Success) await RefreshAsync(); }
    [RelayCommand] private Task MarkMaintenanceAsync()=>ChangeAsync(BookCopyStatus.Maintenance,BookCopyCondition.Damaged);
    [RelayCommand] private Task MarkLostAsync()=>ChangeAsync(BookCopyStatus.Lost,BookCopyCondition.Lost);
    [RelayCommand] private Task RestoreAsync()=>ChangeAsync(BookCopyStatus.Available,SelectedCopy?.Condition==BookCopyCondition.Lost?BookCopyCondition.Fair:null);
    [RelayCommand] private Task RetireAsync()=>ChangeAsync(BookCopyStatus.Retired,null);

    [RelayCommand]
    private async Task GenerateQrAsync()
    {
        if(SelectedCopy is null){StatusMessage="Select a physical copy first.";return;}
        var result=await inventory.GenerateLabelAsync(SelectedCopy.BookCopyId);
        StatusMessage=result.Message;
        if(!result.Success || result.Png is null)return;
        var dialog=new SaveFileDialog{Filter="PNG image|*.png",FileName=$"{SelectedCopy.AccessionCode}-secure-qr.png"};
        if(dialog.ShowDialog()==true){await File.WriteAllBytesAsync(dialog.FileName,result.Png);StatusMessage=$"Secure label saved to {dialog.FileName}";}
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ValidateScanAsync()
    {
        var result=await inventory.ValidateAndFindAsync(ScanText);
        StatusMessage=result.Message;
        if(result.Success && result.Copy is not null) SelectedCopy=Copies.FirstOrDefault(x=>x.BookCopyId==result.Copy.BookCopyId);
    }

    partial void OnSelectedCopyChanged(BookCopy? value)=>_ = LoadHistoryAsync(value);
    private async Task LoadHistoryAsync(BookCopy? copy)
    {
        History.Clear(); if(copy is null)return;
        foreach(var item in await inventory.GetHistoryAsync(copy.BookCopyId))History.Add(item);
    }
    private async Task ChangeAsync(BookCopyStatus status,BookCopyCondition? condition)
    {
        if(SelectedCopy is null){StatusMessage="Select a physical copy first.";return;}
        var result=await inventory.ChangeStateAsync(SelectedCopy.BookCopyId,status,condition,Notes);
        StatusMessage=result.Message;if(result.Success)await RefreshAsync();
    }
}
