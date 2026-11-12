using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace LibraryManagementSystem.Data;
public sealed class LibraryDbContextFactory : IDesignTimeDbContextFactory<LibraryDbContext>
{
 public LibraryDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<LibraryDbContext>().UseSqlite("Data Source=Library.db").Options);
}
