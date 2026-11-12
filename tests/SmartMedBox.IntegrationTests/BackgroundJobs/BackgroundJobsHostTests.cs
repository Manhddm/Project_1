using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;
using SmartMedBox.IntegrationTests.Alerts;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.BackgroundJobs;

public sealed class BackgroundJobsHostTests
{
    [Fact]
    public async Task Host_JobsEnabled_GeneratesDosesEvaluatesOverdueAndSendsEmails()
    {
        await using var env = await AlertTestDatabase.CreateAsync();
        var box = await env.SeedBoxAsync(lastSeenAt: null, activeRelatives: 1, revokedRelatives: 0);
        await using (var db = env.CreateContext())
        {
            db.ScheduleDays.AddRange(Enumerable.Range(1, 7).Select(day => new ScheduleDay { ScheduleId = box.ScheduleId, DayOfWeek = day }));
            await db.SaveChangesAsync();
        }

        // Host uses the real clock; this dose is already past its deadline whatever time the test runs.
        var overdueId = await env.SeedDoseAsync(box, DateTimeOffset.UtcNow.AddMinutes(-5));

        await using var baseFactory = new TestApplicationFactory(env.ConnectionString);
        await using var factory = baseFactory.WithWebHostBuilder(b => b
            .UseSetting("BackgroundJobs:Enabled", "true")
            .UseSetting("BackgroundJobs:DoseGenerationInterval", "00:00:00.200")
            .UseSetting("BackgroundJobs:AlertEvaluationInterval", "00:00:00.200")
            .UseSetting("BackgroundJobs:EmailDeliveryIdleDelay", "00:00:00.200"));
        _ = factory.Services; // starts the host and its hosted services

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (true)
        {
            await using var db = env.CreateContext();
            var generated = await db.DoseInstances.CountAsync(x => x.ScheduleId == box.ScheduleId && x.Id != overdueId && x.ScheduledAt > DateTimeOffset.UtcNow);
            var status = await db.DoseInstances.Where(x => x.Id == overdueId).Select(x => x.Status).SingleAsync();
            var sent = await (from delivery in db.EmailDeliveries
                              join recipient in db.AlertRecipients on delivery.AlertRecipientId equals recipient.Id
                              join alert in db.Alerts on recipient.AlertId equals alert.Id
                              where alert.DoseInstanceId == overdueId && delivery.Status == EmailDeliveryStatus.Sent
                              select delivery).CountAsync();
            if (generated > 0 && status == DoseStatus.Unknown && sent == box.ExpectedRecipientIds.Count()) break;
            Assert.True(DateTimeOffset.UtcNow < deadline, $"Jobs did not finish: generated={generated}, status={status}, sent={sent}.");
            await Task.Delay(200);
        }

        Assert.Contains(baseFactory.Emails.Messages, x => x.To == box.Owner.Email);
    }
}
