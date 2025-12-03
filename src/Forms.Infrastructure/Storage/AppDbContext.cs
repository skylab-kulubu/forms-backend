using Forms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Forms.Infrastructure.Storage;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Form> Forms { get; set; }
    public DbSet<FormCollaborator> Collaborators { get; set; }
    public DbSet<FormResponse> Responses { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        SetTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void SetTimestamps()
    {
        var entries = ChangeTracker.Entries<BaseEntity>().Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            var now = DateTime.UtcNow;

            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = null;
            }
            if (entry.State == EntityState.Modified)
            {
                entry.Property(x => x.CreatedAt).IsModified = false; 
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}