using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Devices;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Prescriptions;

internal sealed record PrescriptionSetup(Guid DeviceId, ApplicationUser Owner, ApplicationUser Relative, ApplicationUser Stranger);

internal static class PrescriptionTestSupport
{
    /// <summary>JPEG tối giản hợp lệ với <c>ImageSanitizer</c>: SOI + JFIF APP0 + SOS + EOI.</summary>
    public static byte[] Jpeg() =>
    [
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x0E, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01,
        0x00, 0x00, 0x01, 0x00, 0x01,
        0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00, 0x12, 0x34,
        0xFF, 0xD9,
    ];

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
