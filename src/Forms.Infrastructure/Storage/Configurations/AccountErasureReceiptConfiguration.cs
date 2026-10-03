using Skylab.Forms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Skylab.Forms.Infrastructure.Storage.Configurations;

/// <summary>
/// Tablo ve kolon adları hesap silme sözleşmesinden gelir (core
/// docs/account-erasure-command.md §3); bu yüzden Forms'un öbür tablolarından farklı yazılır.
/// </summary>
public class AccountErasureReceiptConfiguration : IEntityTypeConfiguration<AccountErasureReceipt>
{
    public void Configure(EntityTypeBuilder<AccountErasureReceipt> builder)
    {
        builder.ToTable("account_erasure_receipts", table =>
            table.HasCheckConstraint("account_erasure_receipts_counts_object", "jsonb_typeof(counts) = 'object'"));

        builder.HasKey(r => r.RequestId);

        builder.Property(r => r.RequestId).HasColumnName("request_id").ValueGeneratedNever();
        builder.Property(r => r.CompletedAt).HasColumnName("completed_at").IsRequired();
        builder.Property(r => r.Counts).HasColumnName("counts").HasColumnType("jsonb").IsRequired();
    }
}
