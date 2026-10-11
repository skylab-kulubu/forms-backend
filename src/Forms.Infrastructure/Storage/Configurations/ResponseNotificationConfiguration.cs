using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skylab.Forms.Infrastructure.ResponseNotifications;

namespace Skylab.Forms.Infrastructure.Storage.Configurations;

public class ResponseNotificationConfiguration : IEntityTypeConfiguration<ResponseNotification>
{
    public void Configure(EntityTypeBuilder<ResponseNotification> builder)
    {
        builder.ToTable("ResponseNotifications");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Payload).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(n => n.NextAttemptAt);
    }
}
