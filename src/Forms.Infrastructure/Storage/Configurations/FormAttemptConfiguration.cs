using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;

namespace Skylab.Forms.Infrastructure.Storage.Configurations;

public class FormAttemptConfiguration : IEntityTypeConfiguration<FormAttempt>
{
    public void Configure(EntityTypeBuilder<FormAttempt> builder)
    {
        builder.ToTable("Attempts");

        builder.HasKey(a => a.Id);

        builder.HasIndex(a => new { a.FormId, a.UserId, a.WorkflowStepId })
            .IsUnique()
            .HasDatabaseName("IX_Attempts_FormId_UserId_WorkflowStepId");

        builder.HasIndex(a => new { a.FormId, a.UserId })
            .IsUnique()
            .HasFilter("\"WorkflowStepId\" IS NULL")
            .HasDatabaseName("IX_Attempts_FormId_UserId_Standalone");

        builder.HasIndex(a => new { a.Status, a.DeadlineAt })
            .HasDatabaseName("IX_Attempts_Status_DeadlineAt");

        builder.HasIndex(a => a.ResponseId);

        builder.HasOne(a => a.Form)
            .WithMany()
            .HasForeignKey(a => a.FormId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FormResponse>()
            .WithMany()
            .HasForeignKey(a => a.ResponseId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<FormWorkflowStep>()
            .WithMany()
            .HasForeignKey(a => a.WorkflowStepId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(a => a.Events)
            .WithOne(e => e.Attempt)
            .HasForeignKey(e => e.AttemptId)
            .OnDelete(DeleteBehavior.Cascade);

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        builder.Property(a => a.DraftSnapshot)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<List<FormResponseSchemaItem>>(v, jsonOptions)
            )
            .Metadata.SetValueComparer(new ValueComparer<List<FormResponseSchemaItem>?>(
                (c1, c2) => JsonSerializer.Serialize(c1, jsonOptions) == JsonSerializer.Serialize(c2, jsonOptions),
                c => JsonSerializer.Serialize(c, jsonOptions).GetHashCode(),
                c => JsonSerializer.Deserialize<List<FormResponseSchemaItem>>(JsonSerializer.Serialize(c, jsonOptions), jsonOptions)
            ));

        builder.HasQueryFilter(a => a.Form.Status != FormStatus.Deleted);

        builder.Property<uint>("xmin").IsRowVersion();
    }
}
