using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.Views;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Windows;

namespace LibraryManagementSystem.ViewModels;

public partial class MembersViewModel(MemberService service, IServiceProvider services) : ObservableObject
{
    public ObservableCollection<Member> Members { get; } = [];
    public ObservableCollection<Loan> History { get; } = [];
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private Member? selectedMember;
    [ObservableProperty] private string statusMessage = string.Empty;

    [RelayCommand] private async Task LoadAsync() { Members.Clear(); foreach (var x in await service.SearchAsync(SearchText)) Members.Add(x); }
    [RelayCommand] private Task SearchAsync() => LoadAsync();
    [RelayCommand] private Task NewMemberAsync() => OpenEditorAsync(null);
    [RelayCommand] private async Task EditMemberAsync() { if (SelectedMember is not null) await OpenEditorAsync(SelectedMember); else StatusMessage = "Select a member to edit."; }
    [RelayCommand] private async Task ViewHistoryAsync() { History.Clear(); if (SelectedMember is null) { StatusMessage = "Select a member first."; return; } foreach (var x in await service.HistoryAsync(SelectedMember.MemberId)) History.Add(x); StatusMessage = $"Loaded {History.Count} borrowing records."; }

    private async Task OpenEditorAsync(Member? member)
    {
        var window = services.GetRequiredService<MemberEditorWindow>();
        window.Load(member); window.Owner = Application.Current.MainWindow;
        if (window.ShowDialog() == true) { StatusMessage = member is null ? "Member registered." : "Member updated."; await LoadAsync(); }
    }
}
