using Forms.Domain.Entities;
using Forms.Domain.Enums;
using Forms.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;

namespace Forms.Infrastructure.Storage.Configurations;

public class FormConfiguration : IEntityTypeConfiguration<Form>
{
     public void Configure(EntityTypeBuilder<Form> builder)
    {
        builder.ToTable("Forms");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.Title).IsRequired().HasMaxLength(100);

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        builder.Property(f => f.Schema).HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions), 
                v => JsonSerializer.Deserialize<List<FormSchemaItem>>(v, jsonOptions) ?? new List<FormSchemaItem>()
            ).Metadata.SetValueComparer(new ValueComparer<List<FormSchemaItem>>(
                (c1, c2) => JsonSerializer.Serialize(c1, jsonOptions) == JsonSerializer.Serialize(c2, jsonOptions),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => JsonSerializer.Deserialize<List<FormSchemaItem>>(JsonSerializer.Serialize(c, jsonOptions), jsonOptions)!
            ));

        builder.HasQueryFilter(f => f.Status != FormStatus.Deleted);

        builder.HasOne(r => r.LinkedForm).WithMany().OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(f => f.Collaborators).WithOne(fc => fc.Form).HasForeignKey(fc => fc.FormId).OnDelete(DeleteBehavior.Cascade);
    }
}