using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Enums;

namespace SmartMedBox.IntegrationTests.Alerts;

public sealed class AlertEvaluationServiceTests
{
    [Fact]
    public async Task EvaluateAsync_PendingPastDeadlineDeviceOnline_MarksOverdueAndAlertsOwnerAndActiveRelatives()
    {
        await using var env = await AlertTestDatabase.CreateAsync();
        var deadline = env.Clock.UtcNow.AddMinutes(-5);
        var box = await env.SeedBoxAsync(lastSeenAt: deadline.AddMinutes(-1));
        var doseId = await env.SeedDoseAsync(box, deadline);

        await using (var db = env.CreateContext())
        {
            var result = await env.CreateEvaluator(db).EvaluateAsync();
            Assert.Equal(1, result.CreatedCount);
        }

        await using var verify = env.CreateContext();
        Assert.Equal(DoseStatus.Overdue, (await verify.DoseInstances.SingleAsync(x => x.Id == doseId)).Status);
        var alert = await verify.Alerts.SingleAsync(x => x.DoseInstanceId == doseId);
        Assert.Equal(AlertType.OverdueUnconfirmed, alert.AlertType);
        var recipients = await verify.AlertRecipients.Where(x => x.AlertId == alert.Id).ToListAsync();
        Assert.Equal(box.ExpectedRecipientIds.Order(), recipients.Select(x => x.RecipientUserId).Order());
        Assert.DoesNotContain(recipients, x => box.RevokedRelatives.Any(r => r.Id == x.RecipientUserId));
        Assert.All(recipients, x => Assert.Equal("Email", x.Channel));

        var recipientIds = recipients.Select(x => x.Id).ToList();
        var deliveries = await verify.EmailDeliveries.Where(x => recipientIds.Contains(x.AlertRecipientId)).ToListAsync();
        Assert.Equal(recipientIds.Order(), deliveries.Select(x => x.AlertRecipientId).Order());
        Assert.All(deliveries, x => Assert.Equal((1, EmailDeliveryStatus.Pending), (x.AttemptNumber, x.Status)));
    }

    [Fact]
    public async Task EvaluateAsync_LastSeenAtNull_MarksUnknownWithOfflineAlert()
    {
        await using var env = await AlertTestDatabase.CreateAsync();
        var box = await env.SeedBoxAsync(lastSeenAt: null);
        var doseId = await env.SeedDoseAsync(box, env.Clock.UtcNow.AddMinutes(-5));

        await using (var db = env.CreateContext()) await env.CreateEvaluator(db).EvaluateAsync();

        await AssertOfflineOutcomeAsync(env, doseId);
    }

    [Fact]
    public async Task EvaluateAsync_LastSeenTooLongBeforeDeadline_MarksUnknownWithOfflineAlert()
    {
        await using var env = await AlertTestDatabase.CreateAsync();
        var deadline = env.Clock.UtcNow.AddMinutes(-5);
        var box = await env.SeedBoxAsync(lastSeenAt: deadline - AlertTestDatabase.OnlineThreshold - TimeSpan.FromSeconds(1));
        var doseId = await env.SeedDoseAsync(box, deadline);

        await using (var db = env.CreateContext()) await env.CreateEvaluator(db).EvaluateAsync();

        await AssertOfflineOutcomeAsync(env, doseId);
    }

    [Fact]
    public async Task EvaluateAsync_JobRunsHoursAfterDeadlineWhileDeviceNowSilent_UsesConnectivityAtDeadline()
    {
        await using var env = await AlertTestDatabase.CreateAsync();
        var deadline = env.Clock.UtcNow.AddHours(-6);
        // Online at the deadline, silent for hours at job run time: the run time must not matter.
        var box = await env.SeedBoxAsync(lastSeenAt: deadline.AddMinutes(-2));
        var doseId = await env.SeedDoseAsync(box, deadline);

        await using (var db = env.CreateContext()) await env.CreateEvaluator(db).EvaluateAsync();

        await using var verify = env.CreateContext();
        Assert.Equal(DoseStatus.Overdue, (await verify.DoseInstances.SingleAsync(x => x.Id == doseId)).Status);
        Assert.Equal(AlertType.OverdueUnconfirmed, (await verify.Alerts.SingleAsync(x => x.DoseInstanceId == doseId)).AlertType);
    }

    [Fact]
    public async Task EvaluateAsync_OfflineAtDeadlineReconnectedBeforeJobRuns_MarksUnknownWithOfflineAlert()
    {
        await using var env = await AlertTestDatabase.CreateAsync();
        var deadline = env.Clock.UtcNow.AddMinutes(-30);
        // Offline from deadline-20min until it reconnected 2 minutes ago: LastSeenAt is fresh, but no window covers the deadline.
        var box = await env.SeedBoxAsync(lastSeenAt: deadline.AddMinutes(-20));
        await env.SeedWindowAsync(box, env.Clock.UtcNow.AddMinutes(-2), env.Clock.UtcNow);
        await using (var seed = env.CreateContext())
        {
            await seed.Devices.Where(x => x.Id == box.DeviceId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, env.Clock.UtcNow));
        }

