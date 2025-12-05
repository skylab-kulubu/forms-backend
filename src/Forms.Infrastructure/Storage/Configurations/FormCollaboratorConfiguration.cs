using Forms.Domain.Entities;
using Forms.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forms.Infrastructure.Storage.Configurations;

public class FormCollaboratorConfiguration : IEntityTypeConfiguration<FormCollaborator>
{
    public void Configure(EntityTypeBuilder<FormCollaborator> builder)
    {
        builder.ToTable("Collaborators");

        builder.HasKey(fc => new {fc.FormId, fc.UserId});

        builder.HasIndex(fc => fc.FormId).IsUnique().HasFilter($"\"Role\" = {(int)CollaboratorRole.Owner}");

        builder.HasOne(fc => fc.Form).WithMany(fc => fc.Collaborators).HasForeignKey(fc => fc.FormId).OnDelete(DeleteBehavior.Cascade);
        
        builder.HasQueryFilter(fc => fc.Form.Status != FormStatus.Deleted);

        builder.Property(fc => fc.Role).IsRequired();
    }
}