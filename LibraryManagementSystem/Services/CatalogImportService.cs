using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.IO;
using System.Text;

namespace LibraryManagementSystem.Services;

public sealed record CatalogImportResult(bool Success, string Message, int Imported, int Skipped);

public sealed class CatalogImportService(
    IDbContextFactory<LibraryDbContext> factory,
    AuthorizationService authorization,
    AuditService audit)
{
    public async Task<CatalogImportResult> ImportBooksCsvAsync(string path, CancellationToken cancellationToken = default)
    {
        authorization.Demand(Permission.ManageCatalog);
        if (!File.Exists(path)) return new(false, "The selected CSV file no longer exists.", 0, 0);
        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        if (lines.Length < 2) return new(false, "The CSV file has no book rows.", 0, 0);
        var headers = ParseCsvLine(lines[0]).Select((name, index) => (name: name.Trim(), index))
            .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
        if (!headers.ContainsKey("Title") || !headers.ContainsKey("Author"))
            return new(false, "CSV headers must include Title and Author.", 0, lines.Length - 1);

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var genres = await db.Genres.ToDictionaryAsync(x => x.Name, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var existingIsbns = (await db.Books.Where(x => x.Isbn != null).Select(x => x.Isbn!).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var imported = 0; var skipped = 0;
        for (var lineNumber = 1; lineNumber < lines.Length; lineNumber++)
        {
            if (string.IsNullOrWhiteSpace(lines[lineNumber])) continue;
            var row = ParseCsvLine(lines[lineNumber]);
            string Get(string name) => headers.TryGetValue(name, out var index) && index < row.Count ? row[index].Trim() : string.Empty;
            var title = Get("Title"); var author = Get("Author"); var isbn = Get("ISBN");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(author) ||
                (!string.IsNullOrWhiteSpace(isbn) && existingIsbns.Contains(isbn))) { skipped++; continue; }
            var total = int.TryParse(Get("TotalCopies"), out var parsedTotal) ? parsedTotal : 1;
            var available = int.TryParse(Get("AvailableCopies"), out var parsedAvailable) ? parsedAvailable : total;
            if (total < 1 || available < 0 || available > total) { skipped++; continue; }
            var genreName = Get("Genre"); Genre? genre = null;
            if (!string.IsNullOrWhiteSpace(genreName) && !genres.TryGetValue(genreName, out genre))
            {
                genre = new Genre { Name = genreName }; db.Genres.Add(genre); genres[genreName] = genre;
            }
            int? publicationYear = int.TryParse(Get("PublicationYear"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) ? year : null;
            if (publicationYear is { } value && (value < 1000 || value > DateTime.UtcNow.Year + 1)) { skipped++; continue; }
            db.Books.Add(new Book
            {
                Title = title, Author = author, Isbn = string.IsNullOrWhiteSpace(isbn) ? null : isbn,
                Genre = genre, Publisher = NullIfEmpty(Get("Publisher")), PublicationYear = publicationYear,
                Location = NullIfEmpty(Get("Location")), Description = NullIfEmpty(Get("Description")),
                Condition = NullIfEmpty(Get("Condition")) ?? "Good", TotalCopies = total, AvailableCopies = available,
                CoverImagePath = NullIfEmpty(Get("CoverImagePath"))
            });
            if (!string.IsNullOrWhiteSpace(isbn)) existingIsbns.Add(isbn);
            imported++;
        }
        if (imported == 0) return new(false, $"No books were imported. {skipped} rows were skipped.", 0, skipped);
        await db.SaveChangesAsync(cancellationToken);
        audit.Add(db, "BooksImportedCsv", nameof(Book), details: $"File={Path.GetFileName(path)}; Imported={imported}; Skipped={skipped}");
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(true, $"Imported {imported} books; skipped {skipped} rows.", imported, skipped);
    }

    internal static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>(); var value = new StringBuilder(); var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var ch = line[index];
            if (ch == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"') { value.Append('"'); index++; }
                else quoted = !quoted;
            }
            else if (ch == ',' && !quoted) { values.Add(value.ToString()); value.Clear(); }
            else value.Append(ch);
        }
        values.Add(value.ToString()); return values;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
