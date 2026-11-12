using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Data;
using SmartMedBox.Infrastructure.Email;

namespace SmartMedBox.IntegrationTests.Alerts;

public sealed class EmailDeliveryWorkerTests
{
    [Fact]
    public async Task ProcessNextAsync_AllRecipientsSucceed_SendsOncePerRecipientAndMarksSent()
    {
        await using var env = await AlertTestDatabase.CreateAsync();
        var box = await SeedAlertAsync(env);
        var sender = new FakeEmailSender();

        await using (var db = env.CreateContext()) await DrainAsync(env.CreateWorker(db, sender));

        Assert.Equal(box.ExpectedRecipientIds.Count(), sender.Sent.Count);
        Assert.All(RecipientEmails(box), email => Assert.Equal(1, sender.SentCount(email)));
        Assert.All(box.RevokedRelatives, x => Assert.Equal(0, sender.SentCount(x.Email)));
        await using var verify = env.CreateContext();
        Assert.All(await verify.EmailDeliveries.ToListAsync(), x =>
        {
            Assert.Equal(EmailDeliveryStatus.Sent, x.Status);
            Assert.Equal(env.Clock.UtcNow, x.SentAt);
            Assert.Null(x.LockedUntil);
        });
    }

    [Fact]
    public async Task ProcessNextAsync_Sent_StoresMessageIdAsProviderMessageIdWithVietnameseSubject()
    {
        await using var env = await AlertTestDatabase.CreateAsync();
        await SeedAlertAsync(env);
        var sender = new FakeEmailSender();

        await using (var db = env.CreateContext()) await DrainAsync(env.CreateWorker(db, sender));

        await using var verify = env.CreateContext();
        var stored = await verify.EmailDeliveries.Select(x => x.ProviderMessageId).ToListAsync();
        Assert.Equal(sender.MessageIds.Order(), stored.Order()!);
        Assert.All(stored, x => Assert.Matches("^<[0-9a-f]{32}@test.local>$", x));
        Assert.All(sender.Sent, x => Assert.Equal("Cảnh báo lần uống từ Hộp thuốc thông minh", x.Subject));
    }

    [Fact]
    public async Task ProcessNextAsync_TwoConcurrentWorkers_NeverSendSameDeliveryTwice()
    {
        await using var env = await AlertTestDatabase.CreateAsync();
        var box = await SeedAlertAsync(env, activeRelatives: 6);
        var sender = new FakeEmailSender { SendDelay = TimeSpan.FromMilliseconds(50) };

        await using var first = env.CreateContext();
        await using var second = env.CreateContext();
        await Task.WhenAll(
            Task.Run(() => DrainAsync(env.CreateWorker(first, sender))),
            Task.Run(() => DrainAsync(env.CreateWorker(second, sender))));

        Assert.Equal(box.ExpectedRecipientIds.Count(), sender.Sent.Count);
        Assert.All(RecipientEmails(box), email => Assert.Equal(1, sender.SentCount(email)));
        await using var verify = env.CreateContext();
        Assert.All(await verify.EmailDeliveries.ToListAsync(), x => Assert.Equal(EmailDeliveryStatus.Sent, x.Status));
    }

    [Fact]
    public async Task ProcessNextAsync_DeliveryUnderActiveLease_IsNotClaimedByAnotherWorker()
    {
        await using var env = await AlertTestDatabase.CreateAsync();
        await SeedAlertAsync(env, activeRelatives: 0);
        await using (var db = env.CreateContext())
        {
            await db.EmailDeliveries.ExecuteUpdateAsync(s => s.SetProperty(x => x.LockedUntil, env.Clock.UtcNow.AddMinutes(1)));
        }

        var sender = new FakeEmailSender();
        await using (var db = env.CreateContext())
        {
            Assert.Equal(EmailDeliveryResult.NoDelivery, await env.CreateWorker(db, sender).ProcessNextAsync());
        }

        Assert.Empty(sender.Sent);
        env.Clock.UtcNow = env.Clock.UtcNow.AddMinutes(1); // lease expired: a worker may take over
        await using (var db = env.CreateContext())
        {
            Assert.Equal(EmailDeliveryResult.Sent, await env.CreateWorker(db, sender).ProcessNextAsync());
        }
    }

