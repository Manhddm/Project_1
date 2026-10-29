using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;

namespace SmartMedBox.Infrastructure.Data.Configurations;

public sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("Devices");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(x => x.DeviceId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.SerialNumber).HasMaxLength(100).IsRequired();
        builder.Property(x => x.DeviceApiKeyHash).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ScheduleVersion).HasDefaultValue(0);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.DeviceId).IsUnique();
        builder.HasIndex(x => x.SerialNumber).IsUnique();
        builder.HasIndex(x => x.OwnerUserId).IsUnique().HasFilter("[OwnerUserId] IS NOT NULL");
    }
}

public sealed class DevicePairingCodeConfiguration : IEntityTypeConfiguration<DevicePairingCode>
{
    public void Configure(EntityTypeBuilder<DevicePairingCode> builder)
    {
        builder.ToTable("DevicePairingCodes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(x => x.CodeHash).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ExpiresAt).IsRequired();
        builder.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UsedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.CodeHash).IsUnique();
    }
}

public sealed class CompartmentConfiguration : IEntityTypeConfiguration<Compartment>
{
    public void Configure(EntityTypeBuilder<Compartment> builder)
    {
        builder.ToTable("Compartments", table =>
            table.HasCheckConstraint("CK_Compartments_SlotNumber", "[SlotNumber] BETWEEN 1 AND 3"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(x => x.Name).HasMaxLength(30).IsRequired();
        builder.Property(x => x.IsEnabled).HasDefaultValue(true);
        builder.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.DeviceId, x.SlotNumber }).IsUnique();
    }
}

public sealed class DeviceEventConfiguration : IEntityTypeConfiguration<DeviceEvent>
{
    public void Configure(EntityTypeBuilder<DeviceEvent> builder)
    {
        builder.ToTable("DeviceEvents", table =>
            table.HasCheckConstraint("CK_DeviceEvents_EventType", "[EventType] IN (N'LidOpened', N'LidClosed')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(x => x.EventId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EventType)
            .HasConversion<string>()
            .HasColumnType("nvarchar(40)")
            .IsRequired();
        builder.Property(x => x.OccurredAt).IsRequired();
        builder.Property(x => x.ReceivedAt).IsRequired();
        builder.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Compartment>().WithMany().HasForeignKey(x => x.CompartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DoseInstance>().WithMany().HasForeignKey(x => x.MatchedDoseInstanceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.DeviceId, x.EventId }).IsUnique();
        builder.HasIndex(x => new { x.DeviceId, x.OccurredAt });
    }
}
