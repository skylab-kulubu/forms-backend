using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Skylab.Forms.Infrastructure.Storage.Configurations;

public class FormWorkflowTransitionConfiguration : IEntityTypeConfiguration<FormWorkflowTransition>
{
    public void Configure(EntityTypeBuilder<FormWorkflowTransition> builder)
    {
        builder.ToTable("WorkflowTransitions");

        builder.HasKey(t => t.Id);

        // Aynı kaynak ve tetik için iki transition aynı önceliği paylaşamaz:
        // rota seçiminin determinizmi buna dayanır.
        builder.HasIndex(t => new { t.WorkflowVersionId, t.SourceNodeId, t.Trigger, t.Priority })
            .IsUnique()
            .HasDatabaseName("IX_WorkflowTransitions_Source_Trigger_Priority");

        builder.HasOne(t => t.SourceNode)
            .WithMany()
            .HasForeignKey(t => t.SourceNodeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.TargetNode)
            .WithMany()
            .HasForeignKey(t => t.TargetNodeId)
            .OnDelete(DeleteBehavior.Cascade);

        // Koşullar jsonb olarak saklanır; enum'lar okunabilir kalsın diye string yazılır.
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        builder.Property(t => t.Condition)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<WorkflowConditionGroup>(v, jsonOptions)
            )
            .Metadata.SetValueComparer(new ValueComparer<WorkflowConditionGroup>(
                (c1, c2) => JsonSerializer.Serialize(c1, jsonOptions) == JsonSerializer.Serialize(c2, jsonOptions),
                c => JsonSerializer.Serialize(c, jsonOptions).GetHashCode(),
                c => JsonSerializer.Deserialize<WorkflowConditionGroup>(JsonSerializer.Serialize(c, jsonOptions), jsonOptions)!
            ));
    }
}
