using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skylab.Forms.Infrastructure.AnswerFiles;

namespace Skylab.Forms.Infrastructure.Storage.Configurations;

public class AnswerFileLinkConfiguration : IEntityTypeConfiguration<AnswerFileLink>
{
    public void Configure(EntityTypeBuilder<AnswerFileLink> builder)
    {
        builder.ToTable("AnswerFileLinks");

        builder.HasKey(l => l.Id);

        // Cevaba yabancı anahtar yok: silinen cevabın bağı core'dan kaldırılana dek satır kalmalı.
        builder.HasIndex(l => new { l.FormId, l.UserId, l.MediaId }).IsUnique().HasFilter("\"ResponseId\" IS NULL");
        builder.HasIndex(l => new { l.ResponseId, l.MediaId }).IsUnique().HasFilter("\"ResponseId\" IS NOT NULL");

        builder.HasIndex(l => l.NextAttemptAt).HasFilter("\"NextAttemptAt\" IS NOT NULL");
    }
}
