using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;

namespace SmartMedBox.Infrastructure.Data.Configurations;

public sealed class MedicationScheduleConfiguration : IEntityTypeConfiguration<MedicationSchedule>
{
    public void Configure(EntityTypeBuilder<MedicationSchedule> builder)
    {
        builder.ToTable("MedicationSchedules", table =>
            table.HasCheckConstraint("CK_MedicationSchedules_Grace", "[GracePeriodMinutes] BETWEEN 1 AND 120"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(x => x.TimeOfDay).HasColumnType("time(0)");
        builder.Property(x => x.StartDate).HasColumnType("date");
        builder.Property(x => x.EndDate).HasColumnType("date");
        builder.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.IsDeleted).HasDefaultValue(false);
        builder.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Compartment>().WithMany().HasForeignKey(x => x.CompartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.DeviceId, x.IsDeleted });
    }
}

public sealed class ScheduleDayConfiguration : IEntityTypeConfiguration<ScheduleDay>
{
    public void Configure(EntityTypeBuilder<ScheduleDay> builder)
    {
        builder.ToTable("ScheduleDays", table =>
            table.HasCheckConstraint("CK_ScheduleDays_DayOfWeek", "[DayOfWeek] BETWEEN 1 AND 7"));
        builder.HasKey(x => new { x.ScheduleId, x.DayOfWeek });
        builder.HasOne<MedicationSchedule>().WithMany().HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ScheduleItemConfiguration : IEntityTypeConfiguration<ScheduleItem>
{
    public void Configure(EntityTypeBuilder<ScheduleItem> builder)
    {
        builder.ToTable("ScheduleItems", table =>
            table.HasCheckConstraint("CK_ScheduleItems_Quantity", "[Quantity] > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(x => x.MedicineName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Quantity).HasPrecision(10, 2);
        builder.Property(x => x.Unit).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Instructions).HasMaxLength(500).IsRequired();
        builder.HasOne<MedicationSchedule>().WithMany().HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DoseInstanceConfiguration : IEntityTypeConfiguration<DoseInstance>
{
    public void Configure(EntityTypeBuilder<DoseInstance> builder)
    {
        builder.ToTable("DoseInstances", table =>
        {
            table.HasCheckConstraint(
                "CK_DoseInstances_Status",
                "[Status] IN (N'Pending', N'Confirmed', N'Overdue', N'Unknown')");
            table.HasCheckConstraint("CK_DoseInstances_Grace", "[GracePeriodMinutes] BETWEEN 1 AND 120");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(x => x.ScheduledAt).IsRequired();
        builder.Property(x => x.DeadlineAt).IsRequired();
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasColumnType("nvarchar(30)")
            .IsRequired();
        builder.HasOne<MedicationSchedule>().WithMany().HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Compartment>().WithMany().HasForeignKey(x => x.CompartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ScheduleId, x.ScheduledAt }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.DeadlineAt });
    }
}

public sealed class DoseItemSnapshotConfiguration : IEntityTypeConfiguration<DoseItemSnapshot>
{
    public void Configure(EntityTypeBuilder<DoseItemSnapshot> builder)
    {
        builder.ToTable("DoseItemSnapshots", table =>
            table.HasCheckConstraint("CK_DoseItemSnapshots_Quantity", "[Quantity] > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(x => x.MedicineName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Quantity).HasPrecision(10, 2);
        builder.Property(x => x.Unit).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Instructions).HasMaxLength(500).IsRequired();
        builder.HasOne<DoseInstance>().WithMany().HasForeignKey(x => x.DoseInstanceId).OnDelete(DeleteBehavior.Restrict);
    }
}
