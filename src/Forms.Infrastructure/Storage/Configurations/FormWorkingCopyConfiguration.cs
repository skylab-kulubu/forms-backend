using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Studio;

namespace Skylab.Forms.Infrastructure.Storage.Configurations;

public class FormWorkingCopyConfiguration : IEntityTypeConfiguration<FormWorkingCopy>
{
    public void Configure(EntityTypeBuilder<FormWorkingCopy> builder)
    {
        builder.ToTable("FormWorkingCopies");

        builder.HasKey(c => c.FormId);

        builder.Property(c => c.Document).HasColumnType("jsonb")
            .HasConversion(
                document => document.ToJsonString(),
                json => FormDocument.FromStored(json)
            ).Metadata.SetValueComparer(new ValueComparer<FormDocument>(
                (left, right) => ReferenceEquals(left, right) || left != null && right != null && left.ToJsonString() == right.ToJsonString(),
                document => document.ToJsonString().GetHashCode(),
                document => FormDocument.FromStored(document.ToJsonString())
            ));

        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasOne(c => c.Form).WithOne().HasForeignKey<FormWorkingCopy>(c => c.FormId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(c => c.Form.Status != FormStatus.Deleted);
    }
}