        var doseId = await env.SeedDoseAsync(box, deadline);

        await using (var db = env.CreateContext()) await env.CreateEvaluator(db).EvaluateAsync();

        await AssertOfflineOutcomeAsync(env, doseId);
    }

    [Fact]
    public async Task EvaluateAsync_WindowOpenedAfterDeadline_MarksUnknown()
    {
        await using var env = await AlertTestDatabase.CreateAsync();
        var deadline = env.Clock.UtcNow.AddMinutes(-30);
        var box = await env.SeedBoxAsync(lastSeenAt: null);
        // Ends within the threshold of the deadline but only started after it.
        await env.SeedWindowAsync(box, deadline.AddSeconds(1), env.Clock.UtcNow);
        var doseId = await env.SeedDoseAsync(box, deadline);

        await using (var db = env.CreateContext()) await env.CreateEvaluator(db).EvaluateAsync();

        await AssertOfflineOutcomeAsync(env, doseId);
    }

    [Fact]
    public async Task EvaluateAsync_ConfirmedOrNotYetDueDoses_CreatesNoAlert()
    {
        await using var env = await AlertTestDatabase.CreateAsync();
        var box = await env.SeedBoxAsync(lastSeenAt: env.Clock.UtcNow);
        var confirmedId = await env.SeedDoseAsync(box, env.Clock.UtcNow.AddMinutes(-5), DoseStatus.Confirmed);
        var atDeadlineId = await env.SeedDoseAsync(box, env.Clock.UtcNow);
        var futureId = await env.SeedDoseAsync(box, env.Clock.UtcNow.AddMinutes(5));

        await using (var db = env.CreateContext())
        {
            var result = await env.CreateEvaluator(db).EvaluateAsync();
            Assert.Equal(0, result.CreatedCount);
        }

        await using var verify = env.CreateContext();
        Assert.Empty(await verify.Alerts.ToListAsync());
        Assert.Equal(DoseStatus.Confirmed, (await verify.DoseInstances.SingleAsync(x => x.Id == confirmedId)).Status);
        // The confirmation window [ScheduledAt, DeadlineAt] is inclusive, so a dose exactly at its deadline is still open.
        Assert.Equal(DoseStatus.Pending, (await verify.DoseInstances.SingleAsync(x => x.Id == atDeadlineId)).Status);
        Assert.Equal(DoseStatus.Pending, (await verify.DoseInstances.SingleAsync(x => x.Id == futureId)).Status);
    }

    [Fact]
    public async Task EvaluateAsync_RunTwice_CreatesOneAlertWithoutDuplicateRecipients()
    {
        await using var env = await AlertTestDatabase.CreateAsync();
        var box = await env.SeedBoxAsync(lastSeenAt: env.Clock.UtcNow);
        var doseId = await env.SeedDoseAsync(box, env.Clock.UtcNow.AddMinutes(-1));

        await using (var db = env.CreateContext()) await env.CreateEvaluator(db).EvaluateAsync();
        env.Clock.UtcNow = env.Clock.UtcNow.AddMinutes(5);
        await using (var db = env.CreateContext())
        {
            var second = await env.CreateEvaluator(db).EvaluateAsync();
            Assert.Equal((0, 0), (second.EvaluatedCount, second.CreatedCount));
        }

        await AssertSingleAlertPerDoseAsync(env, box, [doseId]);
    }

    [Fact]
    public async Task EvaluateAsync_TwoConcurrentInstances_CreateOneAlertPerDose()
    {
        await using var env = await AlertTestDatabase.CreateAsync();
        var box = await env.SeedBoxAsync(lastSeenAt: env.Clock.UtcNow);
        var doseIds = new List<Guid>();
        for (var i = 0; i < 10; i++) doseIds.Add(await env.SeedDoseAsync(box, env.Clock.UtcNow.AddMinutes(-1 - i)));

        await using var first = env.CreateContext();
        await using var second = env.CreateContext();
        var results = await Task.WhenAll(
            Task.Run(() => env.CreateEvaluator(first).EvaluateAsync()),
            Task.Run(() => env.CreateEvaluator(second).EvaluateAsync()));

        Assert.Equal(doseIds.Count, results.Sum(x => x.CreatedCount));
        await AssertSingleAlertPerDoseAsync(env, box, doseIds);
    }

    [Fact]
    public async Task EvaluateAsync_AlertInsertFails_RollsBackStatusRecipientsAndDeliveries()
    {
        await using var env = await AlertTestDatabase.CreateAsync();
        var box = await env.SeedBoxAsync(lastSeenAt: env.Clock.UtcNow);
        var doseId = await env.SeedDoseAsync(box, env.Clock.UtcNow.AddMinutes(-1));
        await using (var seed = env.CreateContext())
        {
            // A conflicting row makes the alert insert violate UNIQUE (DoseInstanceId, AlertType).
            seed.Alerts.Add(new Domain.Entities.Alert { Id = Guid.NewGuid(), DoseInstanceId = doseId, AlertType = AlertType.OverdueUnconfirmed, Message = "x", CreatedAt = env.Clock.UtcNow });
            await seed.SaveChangesAsync();
        }

        await using (var db = env.CreateContext())
        {
            var result = await env.CreateEvaluator(db).EvaluateAsync();
            Assert.Equal(0, result.CreatedCount);
        }

        await using var verify = env.CreateContext();
        Assert.Equal(DoseStatus.Pending, (await verify.DoseInstances.SingleAsync(x => x.Id == doseId)).Status);
        Assert.Equal(1, await verify.Alerts.CountAsync());
        Assert.Equal(0, await verify.AlertRecipients.CountAsync());
        Assert.Equal(0, await verify.EmailDeliveries.CountAsync());
    }

    [Fact]
    public async Task EvaluateAsync_AlertMessages_UseOpenedConfirmedWordingOnly()
    {
        await using var env = await AlertTestDatabase.CreateAsync();
        var onlineBox = await env.SeedBoxAsync(lastSeenAt: env.Clock.UtcNow, activeRelatives: 0, revokedRelatives: 0);
        var offlineBox = await env.SeedBoxAsync(lastSeenAt: null, activeRelatives: 0, revokedRelatives: 0);
        await env.SeedDoseAsync(onlineBox, env.Clock.UtcNow.AddMinutes(-1));
        await env.SeedDoseAsync(offlineBox, env.Clock.UtcNow.AddMinutes(-1));

        await using (var db = env.CreateContext()) await env.CreateEvaluator(db).EvaluateAsync();
        var sender = new FakeEmailSender();
        await using (var db = env.CreateContext())
        {
            var worker = env.CreateWorker(db, sender);
            while (await worker.ProcessNextAsync() != Infrastructure.Email.EmailDeliveryResult.NoDelivery) { }
        }

        await using var verify = env.CreateContext();
        var texts = (await verify.Alerts.Select(x => x.Message).ToListAsync())
            .Concat(sender.Sent.SelectMany(x => new[] { x.Subject, x.Body })).ToList();
        Assert.Equal(2, sender.Sent.Count);
        Assert.All(texts, text =>
        {
            Assert.DoesNotContain("đã uống", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("quên uống", text, StringComparison.OrdinalIgnoreCase);
        });
        var offlineEmail = Assert.Single(sender.Sent, x => x.To == offlineBox.Owner.Email);
        Assert.Contains("không thể xác nhận do mất kết nối", offlineEmail.Body, StringComparison.OrdinalIgnoreCase);
        var onlineEmail = Assert.Single(sender.Sent, x => x.To == onlineBox.Owner.Email);
        Assert.DoesNotContain("mất kết nối", onlineEmail.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("chưa được xác nhận", onlineEmail.Body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertOfflineOutcomeAsync(AlertTestDatabase env, Guid doseId)
    {
        await using var verify = env.CreateContext();
        Assert.Equal(DoseStatus.Unknown, (await verify.DoseInstances.SingleAsync(x => x.Id == doseId)).Status);
        var alert = await verify.Alerts.SingleAsync(x => x.DoseInstanceId == doseId);
        Assert.Equal(AlertType.UnconfirmedOffline, alert.AlertType);
        Assert.Contains("không thể xác nhận do mất kết nối", alert.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertSingleAlertPerDoseAsync(AlertTestDatabase env, Box box, IReadOnlyCollection<Guid> doseIds)
    {
        await using var verify = env.CreateContext();
        var alerts = await verify.Alerts.ToListAsync();
        Assert.Equal(doseIds.Order(), alerts.Select(x => x.DoseInstanceId).Order());
        foreach (var alert in alerts)
        {
            var recipients = await verify.AlertRecipients.Where(x => x.AlertId == alert.Id).Select(x => x.RecipientUserId).ToListAsync();
            Assert.Equal(box.ExpectedRecipientIds.Order(), recipients.Order());
        }

        var expectedDeliveries = doseIds.Count * box.ExpectedRecipientIds.Count();
        Assert.Equal(expectedDeliveries, await verify.EmailDeliveries.CountAsync());
    }
}
