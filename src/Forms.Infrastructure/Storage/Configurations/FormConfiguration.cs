using Forms.Domain.Entities;
using Forms.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forms.Infrastructure.Storage.Configurations;

public class FormConfiguration : IEntityTypeConfiguration<Form>
{
     public void Configure(EntityTypeBuilder<Form> builder)
    {
        builder.ToTable("Forms");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.Title).IsRequired().HasMaxLength(100);
        builder.Property(f => f.Schema).HasColumnType("jsonb");

        builder.HasQueryFilter(f => f.Status != FormStatus.Deleted);

        builder.HasOne(r => r.LinkedForm).WithMany().OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(f => f.Collaborators).WithOne(fc => fc.Form).HasForeignKey(fc => fc.FormId).OnDelete(DeleteBehavior.Cascade);
    }
}