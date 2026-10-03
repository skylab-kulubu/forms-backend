using Skylab.Forms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Skylab.Forms.Infrastructure.Storage.Configurations;

public class AccountErasureReceiptConfiguration : IEntityTypeConfiguration<AccountErasureReceipt>
{
    public void Configure(EntityTypeBuilder<AccountErasureReceipt> builder)
    {
        builder.ToTable("account_erasure_receipts");
        builder.HasKey(r => r.RequestId);
        builder.Property(r => r.RequestId).HasColumnName("request_id").ValueGeneratedNever();
        builder.Property(r => r.CompletedAt).HasColumnName("completed_at");
        builder.Property(r => r.Counts).HasColumnName("counts").HasColumnType("jsonb");
    }
}
