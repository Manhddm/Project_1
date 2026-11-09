using System.Net;
using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Auditing;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;
using static SmartMedBox.IntegrationTests.Relatives.RelativesTestSupport;

namespace SmartMedBox.IntegrationTests.Relatives;

public sealed class CareRelationshipApiTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    [Fact]
    public async Task ListRelationships_Owner_SeesAllRelativesWithEmails()
    {
        var setup = await SeedAsync();
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var owner = await LoginWithCsrfAsync(factory, setup.Owner);

        using var response = await owner.GetAsync($"/api/relatives/relationships?deviceId={setup.DeviceId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await ReadJsonAsync(response)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);
        Assert.Contains(items, x => x.GetProperty("relativeEmail").GetString() == setup.RelativeA.Email && x.GetProperty("status").GetString() == "Active");
        Assert.Contains(items, x => x.GetProperty("relativeEmail").GetString() == setup.RelativeB.Email);
        Assert.Contains(items, x => x.GetProperty("relativeEmail").GetString() == setup.Revoked.Email && x.GetProperty("status").GetString() == "Revoked");
        Assert.All(items, x => Assert.Equal(setup.DeviceId, x.GetProperty("deviceId").GetGuid()));
    }

    [Fact]
    public async Task ListRelationships_Relative_SeesOnlyOwnRelationshipNotOtherRelativesEmails()
    {
        var setup = await SeedAsync();
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var relative = await LoginWithCsrfAsync(factory, setup.RelativeA);

        foreach (var url in new[] { "/api/relatives/relationships", $"/api/relatives/relationships?deviceId={setup.DeviceId}" })
        {
            using var response = await relative.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var raw = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(setup.RelativeB.Email, raw);
            Assert.DoesNotContain(setup.Revoked.Email, raw);
            Assert.DoesNotContain(setup.Owner.Email, raw);
            var item = Assert.Single((await ReadJsonAsync(response)).GetProperty("items").EnumerateArray());
            Assert.Equal(setup.RelativeA.Id, item.GetProperty("relativeUserId").GetGuid());
        }
    }

    [Fact]
    public async Task ListRelationships_StrangerOrRevokedRelative_ForbiddenForDeviceAndEmptyOtherwise()
    {
        var setup = await SeedAsync();
        using var factory = new TestApplicationFactory(fixture.ConnectionString);

        foreach (var outsider in new[] { setup.Stranger, setup.Revoked })
        {
            using var client = await LoginWithCsrfAsync(factory, outsider);
            using var forDevice = await client.GetAsync($"/api/relatives/relationships?deviceId={setup.DeviceId}");
            using var all = await client.GetAsync("/api/relatives/relationships");

            Assert.Equal(HttpStatusCode.Forbidden, forDevice.StatusCode);
            Assert.Equal("FORBIDDEN", await ReadCodeAsync(forDevice));
            Assert.Equal(HttpStatusCode.OK, all.StatusCode);
            Assert.Empty((await ReadJsonAsync(all)).GetProperty("items").EnumerateArray());
        }
    }

    [Fact]
    public async Task DeleteRelationship_Owner_RevokesKeepsHistoryAndAccessEndsImmediately()
    {
        var setup = await SeedAsync();
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var owner = await LoginWithCsrfAsync(factory, setup.Owner);
        using var relative = await LoginWithCsrfAsync(factory, setup.RelativeA);

        using var delete = await owner.DeleteAsync($"/api/relatives/relationships/{setup.RelativeAId}");
        using var again = await owner.DeleteAsync($"/api/relatives/relationships/{setup.RelativeAId}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal("RELATIONSHIP_NOT_FOUND", await ReadCodeAsync(again));

        using var afterRevoke = await relative.GetAsync($"/api/relatives/relationships?deviceId={setup.DeviceId}");
        Assert.Equal(HttpStatusCode.Forbidden, afterRevoke.StatusCode);

        await using var db = fixture.CreateContext();
        var row = await db.CareRelationships.SingleAsync(x => x.Id == setup.RelativeAId);
        Assert.Equal(RelationshipStatus.Revoked, row.Status);
        Assert.NotNull(row.RevokedAt);
        var audit = await db.AuditLogs.SingleAsync(x => x.Action == AuditActions.RelationshipRevoked && x.EntityId == setup.RelativeAId.ToString());
        Assert.Equal(setup.Owner.Id, audit.ActorUserId);
        Assert.Equal("by=owner", audit.Details);
        Assert.Equal(
            RelationshipStatus.Active,
            (await db.CareRelationships.SingleAsync(x => x.Id == setup.RelativeBId)).Status);
    }

    [Fact]
    public async Task DeleteRelationship_RelativeOwn_StopsFollowing()
    {
        var setup = await SeedAsync();
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var relative = await LoginWithCsrfAsync(factory, setup.RelativeA);

        using var delete = await relative.DeleteAsync($"/api/relatives/relationships/{setup.RelativeAId}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        await using var db = fixture.CreateContext();
        Assert.Equal(RelationshipStatus.Revoked, (await db.CareRelationships.SingleAsync(x => x.Id == setup.RelativeAId)).Status);
        var audit = await db.AuditLogs.SingleAsync(x => x.Action == AuditActions.RelationshipRevoked && x.EntityId == setup.RelativeAId.ToString());
        Assert.Equal(setup.RelativeA.Id, audit.ActorUserId);
        Assert.Equal("by=relative", audit.Details);
    }

    [Fact]
    public async Task DeleteRelationship_OtherRelativeOrStranger_Returns403AndMissingReturns404()
    {
        var setup = await SeedAsync();
        using var factory = new TestApplicationFactory(fixture.ConnectionString);

        foreach (var outsider in new[] { setup.RelativeA, setup.Stranger })
        {
            using var client = await LoginWithCsrfAsync(factory, outsider);
            using var response = await client.DeleteAsync($"/api/relatives/relationships/{setup.RelativeBId}");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("FORBIDDEN", await ReadCodeAsync(response));
        }

        using var owner = await LoginWithCsrfAsync(factory, setup.Owner);
        using var missing = await owner.DeleteAsync($"/api/relatives/relationships/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("RELATIONSHIP_NOT_FOUND", await ReadCodeAsync(missing));

        await using var db = fixture.CreateContext();
        Assert.Equal(RelationshipStatus.Active, (await db.CareRelationships.SingleAsync(x => x.Id == setup.RelativeBId)).Status);
    }

    private async Task<Setup> SeedAsync()
    {
        var owner = await TestUsers.SeedAsync(fixture);
        var relativeA = await TestUsers.SeedAsync(fixture);
        var relativeB = await TestUsers.SeedAsync(fixture);
        var revoked = await TestUsers.SeedAsync(fixture);
        var stranger = await TestUsers.SeedAsync(fixture);
        var deviceId = await SeedDeviceAsync(fixture, owner.Id);
        var relativeAId = await SeedRelationshipAsync(fixture, deviceId, relativeA.Id);
        var relativeBId = await SeedRelationshipAsync(fixture, deviceId, relativeB.Id);
        await SeedRelationshipAsync(fixture, deviceId, revoked.Id, RelationshipStatus.Revoked);
        return new Setup(deviceId, owner, relativeA, relativeAId, relativeB, relativeBId, revoked, stranger);
    }

    private sealed record Setup(
        Guid DeviceId,
        ApplicationUser Owner,
        ApplicationUser RelativeA,
        Guid RelativeAId,
        ApplicationUser RelativeB,
        Guid RelativeBId,
        ApplicationUser Revoked,
        ApplicationUser Stranger);
}
