using System.Collections.ObjectModel;using CommunityToolkit.Mvvm.ComponentModel;using CommunityToolkit.Mvvm.Input;using LibraryManagementSystem.Models;using LibraryManagementSystem.Services;using Microsoft.Win32;using System.Windows;
namespace LibraryManagementSystem.ViewModels;
public partial class LoansViewModel:ObservableObject
{
 readonly LoanService service;readonly ReportService reports;public ObservableCollection<Loan> Loans{get;}=[];
 [ObservableProperty]Loan? selectedLoan;[ObservableProperty]int bookId;[ObservableProperty]int memberId;[ObservableProperty]int loanDays=14;
 public LoansViewModel(LoanService service,ReportService reports){this.service=service;this.reports=reports;_=LoadAsync();}
 [RelayCommand]async Task LoadAsync(){Loans.Clear();foreach(var x in await service.GetActiveAsync())Loans.Add(x);}
 [RelayCommand]async Task IssueAsync(){var r=await service.IssueBookAsync(BookId,MemberId,LoanDays);MessageBox.Show(r.Message,"Issue book");if(r.Success)await LoadAsync();}
 [RelayCommand]async Task ReturnAsync(){if(SelectedLoan is null)return;var r=await service.ReturnBookAsync(SelectedLoan.LoanId);MessageBox.Show(r.Success?$"{r.Message} Fine: {r.Fine:C}":r.Message,"Return book");await LoadAsync();}
 [RelayCommand]async Task ExportOverdueAsync(){var d=new SaveFileDialog{Filter="PDF document|*.pdf",FileName="overdue-loans.pdf"};if(d.ShowDialog()==true)await reports.ExportOverduePdfAsync(d.FileName);}
}
