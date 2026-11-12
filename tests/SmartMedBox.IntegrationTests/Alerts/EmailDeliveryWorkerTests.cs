using Microsoft.EntityFrameworkCore;
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
