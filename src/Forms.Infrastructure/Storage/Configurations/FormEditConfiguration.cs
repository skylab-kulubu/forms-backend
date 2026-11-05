using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Infrastructure.Storage.Configurations;

public class FormEditConfiguration : IEntityTypeConfiguration<FormEdit>
{
    public void Configure(EntityTypeBuilder<FormEdit> builder)
    {
        builder.ToTable("FormEdits");

        builder.HasKey(e => new { e.FormId, e.Version });

        builder.HasIndex(e => new { e.FormId, e.BatchId }).IsUnique();
        builder.HasIndex(e => e.CreatedAt);

        builder.Property(e => e.Ops).HasColumnType("jsonb").IsRequired();

        builder.HasOne(e => e.Form).WithMany().HasForeignKey(e => e.FormId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(e => e.Form.Status != FormStatus.Deleted);
    }
}
