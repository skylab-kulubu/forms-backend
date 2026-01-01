using Forms.Domain.Entities;
using Forms.Domain.Enums;
using Forms.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;

namespace Forms.Infrastructure.Storage.Configurations;

public class FormResponseConfiguration : IEntityTypeConfiguration<FormResponse>
{
    public void Configure(EntityTypeBuilder<FormResponse> builder)
    {
        builder.ToTable("Responses");

        builder.HasKey(fr => fr.Id);

        builder.Property(fr => fr.UserId).IsRequired(false);
        builder.HasIndex(fr => fr.UserId);

        builder.Property(fr => fr.ReviewNote).HasMaxLength(500).IsRequired(false);

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        builder.Property(fr => fr.Data)
            .HasColumnType("jsonb")
            .IsRequired()
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<List<FormResponseSchemaItem>>(v, jsonOptions) ?? new List<FormResponseSchemaItem>()
            )
            .Metadata.SetValueComparer(new ValueComparer<List<FormResponseSchemaItem>>(
                (c1, c2) => JsonSerializer.Serialize(c1, jsonOptions) == JsonSerializer.Serialize(c2, jsonOptions),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => JsonSerializer.Deserialize<List<FormResponseSchemaItem>>(JsonSerializer.Serialize(c, jsonOptions), jsonOptions)!
            ));

        builder.HasOne(fr => fr.Form).WithMany(f => f.Responses).HasForeignKey(fr => fr.FormId).OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(fr => fr.Form.Status != FormStatus.Deleted);

    }
}