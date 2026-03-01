using Forms.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;
using Forms.Domain.Entities;

namespace Forms.Infrastructure.Storage.Configurations;

public class FormComponentGroupConfiguration : IEntityTypeConfiguration<ComponentGroup>
{
    public void Configure(EntityTypeBuilder<ComponentGroup> builder)
    {
        builder.ToTable("ComponentGroup");

        builder.HasKey(cg => cg.Id);

        builder.Property(cg => cg.Title).IsRequired().HasMaxLength(100);

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        builder.Property(cg => cg.Schema).HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<List<FormSchemaItem>>(v, jsonOptions) ?? new List<FormSchemaItem>()
            ).Metadata.SetValueComparer(new ValueComparer<List<FormSchemaItem>>(
                (c1, c2) => JsonSerializer.Serialize(c1, jsonOptions) == JsonSerializer.Serialize(c2, jsonOptions),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => JsonSerializer.Deserialize<List<FormSchemaItem>>(JsonSerializer.Serialize(c, jsonOptions), jsonOptions)!
            ));
    }
}