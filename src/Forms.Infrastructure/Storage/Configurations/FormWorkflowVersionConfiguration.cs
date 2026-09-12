using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Skylab.Forms.Infrastructure.Storage.Configurations;

public class FormWorkflowVersionConfiguration : IEntityTypeConfiguration<FormWorkflowVersion>
{
    public void Configure(EntityTypeBuilder<FormWorkflowVersion> builder)
    {
        builder.ToTable("WorkflowVersions");

        builder.HasKey(v => v.Id);

        builder.HasIndex(v => new { v.WorkflowId, v.Version }).IsUnique();

        // Bir workflow'un aynı anda en fazla bir yayınlanmış ve bir draft version'ı olur.
        builder.HasIndex(v => v.WorkflowId, "IX_WorkflowVersions_WorkflowId_Published")
            .IsUnique()
            .HasFilter($"\"Status\" = {(int)WorkflowStatus.Published}");

        builder.HasIndex(v => v.WorkflowId, "IX_WorkflowVersions_WorkflowId_Draft")
            .IsUnique()
            .HasFilter($"\"Status\" = {(int)WorkflowStatus.Draft}");

        builder.HasMany(v => v.Nodes)
            .WithOne(n => n.WorkflowVersion)
            .HasForeignKey(n => n.WorkflowVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(v => v.Transitions)
            .WithOne(t => t.WorkflowVersion)
            .HasForeignKey(t => t.WorkflowVersionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
