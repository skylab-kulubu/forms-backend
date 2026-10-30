using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Skylab.Forms.Infrastructure.Storage;

public sealed class DesignTimeFormsDbContextFactory : IDesignTimeDbContextFactory<FormsDbContext>
{
    public FormsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<FormsDbContext>().UseNpgsql("Host=localhost;Database=forms_db").Options);
}
