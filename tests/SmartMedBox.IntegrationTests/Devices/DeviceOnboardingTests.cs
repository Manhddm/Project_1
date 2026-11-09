using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartMedBox.Domain.Devices;
using SmartMedBox.Infrastructure.Auditing;
using SmartMedBox.Infrastructure.Data;
using SmartMedBox.Infrastructure.Devices;
using SmartMedBox.Infrastructure.Security;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;
using SmartMedBox.UnitTests.TestDoubles;

namespace SmartMedBox.IntegrationTests.Devices;

public sealed class DeviceOnboardingTests(SqlServerDatabaseFixture fixture)
    : IClassFixture<SqlServerDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    private static readonly HmacTokenHasher Hasher =
        new(Options.Create(new TokenPepperOptions { TokenPepper = "onboarding-test-pepper-0123456789abcdef" }));

    private readonly FakeClock _clock = new(Now);

    private DeviceOnboardingService CreateService(AppDbContext db) => new(db, Hasher, _clock);

    [Fact]
    public async Task ProvisionAsync_NewSerial_StoresOnlyHashesAndThreeCompartments()
    {
        await using var db = fixture.CreateContext();
        var result = await CreateService(db).ProvisionAsync($"serial-{Guid.NewGuid():N}");

        Assert.True(DeviceApiKey.IsWellFormedKey(result.DeviceApiKey));
        Assert.True(DeviceApiKey.IsWellFormedDeviceId(result.Provisioned.Device.DeviceId));

        await using var verify = fixture.CreateContext();
        var device = await verify.Devices.AsNoTracking().SingleAsync(x => x.Id == result.Provisioned.Device.Id);
        Assert.Null(device.OwnerUserId);
        Assert.Equal(Hasher.Hash(result.DeviceApiKey), device.DeviceApiKeyHash);
        Assert.DoesNotContain(result.DeviceApiKey, device.DeviceApiKeyHash);
        Assert.Equal(3, await verify.Compartments.CountAsync(x => x.DeviceId == device.Id));

        var code = await verify.DevicePairingCodes.AsNoTracking().SingleAsync(x => x.DeviceId == device.Id);
        Assert.Equal(Hasher.Hash(result.PairingCode.Code), code.CodeHash);
        Assert.Equal(Now + TimeSpan.FromMinutes(15), code.ExpiresAt);
        Assert.Equal(code.ExpiresAt, result.PairingCode.ExpiresAt);
        Assert.Null(code.UsedAt);
        Assert.Null(code.UsedByUserId);

        var audit = await verify.AuditLogs.AsNoTracking().SingleAsync(x => x.DeviceId == device.Id);
        Assert.Equal(AuditActions.DeviceProvisioned, audit.Action);
        Assert.Null(audit.ActorUserId);
        Assert.DoesNotContain(result.DeviceApiKey, audit.Details);
        Assert.DoesNotContain(result.PairingCode.Code, audit.Details);
    }

    [Fact]
    public async Task ProvisionAsync_DuplicateSerial_ThrowsAndLeavesNoPartialDevice()
    {
        var serial = $"serial-{Guid.NewGuid():N}";
        await using (var db = fixture.CreateContext())
        {
            await CreateService(db).ProvisionAsync(serial);
        }

        await using (var db = fixture.CreateContext())
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => CreateService(db).ProvisionAsync(serial));
        }

        await using var verify = fixture.CreateContext();
        Assert.Equal(1, await verify.Devices.CountAsync(x => x.SerialNumber == serial));
    }

    [Fact]
    public async Task IssuePairingCodeAsync_UnpairedDevice_ExpiresPreviousCodeAndAddsNewHash()
    {
        ProvisioningResult provisioned;
        await using (var db = fixture.CreateContext())
        {
            provisioned = await CreateService(db).ProvisionAsync($"serial-{Guid.NewGuid():N}");
        }

        _clock.UtcNow = Now.AddMinutes(5);
        IssuedPairingCode? issued;
        await using (var db = fixture.CreateContext())
        {
            issued = await CreateService(db).IssuePairingCodeAsync(provisioned.Provisioned.Device.DeviceId);
        }

        Assert.NotNull(issued);
        Assert.NotEqual(provisioned.PairingCode.Code, issued.Code);
        Assert.Equal(_clock.UtcNow + PairingCode.Lifetime, issued.ExpiresAt);

        await using var verify = fixture.CreateContext();
        var codes = await verify.DevicePairingCodes.AsNoTracking()
            .Where(x => x.DeviceId == provisioned.Provisioned.Device.Id)
            .ToListAsync();
        Assert.Equal(2, codes.Count);
        Assert.Equal(_clock.UtcNow, codes.Single(x => x.CodeHash == Hasher.Hash(provisioned.PairingCode.Code)).ExpiresAt);
        Assert.Equal(issued.ExpiresAt, codes.Single(x => x.CodeHash == Hasher.Hash(issued.Code)).ExpiresAt);
        Assert.Contains(
            await verify.AuditLogs.AsNoTracking().Where(x => x.DeviceId == provisioned.Provisioned.Device.Id).Select(x => x.Action).ToListAsync(),
            action => action == AuditActions.PairingCodeIssued);
    }

    [Fact]
    public async Task IssuePairingCodeAsync_PairedOrUnknownDevice_ReturnsNull()
    {
        var owner = await TestUsers.SeedAsync(fixture);
        ProvisioningResult provisioned;
        await using (var db = fixture.CreateContext())
        {
            provisioned = await CreateService(db).ProvisionAsync($"serial-{Guid.NewGuid():N}");
            var device = await db.Devices.SingleAsync(x => x.Id == provisioned.Provisioned.Device.Id);
            device.OwnerUserId = owner.Id;
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateContext())
        {
            Assert.Null(await CreateService(db).IssuePairingCodeAsync(provisioned.Provisioned.Device.DeviceId));
            Assert.Null(await CreateService(db).IssuePairingCodeAsync("SMB-UNKNOWN0"));
        }

        await using var verify = fixture.CreateContext();
        Assert.Equal(1, await verify.DevicePairingCodes.CountAsync(x => x.DeviceId == provisioned.Provisioned.Device.Id));
    }
}