    [Fact]
    public async Task ProcessNextAsync_OneAddressFailsUntilMaxAttempts_RetriesOnlyThatRecipientThenExhausted()
    {
        await using var env = await AlertTestDatabase.CreateAsync();
        var box = await SeedAlertAsync(env);
        var failing = box.ActiveRelatives[0];
        var sender = new FakeEmailSender();
        sender.FailFor(failing.Email);

        for (var round = 0; round < AlertTestDatabase.MaxAttempts; round++)
        {
            await using var db = env.CreateContext();
            await DrainAsync(env.CreateWorker(db, sender));
            env.Clock.UtcNow = env.Clock.UtcNow.Add(AlertTestDatabase.RetryDelay);
        }

        await using (var db = env.CreateContext())
        {
            Assert.Equal(EmailDeliveryResult.NoDelivery, await env.CreateWorker(db, sender).ProcessNextAsync());
        }

        Assert.Equal(AlertTestDatabase.MaxAttempts, sender.FailedAttempts.Count(x => x == failing.Email));
        Assert.Equal(0, sender.SentCount(failing.Email));
        Assert.All(RecipientEmails(box).Where(x => x != failing.Email), email => Assert.Equal(1, sender.SentCount(email)));

        await using var verify = env.CreateContext();
        var failingRecipientId = await RecipientIdAsync(verify, failing.Id);
        var attempts = await verify.EmailDeliveries.Where(x => x.AlertRecipientId == failingRecipientId)
            .OrderBy(x => x.AttemptNumber).Select(x => new { x.AttemptNumber, x.Status }).ToListAsync();
        Assert.Equal(Enumerable.Range(1, AlertTestDatabase.MaxAttempts), attempts.Select(x => x.AttemptNumber));
        Assert.All(attempts.SkipLast(1), x => Assert.Equal(EmailDeliveryStatus.Failed, x.Status));
        Assert.Equal(EmailDeliveryStatus.Exhausted, attempts[^1].Status);
        Assert.Equal(box.ExpectedRecipientIds.Count(), await verify.AlertRecipients.CountAsync());
        Assert.Equal(box.ExpectedRecipientIds.Count() - 1 + AlertTestDatabase.MaxAttempts, await verify.EmailDeliveries.CountAsync());
    }

    [Fact]
    public async Task ProcessNextAsync_TransientFailure_RetriesAfterDelayWithNextAttemptNumber()
    {
        await using var env = await AlertTestDatabase.CreateAsync();
        var box = await SeedAlertAsync(env, activeRelatives: 0);
        var sender = new FakeEmailSender();
        sender.FailFor(box.Owner.Email, times: 1);

        await using (var db = env.CreateContext())
        {
            var worker = env.CreateWorker(db, sender);
            Assert.Equal(EmailDeliveryResult.Retried, await worker.ProcessNextAsync());
            Assert.Equal(EmailDeliveryResult.NoDelivery, await worker.ProcessNextAsync()); // retry not due yet
            env.Clock.UtcNow = env.Clock.UtcNow.Add(AlertTestDatabase.RetryDelay);
            Assert.Equal(EmailDeliveryResult.Sent, await worker.ProcessNextAsync());
        }

        Assert.Equal(1, sender.SentCount(box.Owner.Email));
        await using var verify = env.CreateContext();
        var attempts = await verify.EmailDeliveries.OrderBy(x => x.AttemptNumber).Select(x => new { x.AttemptNumber, x.Status }).ToListAsync();
        Assert.Equal([(1, EmailDeliveryStatus.Failed), (2, EmailDeliveryStatus.Sent)], attempts.Select(x => (x.AttemptNumber, x.Status)));
    }

