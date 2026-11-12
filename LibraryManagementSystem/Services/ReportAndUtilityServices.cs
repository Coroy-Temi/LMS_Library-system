using ClosedXML.Excel;
using LibraryManagementSystem.Data;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.IO;

namespace LibraryManagementSystem.Services;

public sealed class ReportService(
    IDbContextFactory<LibraryDbContext> factory,
    AuthorizationService authorization)
{
    public async Task ExportInventoryExcelAsync(string path)
    {
        authorization.Demand(Permission.ViewReports);
        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.Books.AsNoTracking().Include(x => x.Genre).OrderBy(x => x.Title).ToListAsync();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Inventory");
        sheet.Cell(1, 1).InsertTable(rows.Select(x => new
        {
            x.Title, x.Author, x.Isbn, Genre = x.Genre!.Name, x.TotalCopies, x.AvailableCopies
        }));
        sheet.Columns().AdjustToContents();
        workbook.SaveAs(path);
    }

    public async Task ExportOverduePdfAsync(string path)
    {
        authorization.Demand(Permission.ViewReports);
        QuestPDF.Settings.License = LicenseType.Community;
        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.Loans.AsNoTracking().Include(x => x.Book).Include(x => x.Member)
            .Where(x => x.ReturnedAt == null && x.DueAt < DateTime.UtcNow.Date).ToListAsync();
        Document.Create(document => document.Page(page =>
        {
            page.Margin(36);
            page.Header().Text("Overdue Loans").FontSize(24).FontColor(Colors.Teal.Darken2);
            page.Content().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2); columns.RelativeColumn(2); columns.RelativeColumn();
                });
                table.Header(header =>
                {
                    header.Cell().Text("Book").Bold(); header.Cell().Text("Member").Bold(); header.Cell().Text("Due").Bold();
                });
                foreach (var loan in rows)
                {
                    table.Cell().Text(loan.Book.Title); table.Cell().Text(loan.Member.FullName); table.Cell().Text(loan.DueAt.ToString("d"));
                }
            });
            page.Footer().AlignCenter().Text(text =>
            {
                text.Span("Generated "); text.Span(DateTime.Now.ToString("g"));
            });
        })).GeneratePdf(path);
    }
}

public sealed class BackupService(
    IDbContextFactory<LibraryDbContext> factory,
    AuthorizationService authorization)
{
    public async Task BackupAsync(string destination)
    {
        authorization.Demand(Permission.ManageSettings);
        await using var db = await factory.CreateDbContextAsync();
        var source = db.Database.GetDbConnection().DataSource;
        await db.Database.CloseConnectionAsync();
        File.Copy(source, destination, true);
    }

    public async Task RestoreAsync(string source)
    {
        authorization.Demand(Permission.ManageSettings);
        await using var db = await factory.CreateDbContextAsync();
        var destination = db.Database.GetDbConnection().DataSource;
        await db.Database.CloseConnectionAsync();
        File.Copy(source, destination, true);
    }
}
