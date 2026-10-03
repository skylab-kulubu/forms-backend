using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;

namespace Skylab.Forms.Infrastructure.Storage.Configurations;

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

        builder.Property(f => f.Task).HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<FormTask>(v, jsonOptions)
            ).Metadata.SetValueComparer(new ValueComparer<FormTask?>(
                (c1, c2) => JsonSerializer.Serialize(c1, jsonOptions) == JsonSerializer.Serialize(c2, jsonOptions),
                c => JsonSerializer.Serialize(c, jsonOptions).GetHashCode(),
                c => JsonSerializer.Deserialize<FormTask>(JsonSerializer.Serialize(c, jsonOptions), jsonOptions)
            ));

        builder.Property(f => f.ClosesAt).IsRequired(false);
        builder.Property(f => f.TimeLimitMinutes).IsRequired(false);

        builder.HasQueryFilter(f => f.Status != FormStatus.Deleted);

        builder.HasOne(r => r.LinkedForm).WithMany().OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(f => f.Collaborators).WithOne(fc => fc.Form).HasForeignKey(fc => fc.FormId).OnDelete(DeleteBehavior.Cascade);
    }
}