    [Fact]
    public async Task ProcessNextAsync_RelativeRevokedAfterAlertCreated_CancelsOnlyThatDeliveryAndSendsOthers()
    {
        await using var env = await AlertTestDatabase.CreateAsync();
        var box = await SeedAlertAsync(env);
        var revoked = box.ActiveRelatives[0];
        await RevokeAsync(env, box, revoked.Id);
        var sender = new FakeEmailSender();

        await using (var db = env.CreateContext()) await DrainAsync(env.CreateWorker(db, sender));

        Assert.Equal(0, sender.SentCount(revoked.Email));
        Assert.All(RecipientEmails(box).Where(x => x != revoked.Email), email => Assert.Equal(1, sender.SentCount(email)));
        await using var verify = env.CreateContext();
        var revokedRecipientId = await RecipientIdAsync(verify, revoked.Id);
        var deliveries = await verify.EmailDeliveries.ToListAsync();
        Assert.Equal(box.ExpectedRecipientIds.Count(), deliveries.Count); // no further attempts created
        var cancelled = deliveries.Single(x => x.AlertRecipientId == revokedRecipientId);
        Assert.Equal(EmailDeliveryStatus.Cancelled, cancelled.Status);
        Assert.Null(cancelled.LockedUntil);
        Assert.Null(cancelled.SentAt);
        Assert.All(deliveries.Where(x => x.AlertRecipientId != revokedRecipientId), x => Assert.Equal(EmailDeliveryStatus.Sent, x.Status));
    }

    [Fact]
    public async Task ProcessNextAsync_CancelledDelivery_IsNeverReclaimed()
    {
        await using var env = await AlertTestDatabase.CreateAsync();
        var box = await SeedAlertAsync(env, activeRelatives: 1);
        await RevokeAsync(env, box, box.ActiveRelatives[0].Id);
        var sender = new FakeEmailSender();

        await using (var db = env.CreateContext())
        {
            var worker = env.CreateWorker(db, sender);
            var results = new[] { await worker.ProcessNextAsync(), await worker.ProcessNextAsync() };
            Assert.Equal([EmailDeliveryResult.Sent, EmailDeliveryResult.Cancelled], results.Order());
            env.Clock.UtcNow = env.Clock.UtcNow.AddDays(1);
            Assert.Equal(EmailDeliveryResult.NoDelivery, await worker.ProcessNextAsync());
        }

        Assert.Equal(1, sender.SentCount(box.Owner.Email));
        Assert.Equal(0, sender.SentCount(box.ActiveRelatives[0].Email));
        await using var verify = env.CreateContext();
        Assert.Equal(2, await verify.EmailDeliveries.CountAsync());
        Assert.Equal(1, await verify.EmailDeliveries.CountAsync(x => x.Status == EmailDeliveryStatus.Cancelled));
    }

    [Fact]
    public async Task ProcessNextAsync_OwnerUnpairedAfterAlertCreated_CancelsOwnerDelivery()
    {
        await using var env = await AlertTestDatabase.CreateAsync();
        var box = await SeedAlertAsync(env, activeRelatives: 0);
        await using (var db = env.CreateContext())
        {
            await db.Devices.Where(x => x.Id == box.DeviceId).ExecuteUpdateAsync(s => s.SetProperty(x => x.OwnerUserId, (Guid?)null));
        }

        var sender = new FakeEmailSender();
        await using (var db = env.CreateContext())
        {
            var worker = env.CreateWorker(db, sender);
            Assert.Equal(EmailDeliveryResult.Cancelled, await worker.ProcessNextAsync());
            Assert.Equal(EmailDeliveryResult.NoDelivery, await worker.ProcessNextAsync());
        }

        Assert.Empty(sender.Sent);
        await using var verify = env.CreateContext();
        Assert.Equal(EmailDeliveryStatus.Cancelled, (await verify.EmailDeliveries.SingleAsync()).Status);
    }

