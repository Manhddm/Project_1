using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Auditing;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;
using static SmartMedBox.IntegrationTests.Relatives.RelativesTestSupport;

namespace SmartMedBox.IntegrationTests.Relatives;

public sealed class RelativeInvitationApiTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    [Fact]
    public async Task CreateInvitation_Owner_Returns201StoresHashSendsEmailAndAudits()
    {
        var (owner, deviceId) = await SeedOwnerAsync();
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = await LoginWithCsrfAsync(factory, owner);
        var email = NewEmail("invitee");
        var before = DateTimeOffset.UtcNow;

        using var response = await InviteAsync(client, "  " + email.ToUpperInvariant() + " ");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(email, body.GetProperty("inviteeEmail").GetString());
        Assert.Equal("Pending", body.GetProperty("status").GetString());
        var expiresAt = body.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.InRange(expiresAt, before.AddDays(7).AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(7).AddMinutes(1));

        var message = factory.Emails.SingleTo(email);
        var token = ExtractToken(message.Body);
        Assert.DoesNotContain("đã uống", message.Body, StringComparison.OrdinalIgnoreCase);

        await using var db = fixture.CreateContext();
        var invitation = await db.RelativeInvitations.SingleAsync(x => x.DeviceId == deviceId);
        Assert.NotEqual(token, invitation.TokenHash);
        Assert.DoesNotContain(token, invitation.TokenHash);
        var audit = await db.AuditLogs.SingleAsync(x => x.EntityId == invitation.Id.ToString());
        Assert.Equal(AuditActions.InvitationCreated, audit.Action);
        Assert.Equal(owner.Id, audit.ActorUserId);
        Assert.Equal(deviceId, audit.DeviceId);
        Assert.DoesNotContain(email, audit.Details);
        Assert.DoesNotContain(token, audit.Details);
    }

    [Fact]
    public async Task InvitationEndpoints_RelativeOrStranger_Return403()
    {
        var (owner, deviceId) = await SeedOwnerAsync();
        var relative = await TestUsers.SeedAsync(fixture);
        await SeedRelationshipAsync(fixture, deviceId, relative.Id);
        var stranger = await TestUsers.SeedAsync(fixture);
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var ownerClient = await LoginWithCsrfAsync(factory, owner);
        using var created = await InviteAsync(ownerClient, NewEmail("pending"));
        var invitationId = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        foreach (var outsider in new[] { relative, stranger })
        {
            using var client = await LoginWithCsrfAsync(factory, outsider);
            await AssertCodeAsync(await InviteAsync(client, NewEmail("x")), HttpStatusCode.Forbidden, "FORBIDDEN");
            await AssertCodeAsync(await client.GetAsync($"/api/relatives/invitations?deviceId={deviceId}"), HttpStatusCode.Forbidden, "FORBIDDEN");
            await AssertCodeAsync(await client.GetAsync("/api/relatives/invitations"), HttpStatusCode.Forbidden, "FORBIDDEN");
            await AssertCodeAsync(
                await client.PostAsync($"/api/relatives/invitations/{invitationId}/revoke", null),
                HttpStatusCode.Forbidden,
                "FORBIDDEN");
        }

        await using var db = fixture.CreateContext();
        Assert.Equal(InvitationStatus.Pending, (await db.RelativeInvitations.SingleAsync(x => x.Id == invitationId)).Status);
    }

    [Fact]
    public async Task InvitationEndpoints_Anonymous_Return401()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = TestUsers.CreateClient(factory);

        using var list = await client.GetAsync("/api/relatives/invitations");

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
    }

    [Fact]
    public async Task ListInvitations_Owner_ReturnsDeviceInvitationsWithStatus()
    {
        var (owner, deviceId) = await SeedOwnerAsync();
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = await LoginWithCsrfAsync(factory, owner);
        var email = NewEmail("listed");
        (await InviteAsync(client, email)).Dispose();

        using var withoutDevice = await client.GetAsync("/api/relatives/invitations");
        using var withDevice = await client.GetAsync($"/api/relatives/invitations?deviceId={deviceId}");

        foreach (var response in new[] { withoutDevice, withDevice })
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var item = Assert.Single((await ReadJsonAsync(response)).GetProperty("items").EnumerateArray());
            Assert.Equal(email, item.GetProperty("inviteeEmail").GetString());
            Assert.Equal("Pending", item.GetProperty("status").GetString());
        }
    }

    [Fact]
    public async Task RevokeInvitation_Owner_Returns204AndTokenNoLongerAccepted()
    {
        var (owner, deviceId) = await SeedOwnerAsync();
        var invitee = await TestUsers.SeedAsync(fixture);
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var ownerClient = await LoginWithCsrfAsync(factory, owner);
        using var created = await InviteAsync(ownerClient, invitee.Email);
        var invitationId = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();
        var token = ExtractToken(factory.Emails.SingleTo(invitee.Email).Body);

        using var revoke = await ownerClient.PostAsync($"/api/relatives/invitations/{invitationId}/revoke", null);
        using var again = await ownerClient.PostAsync($"/api/relatives/invitations/{invitationId}/revoke", null);
        using var missing = await ownerClient.PostAsync($"/api/relatives/invitations/{Guid.NewGuid()}/revoke", null);

        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        await AssertCodeAsync(again, HttpStatusCode.Conflict, "INVITE_NOT_PENDING");
        await AssertCodeAsync(missing, HttpStatusCode.NotFound, "INVITE_NOT_FOUND");

        using var inviteeClient = await LoginWithCsrfAsync(factory, invitee);
        await AssertCodeAsync(await AcceptAsync(inviteeClient, token), HttpStatusCode.Conflict, "INVITE_NOT_PENDING");

        await using var db = fixture.CreateContext();
        Assert.Equal(InvitationStatus.Revoked, (await db.RelativeInvitations.SingleAsync(x => x.Id == invitationId)).Status);
        Assert.False(await db.CareRelationships.AnyAsync(x => x.DeviceId == deviceId));
        Assert.Single(await db.AuditLogs.Where(x => x.Action == AuditActions.InvitationRevoked && x.EntityId == invitationId.ToString()).ToListAsync());
    }

    [Fact]
    public async Task CreateInvitation_DuplicatePendingDifferentCase_Returns409InviteAlreadyPending()
    {
        var (owner, _) = await SeedOwnerAsync();
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = await LoginWithCsrfAsync(factory, owner);
        var email = NewEmail("dup");

        using var first = await InviteAsync(client, email);
        using var second = await InviteAsync(client, email.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        await AssertCodeAsync(second, HttpStatusCode.Conflict, "INVITE_ALREADY_PENDING");
        Assert.Single(factory.Emails.Messages, m => m.To == email);
    }

    [Fact]
    public async Task CreateInvitation_ExpiredPendingSameEmail_AllowsNewInvitation()
    {
        var (owner, deviceId) = await SeedOwnerAsync();
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = await LoginWithCsrfAsync(factory, owner);
        var email = NewEmail("reinvite");
        (await InviteAsync(client, email)).Dispose();
        await ExpireInvitationsAsync(deviceId);

        using var second = await InviteAsync(client, email);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        await using var db = fixture.CreateContext();
        var statuses = await db.RelativeInvitations.Where(x => x.DeviceId == deviceId).Select(x => x.Status).ToListAsync();
        Assert.Equal([InvitationStatus.Pending, InvitationStatus.Expired], statuses.Order());
    }

    [Fact]
    public async Task CreateInvitation_AlreadyActiveRelative_Returns409RelationshipExists()
    {
        var (owner, deviceId) = await SeedOwnerAsync();
        var relative = await TestUsers.SeedAsync(fixture);
        await SeedRelationshipAsync(fixture, deviceId, relative.Id);
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = await LoginWithCsrfAsync(factory, owner);

        await AssertCodeAsync(await InviteAsync(client, relative.Email), HttpStatusCode.Conflict, "RELATIONSHIP_EXISTS");
        Assert.Empty(factory.Emails.Messages);
    }

    [Fact]
    public async Task CreateInvitation_OwnEmailOrInvalidEmail_Returns400Validation()
    {
        var (owner, _) = await SeedOwnerAsync();
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = await LoginWithCsrfAsync(factory, owner);

        await AssertCodeAsync(await InviteAsync(client, owner.Email), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await AssertCodeAsync(await InviteAsync(client, "not-an-email"), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task AcceptInvitation_MatchingEmail_CreatesActiveRelationshipOnceAndAudits()
    {
        var (owner, deviceId) = await SeedOwnerAsync();
        var invitee = await TestUsers.SeedAsync(fixture);
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var ownerClient = await LoginWithCsrfAsync(factory, owner);
        (await InviteAsync(ownerClient, invitee.Email)).Dispose();
        var token = ExtractToken(factory.Emails.SingleTo(invitee.Email).Body);
        using var client = await LoginWithCsrfAsync(factory, invitee);

        using var accept = await AcceptAsync(client, token);
        using var reuse = await AcceptAsync(client, token);

        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        var body = await ReadJsonAsync(accept);
        Assert.Equal(deviceId, body.GetProperty("deviceId").GetGuid());
        Assert.Equal("Accepted", body.GetProperty("status").GetString());
        await AssertCodeAsync(reuse, HttpStatusCode.Conflict, "INVITE_NOT_PENDING");

        await using var db = fixture.CreateContext();
        var relationship = await db.CareRelationships.SingleAsync(x => x.DeviceId == deviceId);
        Assert.Equal(body.GetProperty("relationshipId").GetGuid(), relationship.Id);
        Assert.Equal(invitee.Id, relationship.RelativeUserId);
        Assert.Equal(RelationshipStatus.Active, relationship.Status);
        Assert.Equal(InvitationStatus.Accepted, (await db.RelativeInvitations.SingleAsync(x => x.DeviceId == deviceId)).Status);
        var audit = await db.AuditLogs.SingleAsync(x => x.Action == AuditActions.InvitationAccepted && x.DeviceId == deviceId);
        Assert.Equal(invitee.Id, audit.ActorUserId);
        Assert.DoesNotContain(token, audit.Details);
        Assert.DoesNotContain(invitee.Email, audit.Details);
    }

    [Fact]
    public async Task AcceptInvitation_DifferentAccountEmail_Returns403InviteEmailMismatch()
    {
        var (owner, deviceId) = await SeedOwnerAsync();
        var other = await TestUsers.SeedAsync(fixture);
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var ownerClient = await LoginWithCsrfAsync(factory, owner);
        var email = NewEmail("intended");
        (await InviteAsync(ownerClient, email)).Dispose();
        var token = ExtractToken(factory.Emails.SingleTo(email).Body);
        using var client = await LoginWithCsrfAsync(factory, other);

        await AssertCodeAsync(await AcceptAsync(client, token), HttpStatusCode.Forbidden, "INVITE_EMAIL_MISMATCH");

        await using var db = fixture.CreateContext();
        Assert.Equal(InvitationStatus.Pending, (await db.RelativeInvitations.SingleAsync(x => x.DeviceId == deviceId)).Status);
        Assert.False(await db.CareRelationships.AnyAsync(x => x.DeviceId == deviceId));
    }

    [Fact]
    public async Task AcceptInvitation_Expired_Returns410InviteExpired()
    {
        var (owner, deviceId) = await SeedOwnerAsync();
        var invitee = await TestUsers.SeedAsync(fixture);
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var ownerClient = await LoginWithCsrfAsync(factory, owner);
        (await InviteAsync(ownerClient, invitee.Email)).Dispose();
        var token = ExtractToken(factory.Emails.SingleTo(invitee.Email).Body);
        await ExpireInvitationsAsync(deviceId);
        using var client = await LoginWithCsrfAsync(factory, invitee);

        await AssertCodeAsync(await AcceptAsync(client, token), HttpStatusCode.Gone, "INVITE_EXPIRED");

        using var list = await ownerClient.GetAsync("/api/relatives/invitations");
        var item = Assert.Single((await ReadJsonAsync(list)).GetProperty("items").EnumerateArray());
        Assert.Equal("Expired", item.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AcceptInvitation_UnknownOrMalformedToken_ReturnsSameInviteNotFound()
    {
        var user = await TestUsers.SeedAsync(fixture);
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = await LoginWithCsrfAsync(factory, user);

        using var unknown = await AcceptAsync(client, Domain.Accounts.SecureToken.Create());
        using var malformed = await AcceptAsync(client, new string('x', 300));

        await AssertCodeAsync(unknown, HttpStatusCode.NotFound, "INVITE_NOT_FOUND");
        await AssertCodeAsync(malformed, HttpStatusCode.NotFound, "INVITE_NOT_FOUND");
    }

    [Fact]
    public async Task AcceptInvitation_InviteeWithoutAccount_RegistersVerifiesThenAccepts()
    {
        var (owner, deviceId) = await SeedOwnerAsync();
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var ownerClient = await LoginWithCsrfAsync(factory, owner);
        var email = NewEmail("newcomer");
        using var created = await InviteAsync(ownerClient, email);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var inviteToken = ExtractToken(factory.Emails.SingleTo(email).Body);

        await using (var db = fixture.CreateContext())
        {
            Assert.False(await db.ApplicationUsers.AnyAsync(x => x.Email == email));
        }

        using var anonymous = TestUsers.CreateClient(factory);
        using var register = await anonymous.PostAsJsonAsync("/api/auth/register", new { email, password = TestUsers.Password });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var verifyToken = ExtractToken(factory.Emails.LastTo(email).Body);
        using var verify = await anonymous.PostAsJsonAsync("/api/auth/verify-email", new { token = verifyToken });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        using var client = await LoginWithCsrfAsync(factory, new ApplicationUser { Email = email });
        using var accept = await AcceptAsync(client, inviteToken);

        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        await using var check = fixture.CreateContext();
        var userId = await check.ApplicationUsers.Where(x => x.Email == email).Select(x => x.Id).SingleAsync();
        Assert.True(await check.CareRelationships.AnyAsync(
            x => x.DeviceId == deviceId && x.RelativeUserId == userId && x.Status == RelationshipStatus.Active));
    }

    [Fact]
    public async Task AcceptInvitation_ConcurrentDoubleAccept_ExactlyOneSucceeds()
    {
        var (owner, deviceId) = await SeedOwnerAsync();
        var invitee = await TestUsers.SeedAsync(fixture);
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var ownerClient = await LoginWithCsrfAsync(factory, owner);
        (await InviteAsync(ownerClient, invitee.Email)).Dispose();
        var token = ExtractToken(factory.Emails.SingleTo(invitee.Email).Body);
        using var first = await LoginWithCsrfAsync(factory, invitee);
        using var second = await LoginWithCsrfAsync(factory, invitee);

        var responses = await Task.WhenAll(AcceptAsync(first, token), AcceptAsync(second, token));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        await using var db = fixture.CreateContext();
        Assert.Single(await db.CareRelationships.Where(x => x.DeviceId == deviceId).ToListAsync());
        Assert.Single(await db.AuditLogs.Where(x => x.Action == AuditActions.InvitationAccepted && x.DeviceId == deviceId).ToListAsync());
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task CreateInvitation_OverConfiguredLimit_Returns429()
    {
        var (owner, _) = await SeedOwnerAsync();
        using var factory = WithSettings(("RateLimiting:InvitationCreate:PermitLimit", "1"));
        using var client = await LoginWithCsrfAsync(factory, owner);

        using var first = await InviteAsync(client, NewEmail("a"));
        using var limited = await InviteAsync(client, NewEmail("b"));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        await AssertCodeAsync(limited, HttpStatusCode.TooManyRequests, "RATE_LIMITED");
        Assert.True(limited.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task AcceptInvitation_OverConfiguredLimit_Returns429()
    {
        var user = await TestUsers.SeedAsync(fixture);
        using var factory = WithSettings(("RateLimiting:InvitationAccept:PermitLimit", "1"));
        using var client = await LoginWithCsrfAsync(factory, user);

        using var first = await AcceptAsync(client, "unknown-token");
        using var limited = await AcceptAsync(client, "unknown-token");

        await AssertCodeAsync(first, HttpStatusCode.NotFound, "INVITE_NOT_FOUND");
        await AssertCodeAsync(limited, HttpStatusCode.TooManyRequests, "RATE_LIMITED");
        Assert.True(limited.Headers.Contains("Retry-After"));
    }

    private async Task<(ApplicationUser Owner, Guid DeviceId)> SeedOwnerAsync()
    {
        var owner = await TestUsers.SeedAsync(fixture);
        return (owner, await SeedDeviceAsync(fixture, owner.Id));
    }

    private async Task ExpireInvitationsAsync(Guid deviceId)
    {
        await using var db = fixture.CreateContext();
        await db.RelativeInvitations
            .Where(x => x.DeviceId == deviceId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> WithSettings(params (string Key, string Value)[] settings) =>
        new TestApplicationFactory(fixture.ConnectionString).WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });

    private static async Task AssertCodeAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            Assert.Equal(code, await ReadCodeAsync(response));
        }
    }

    private static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";
}
