using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Infrastructure.Storage.Configurations;

public class FormCommentReadConfiguration : IEntityTypeConfiguration<FormCommentRead>
{
    public void Configure(EntityTypeBuilder<FormCommentRead> builder)
    {
        builder.ToTable("FormCommentReads");

        builder.HasKey(r => new { r.ThreadId, r.UserId });

        builder.HasIndex(r => r.UserId);

        builder.HasOne(r => r.Thread).WithMany().HasForeignKey(r => r.ThreadId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(r => r.Thread.Form.Status != FormStatus.Deleted);
    }
}
