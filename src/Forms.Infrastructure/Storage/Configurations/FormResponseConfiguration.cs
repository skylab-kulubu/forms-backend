using Forms.Domain.Entities;
using Forms.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forms.Infrastructure.Storage.Configurations;

public class FormResponseConfiguration : IEntityTypeConfiguration<FormResponse>
{
    public void Configure(EntityTypeBuilder<FormResponse> builder)
    {
        builder.ToTable("Responses");

        builder.HasKey(fr => fr.Id);

        builder.Property(fr => fr.UserId).IsRequired(false);
        builder.HasIndex(fr => fr.UserId);

        builder.Property(fr => fr.Data).HasColumnType("jsonb").IsRequired();

        builder.HasOne(fr => fr.Form).WithMany(f => f.Responses).HasForeignKey(fr => fr.FormId).OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(fr => fr.Form.Status != FormStatus.Deleted);

    }
}