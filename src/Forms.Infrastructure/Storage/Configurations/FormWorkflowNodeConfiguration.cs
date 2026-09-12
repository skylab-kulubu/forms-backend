using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Skylab.Forms.Infrastructure.Storage.Configurations;

public class FormWorkflowNodeConfiguration : IEntityTypeConfiguration<FormWorkflowNode>
{
    public void Configure(EntityTypeBuilder<FormWorkflowNode> builder)
    {
        builder.ToTable("WorkflowNodes");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.NodeKey).IsRequired().HasMaxLength(WorkflowLimits.MaxNodeKeyLength);

        builder.HasIndex(n => new { n.WorkflowVersionId, n.NodeKey }).IsUnique();

        // Bir form bir version'da en fazla bir kez yer alabilir.
        builder.HasIndex(n => new { n.WorkflowVersionId, n.FormId }).IsUnique();

        // Version başına tek başlangıç node'u.
        builder.HasIndex(n => n.WorkflowVersionId)
            .IsUnique()
            .HasFilter("\"IsStart\"")
            .HasDatabaseName("IX_WorkflowNodes_WorkflowVersionId_Start");

        builder.HasIndex(n => n.FormId);

        builder.HasOne<Form>()
            .WithMany()
            .HasForeignKey(n => n.FormId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
