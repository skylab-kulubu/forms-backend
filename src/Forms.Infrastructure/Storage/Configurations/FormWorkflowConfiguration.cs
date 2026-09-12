using Skylab.Forms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Skylab.Forms.Infrastructure.Storage.Configurations;

public class FormWorkflowConfiguration : IEntityTypeConfiguration<FormWorkflow>
{
    public void Configure(EntityTypeBuilder<FormWorkflow> builder)
    {
        builder.ToTable("Workflows");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Name).IsRequired().HasMaxLength(100);

        builder.HasIndex(w => w.OwnerUserId);

        // Arşivlenmiş workflow'lar için global filtre yok: devam eden başvurular
        // arşivlenmiş bir tanıma bağlı kalabilir ve okunabilmeleri gerekir.
        builder.HasMany(w => w.Versions)
            .WithOne(v => v.Workflow)
            .HasForeignKey(v => v.WorkflowId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
