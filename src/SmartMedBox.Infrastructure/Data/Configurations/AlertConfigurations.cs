using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;

namespace SmartMedBox.Infrastructure.Data.Configurations;

public sealed class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        builder.ToTable("Alerts", table =>
            table.HasCheckConstraint(
                "CK_Alerts_AlertType",
                "[AlertType] IN (N'OverdueUnconfirmed', N'UnconfirmedOffline')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(x => x.AlertType)
            .HasConversion<string>()
            .HasColumnType("nvarchar(40)")
            .IsRequired();
        builder.Property(x => x.Message).HasMaxLength(500).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasOne<DoseInstance>().WithMany().HasForeignKey(x => x.DoseInstanceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.DoseInstanceId, x.AlertType }).IsUnique();
    }
}

public sealed class AlertRecipientConfiguration : IEntityTypeConfiguration<AlertRecipient>
{
    public void Configure(EntityTypeBuilder<AlertRecipient> builder)
    {
        builder.ToTable("AlertRecipients", table =>
            table.HasCheckConstraint("CK_AlertRecipients_Channel", "[Channel] IN (N'Email')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(x => x.Channel).HasMaxLength(20).IsRequired();
        builder.HasOne<Alert>().WithMany().HasForeignKey(x => x.AlertId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.AlertId, x.RecipientUserId, x.Channel }).IsUnique();
    }
}

public sealed class EmailDeliveryConfiguration : IEntityTypeConfiguration<EmailDelivery>
{
    public void Configure(EntityTypeBuilder<EmailDelivery> builder)
    {
        builder.ToTable("EmailDeliveries", table =>
            table.HasCheckConstraint(
                "CK_EmailDeliveries_Status",
                "[Status] IN (N'Pending', N'Sent', N'Failed', N'Exhausted')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasColumnType("nvarchar(30)")
            .IsRequired();
        builder.Property(x => x.ProviderMessageId).HasMaxLength(200);
        builder.HasOne<AlertRecipient>().WithMany().HasForeignKey(x => x.AlertRecipientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.AlertRecipientId, x.AttemptNumber }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.NextAttemptAt });
    }
}
