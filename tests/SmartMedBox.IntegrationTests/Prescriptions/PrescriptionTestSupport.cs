using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Devices;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Prescriptions;

internal sealed record PrescriptionSetup(Guid DeviceId, ApplicationUser Owner, ApplicationUser Relative, ApplicationUser Stranger);

internal static class PrescriptionTestSupport
{
    /// <summary>Chủ hộp có một hộp đã ghép, một người thân Active và một người ngoài.</summary>
    public static async Task<PrescriptionSetup> SeedAsync(SqlServerDatabaseFixture fixture)
    {
        var owner = await TestUsers.SeedAsync(fixture);
        var relative = await TestUsers.SeedAsync(fixture);
        var stranger = await TestUsers.SeedAsync(fixture);

        await using var db = fixture.CreateContext();
        var provisioned = await new DeviceProvisioningService(db).ProvisionAsync(
            $"esp32-{Guid.NewGuid():N}",
            $"SN-{Guid.NewGuid():N}",
            Guid.NewGuid().ToString("N"));
        provisioned.Device.OwnerUserId = owner.Id;
        db.CareRelationships.Add(new CareRelationship
        {
            Id = Guid.NewGuid(),
            DeviceId = provisioned.Device.Id,
            RelativeUserId = relative.Id,
            Status = RelationshipStatus.Active,
            AcceptedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return new PrescriptionSetup(provisioned.Device.Id, owner, relative, stranger);
    }
}
