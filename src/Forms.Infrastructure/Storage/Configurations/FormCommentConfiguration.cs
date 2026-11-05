using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Infrastructure.Storage.Configurations;

public class FormCommentConfiguration : IEntityTypeConfiguration<FormComment>
{
    public void Configure(EntityTypeBuilder<FormComment> builder)
    {
        builder.ToTable("FormComments");

        builder.HasKey(c => c.Id);

        builder.HasIndex(c => new { c.FormId, c.CreatedAt });
        builder.HasIndex(c => c.ThreadId);

        builder.Property(c => c.FieldId).HasMaxLength(StudioLimits.MaxKeyLength).IsRequired();
        builder.Property(c => c.Body).HasMaxLength(StudioLimits.MaxCommentLength).IsRequired();

        builder.HasOne(c => c.Form).WithMany().HasForeignKey(c => c.FormId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<FormComment>().WithMany().HasForeignKey(c => c.ThreadId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(c => c.Form.Status != FormStatus.Deleted);
    }
}
