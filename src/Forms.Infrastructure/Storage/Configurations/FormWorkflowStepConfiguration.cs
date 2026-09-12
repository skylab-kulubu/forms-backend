using Skylab.Forms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Skylab.Forms.Infrastructure.Storage.Configurations;

public class FormWorkflowStepConfiguration : IEntityTypeConfiguration<FormWorkflowStep>
{
    public void Configure(EntityTypeBuilder<FormWorkflowStep> builder)
    {
        builder.ToTable("WorkflowSteps");

        builder.HasKey(s => s.Id);

        // Bir başvuruda aynı anda tek açık adım bulunur: eşzamanlı dal çalışmasını
        // uygulama katmanı değil, bu kısıt engeller.
        builder.HasIndex(s => s.WorkflowInstanceId)
            .IsUnique()
            .HasFilter("\"CompletedAt\" IS NULL")
            .HasDatabaseName("IX_WorkflowSteps_WorkflowInstanceId_Open");

        // Bir cevap tek adıma aittir: tekrarlanan gönderim ikinci adım üretemez.
        builder.HasIndex(s => s.ResponseId).IsUnique();

        builder.HasIndex(s => new { s.WorkflowInstanceId, s.Sequence });

        builder.HasOne(s => s.Node)
            .WithMany()
            .HasForeignKey(s => s.NodeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Response)
            .WithMany()
            .HasForeignKey(s => s.ResponseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.PreviousStep)
            .WithMany()
            .HasForeignKey(s => s.PreviousStepId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.SelectedTransition)
            .WithMany()
            .HasForeignKey(s => s.SelectedTransitionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
