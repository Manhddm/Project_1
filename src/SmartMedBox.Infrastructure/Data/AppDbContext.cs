using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Entities;

namespace SmartMedBox.Infrastructure.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ApplicationUser> ApplicationUsers => Set<ApplicationUser>();
    public DbSet<CareRelationship> CareRelationships => Set<CareRelationship>();
    public DbSet<RelativeInvitation> RelativeInvitations => Set<RelativeInvitation>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DevicePairingCode> DevicePairingCodes => Set<DevicePairingCode>();
    public DbSet<Compartment> Compartments => Set<Compartment>();
    public DbSet<DeviceEvent> DeviceEvents => Set<DeviceEvent>();
    public DbSet<MedicationSchedule> MedicationSchedules => Set<MedicationSchedule>();
    public DbSet<ScheduleDay> ScheduleDays => Set<ScheduleDay>();
    public DbSet<ScheduleItem> ScheduleItems => Set<ScheduleItem>();
    public DbSet<DoseInstance> DoseInstances => Set<DoseInstance>();
    public DbSet<DoseItemSnapshot> DoseItemSnapshots => Set<DoseItemSnapshot>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<AlertRecipient> AlertRecipients => Set<AlertRecipient>();
    public DbSet<EmailDelivery> EmailDeliveries => Set<EmailDelivery>();
    public DbSet<PrescriptionImage> PrescriptionImages => Set<PrescriptionImage>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
