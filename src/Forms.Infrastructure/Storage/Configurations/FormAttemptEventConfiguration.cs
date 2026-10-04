using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Skylab.Forms.Infrastructure.Storage.Configurations;

public class FormAttemptEventConfiguration : IEntityTypeConfiguration<FormAttemptEvent>
{
    public void Configure(EntityTypeBuilder<FormAttemptEvent> builder)
    {
        builder.ToTable("AttemptEvents");

        builder.HasKey(e => e.Id);

        builder.HasIndex(e => new { e.AttemptId, e.CreatedAt });

        builder.Property(e => e.Note).HasMaxLength(500).IsRequired(false);

        builder.HasQueryFilter(e => e.Attempt.Form.Status != FormStatus.Deleted);
    }
}
