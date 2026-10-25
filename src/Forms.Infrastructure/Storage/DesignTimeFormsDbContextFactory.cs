using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Skylab.Forms.Infrastructure.Storage;

/// <summary>
/// dotnet ef, migration eklerken uygulamayı ayarlarıyla açmadan DbContext'i buradan kurar; migration eklemek veritabanına
/// bağlanmaz. Veritabanını uygulama açılışta kendisi günceller.
/// </summary>
public sealed class DesignTimeFormsDbContextFactory : IDesignTimeDbContextFactory<FormsDbContext>
{
    public FormsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<FormsDbContext>().UseNpgsql("Host=localhost;Database=forms_db").Options);
}
