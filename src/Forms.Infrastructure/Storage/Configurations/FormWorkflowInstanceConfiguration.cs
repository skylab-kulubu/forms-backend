using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Skylab.Forms.Infrastructure.Storage.Configurations;

public class FormWorkflowInstanceConfiguration : IEntityTypeConfiguration<FormWorkflowInstance>
{
    public void Configure(EntityTypeBuilder<FormWorkflowInstance> builder)
    {
        builder.ToTable("WorkflowInstances");

        builder.HasKey(i => i.Id);

        builder.HasIndex(i => new { i.WorkflowId, i.UserId, i.Status });

        // Bir kullanıcının aynı akışta tek aktif başvurusu olur. AllowMultipleRuns
        // ardışık çalıştırmaya izin verir, eşzamanlı olana değil: eşzamanlı iki ilk
        // gönderimden yalnızca biri başvuru yaratabilsin.
        builder.HasIndex(i => new { i.WorkflowId, i.UserId }, "IX_WorkflowInstances_WorkflowId_UserId_Active")
            .IsUnique()
            .HasFilter($"\"Status\" = {(int)WorkflowInstanceStatus.Active}");

        builder.HasOne(i => i.Workflow)
            .WithMany()
            .HasForeignKey(i => i.WorkflowId)
            .OnDelete(DeleteBehavior.Restrict);

        // Başvurusu olan bir version silinemez: rota geçmişi ona bağlı.
        builder.HasOne(i => i.WorkflowVersion)
            .WithMany()
            .HasForeignKey(i => i.WorkflowVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(i => i.Steps)
            .WithOne(s => s.WorkflowInstance)
            .HasForeignKey(s => s.WorkflowInstanceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Postgres'te rowversion yoktur. uint + OnAddOrUpdate + concurrency token
        // üçlüsü Npgsql'i sistem kolonu xmin'e yönlendirir; eşzamanlı iki adım
        // açma denemesinden yalnızca biri commit edebilir.
        builder.Property<uint>("xmin").IsRowVersion();
    }
}