    [Fact]
    public async Task ProcessNextAsync_Sent_BodyIsRenderedTemplateFromDoseSnapshotNotCurrentSchedule()
    {
        await using var env = await AlertTestDatabase.CreateAsync();
        var box = await env.SeedBoxAsync(lastSeenAt: env.Clock.UtcNow, activeRelatives: 1, revokedRelatives: 0);
        // Deadline 23:59 UTC, scheduled 23:29 UTC on 11/11 = 06:29 on 12/11 in Asia/Ho_Chi_Minh.
        var doseId = await env.SeedDoseAsync(box, env.Clock.UtcNow.AddMinutes(-1));
        await using (var db = env.CreateContext())
        {
            db.DoseItemSnapshots.AddRange(
                new DoseItemSnapshot { Id = Guid.NewGuid(), DoseInstanceId = doseId, MedicineName = "Vitamin C", Quantity = 1, Unit = "viên" },
                new DoseItemSnapshot { Id = Guid.NewGuid(), DoseInstanceId = doseId, MedicineName = "Paracetamol 500mg", Quantity = 1, Unit = "viên" });
            // The current schedule changed after the dose was generated: must not leak into the email.
            db.ScheduleItems.Add(new ScheduleItem { Id = Guid.NewGuid(), ScheduleId = box.ScheduleId, MedicineName = "Thuốc mới", Quantity = 1, Unit = "viên" });
            await db.SaveChangesAsync();
            Assert.Equal(1, (await env.CreateEvaluator(db).EvaluateAsync()).CreatedCount);
        }

        var sender = new FakeEmailSender();
        await using (var db = env.CreateContext()) await DrainAsync(env.CreateWorker(db, sender));

        var expected = EmailTemplates.DoseAlert(
            "unused", AlertType.OverdueUnconfirmed, ["Paracetamol 500mg", "Vitamin C"],
            new DateTimeOffset(2026, 11, 11, 23, 29, 0, TimeSpan.Zero), "Asia/Ho_Chi_Minh", 1).Body;
        Assert.Contains("lúc 06:29 ngày 12/11/2026 ở ngăn Sáng (ngăn 1) đã quá hạn", expected);
        Assert.Equal(2, sender.Sent.Count);
        Assert.All(sender.Sent, x =>
        {
            Assert.Equal(expected, x.Body); // identical for owner and relative
            Assert.DoesNotContain("Thuốc mới", x.Body);
            Assert.DoesNotContain("@", x.Body);
        });
    }

    private static async Task RevokeAsync(AlertTestDatabase env, Box box, Guid relativeId)
    {
        await using var db = env.CreateContext();
        await db.CareRelationships.Where(x => x.DeviceId == box.DeviceId && x.RelativeUserId == relativeId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, RelationshipStatus.Revoked).SetProperty(x => x.RevokedAt, env.Clock.UtcNow));
    }

    private static async Task<Box> SeedAlertAsync(AlertTestDatabase env, int activeRelatives = 2)
    {
        var box = await env.SeedBoxAsync(lastSeenAt: env.Clock.UtcNow, activeRelatives: activeRelatives);
        await env.SeedDoseAsync(box, env.Clock.UtcNow.AddMinutes(-1));
        await using var db = env.CreateContext();
        Assert.Equal(1, (await env.CreateEvaluator(db).EvaluateAsync()).CreatedCount);
        return box;
    }

    private static async Task DrainAsync(EmailDeliveryWorker worker)
    {
        while (await worker.ProcessNextAsync() != EmailDeliveryResult.NoDelivery) { }
    }

    private static IEnumerable<string> RecipientEmails(Box box) => box.ActiveRelatives.Select(x => x.Email).Append(box.Owner.Email);

    private static Task<Guid> RecipientIdAsync(AppDbContext db, Guid userId) =>
        db.AlertRecipients.Where(x => x.RecipientUserId == userId).Select(x => x.Id).SingleAsync();
}
