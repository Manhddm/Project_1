using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;

namespace SmartMedBox.Infrastructure.Data.Configurations;

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("ApplicationUsers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(x => x.Email).HasMaxLength(320).IsRequired();
        builder.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
        builder.Property(x => x.EmailVerified).HasDefaultValue(false);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.IsDeleted).HasDefaultValue(false);
        builder.HasIndex(x => x.Email).IsUnique();
    }
}

public sealed class CareRelationshipConfiguration : IEntityTypeConfiguration<CareRelationship>
{
    public void Configure(EntityTypeBuilder<CareRelationship> builder)
    {
        builder.ToTable("CareRelationships", table =>
            table.HasCheckConstraint("CK_CareRelationships_Status", "[Status] IN (N'Active', N'Revoked')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasColumnType("nvarchar(30)")
            .IsRequired();
        builder.Property(x => x.AcceptedAt).IsRequired();
        builder.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RelativeUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.DeviceId, x.RelativeUserId })
            .IsUnique()
            .HasFilter("[Status] = N'Active'");
    }
}

public sealed class RelativeInvitationConfiguration : IEntityTypeConfiguration<RelativeInvitation>
{
    public void Configure(EntityTypeBuilder<RelativeInvitation> builder)
    {
        builder.ToTable("RelativeInvitations", table =>
            table.HasCheckConstraint(
                "CK_RelativeInvitations_Status",
                "[Status] IN (N'Pending', N'Accepted', N'Revoked', N'Expired')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(x => x.InviteeEmail).HasMaxLength(320).IsRequired();
        builder.Property(x => x.TokenHash).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasColumnType("nvarchar(30)")
            .IsRequired();
        builder.Property(x => x.ExpiresAt).IsRequired();
        builder.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.DeviceId, x.InviteeEmail })
            .IsUnique()
            .HasFilter("[Status] = N'Pending'");
    }
}

public sealed class PrescriptionImageConfiguration : IEntityTypeConfiguration<PrescriptionImage>
{
    public void Configure(EntityTypeBuilder<PrescriptionImage> builder)
    {
        builder.ToTable("PrescriptionImages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(x => x.StorageKey).HasMaxLength(500).IsRequired();
        builder.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.UploadedAt).IsRequired();
        builder.Property(x => x.IsDeleted).HasDefaultValue(false);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(x => x.Action).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.OccurredAt).IsRequired();
        builder.Property(x => x.Details).HasColumnType("nvarchar(max)");
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
    }
}
