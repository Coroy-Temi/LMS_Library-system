using CommunityToolkit.Mvvm.ComponentModel;
using LibraryManagementSystem.Services;
namespace LibraryManagementSystem.ViewModels;
public partial class DashboardViewModel:ObservableObject
{
 readonly DashboardService service;
 [ObservableProperty] int totalBooks;[ObservableProperty]int availableBooks;[ObservableProperty]int activeMembers;[ObservableProperty]int overdueLoans;
 public DashboardViewModel(DashboardService service){this.service=service;_=LoadAsync();}
 public async Task LoadAsync(){var x=await service.GetAsync();TotalBooks=x.TotalBooks;AvailableBooks=x.AvailableBooks;ActiveMembers=x.ActiveMembers;OverdueLoans=x.OverdueLoans;}
}
