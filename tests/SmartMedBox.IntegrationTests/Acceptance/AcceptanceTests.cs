using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartMedBox.Domain.Devices;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Email;
using SmartMedBox.Infrastructure.Prescriptions;
using SmartMedBox.Infrastructure.Storage;
using SmartMedBox.IntegrationTests.Prescriptions;
using SmartMedBox.IntegrationTests.Relatives;
using static SmartMedBox.IntegrationTests.Acceptance.AcceptanceHarness;

namespace SmartMedBox.IntegrationTests.Acceptance;

/// <summary>
/// P7-02 — acceptance criteria AC-01…AC-12 (TASKS §13.1) end to end: real HTTP API + SQL Server + jobs run on a fake clock.
/// Hardware parts (LED, buzzer, lid sensor, RTC, Wi-Fi) are emulated by <see cref="DeviceSimulator"/>, not real ESP32.
/// Scenario times are local Asia/Ho_Chi_Minh wall-clock times on <see cref="AcceptanceHarness.Day"/>.
/// </summary>
public sealed class AcceptanceTests
{
    private static readonly string[] ForbiddenPhrases = ["quên uống", "bỏ liều", "đã uống"];

    /// <summary>AC-01 — EMULATED firmware: only the scheduled slot's LED + buzzer are on at 07:00 (logic over synced schedule).</summary>
    [Fact]
    public async Task AC01_ReminderAtScheduledTime_OnlyScheduledSlotLedAndBuzzer()
    {
        await using var h = await StartAsync();
        var owner = await h.RegisterAsync("owner");
        var device = await h.ProvisionAndPairAsync(owner);
        await h.CreateScheduleAsync(owner, 1, "07:00", 15, "Thuốc A");
        await h.CreateScheduleAsync(owner, 2, "12:00", 15, "Thuốc B");
        await device.HeartbeatAsync(); // pulls the new schedule version

        Assert.Equal(2, device.LocalSchedules.Count);
        Assert.False(device.ReminderAt(h.At(6, 59, 59)).Buzzer);

        h.SetTime(h.At(7, 0));
        var state = device.Tick();
        Assert.Equal([true, false, false], state.Leds);
        Assert.True(state.Buzzer);
        Assert.Contains(device.Log, x => x.Contains("remind slots=1 buzzer=on"));
        Assert.DoesNotContain(device.Log, x => x.Contains("slots=2") || x.Contains("slots=3"));
        Assert.Equal([true, false, false], device.ReminderAt(h.At(7, 15)).Leds); // grace boundary inclusive
        Assert.False(device.ReminderAt(h.At(7, 15, 1)).Buzzer);
        Assert.Equal([false, true, false], device.ReminderAt(h.At(12, 0)).Leds);

        // Server side: the 07:00 dose is for slot 1 only.
        await h.GenerateDosesAsync();
        var dashboard = await GetJsonAsync(owner.Client, $"/api/dashboard?deviceId={device.DeviceKey}");
        var at7 = dashboard.GetProperty("today").GetProperty("doses").EnumerateArray()
            .Single(x => x.GetProperty("scheduledAt").GetDateTimeOffset() == h.At(7, 0));
        Assert.Equal(1, at7.GetProperty("compartmentSlot").GetInt32());
    }

    /// <summary>AC-02 — window [12:00, 12:15] slot 2: early / wrong slot do not confirm; 12:05 confirms; 12:15 inclusive.</summary>
    [Fact]
    public async Task AC02_ConfirmationWindow_EarlyAndWrongSlotRejected_InsideAndBoundaryConfirm()
    {
        await using var h = await StartAsync();
        var owner = await h.RegisterAsync("owner");
        var device = await h.ProvisionAndPairAsync(owner);
        await h.CreateScheduleAsync(owner, 2, "12:00", 15, "Thuốc B");
        await h.GenerateDosesAsync();

        // (a) slot 2 at 11:59 — too early.
        await OpenCloseAsync(h, device, 2, h.At(11, 59));
        Assert.Equal(DoseStatus.Pending, (await DoseAtAsync(h, h.At(12, 0))).Status);

        // (b) slot 1 at 12:05 — wrong compartment.
        await OpenCloseAsync(h, device, 1, h.At(12, 5));
        Assert.Equal(DoseStatus.Pending, (await DoseAtAsync(h, h.At(12, 0))).Status);

        // (c) slot 2 at 12:05 — confirmed at 12:05.
        await OpenCloseAsync(h, device, 2, h.At(12, 5, 0));
        var confirmed = await DoseAtAsync(h, h.At(12, 0));
        Assert.Equal(DoseStatus.Confirmed, confirmed.Status);
        Assert.Equal(h.At(12, 5), confirmed.ConfirmedAt);

        // (d) next day: slot 2 exactly at the 12:15 deadline — inclusive boundary.
        await OpenCloseAsync(h, device, 2, h.At(12, 15, dayOffset: 1));
        var boundary = await DoseAtAsync(h, h.At(12, 0, dayOffset: 1));
        Assert.Equal(DoseStatus.Confirmed, boundary.Status);
        Assert.Equal(h.At(12, 15, dayOffset: 1), boundary.ConfirmedAt);

        await using var db = h.CreateContext();
        Assert.Equal(8, await db.DeviceEvents.CountAsync(x => x.DeviceId == device.DeviceKey)); // every edge stored
    }

    /// <summary>AC-03 — EMULATED firmware edge detection: lid already open does not confirm; close + reopen does; reboot with lid open makes no edge.</summary>
    [Fact]
    public async Task AC03_LidAlreadyOpen_NotConfirmedUntilClosedAndReopened()
    {
        await using var h = await StartAsync();
        var owner = await h.RegisterAsync("owner");
        var device = await h.ProvisionAndPairAsync(owner);
        await h.CreateScheduleAsync(owner, 2, "12:00", 15, "Thuốc B");
        await h.GenerateDosesAsync();

        h.SetTime(h.At(11, 50));
        device.OpenLid(2);
        await device.FlushAsync();

        h.SetTime(h.At(12, 3));
        device.Reboot(); // lid still open at boot → no false edge
        Assert.Empty(device.Queue);

        h.SetTime(h.At(12, 5));
        device.SetLid(2, true); // still open: no edge
        Assert.Null(await device.FlushAsync());
        Assert.Equal(DoseStatus.Pending, (await DoseAtAsync(h, h.At(12, 0))).Status);

        h.SetTime(h.At(12, 6));
        device.CloseLid(2);
        h.SetTime(h.At(12, 7));
        device.OpenLid(2);
        await device.FlushAsync();

        var dose = await DoseAtAsync(h, h.At(12, 0));
        Assert.Equal(DoseStatus.Confirmed, dose.Status);
        Assert.Equal(h.At(12, 7), dose.ConfirmedAt);
        await using var db = h.CreateContext();
        Assert.True(await db.DeviceEvents.AnyAsync(x => x.DeviceId == device.DeviceKey && x.OccurredAt == h.At(11, 50) && x.EventType == DeviceEventType.LidOpened));
        Assert.Equal(3, await db.DeviceEvents.CountAsync(x => x.DeviceId == device.DeviceKey));
    }

    /// <summary>AC-04 — EMULATED Wi-Fi loss: reminder still driven by the RTC, event queued (survives power loss), reconnect stores it once.</summary>
    [Fact]
    public async Task AC04_OfflineAtReminder_StillRemindsQueuesAndUploadsOnceOnReconnect()
    {
        await using var h = await StartAsync();
        var owner = await h.RegisterAsync("owner");
        var device = await h.ProvisionAndPairAsync(owner);
        await h.CreateScheduleAsync(owner, 1, "07:00", 15, "Thuốc A");
        await h.GenerateDosesAsync();
        h.SetTime(h.At(6, 55));
        await device.HeartbeatAsync();

        device.Online = false; // Wi-Fi lost before 07:00
        h.SetTime(h.At(7, 0));
        Assert.Equal([true, false, false], device.Tick().Leds);
        Assert.True(device.Tick().Buzzer);

        h.SetTime(h.At(7, 3));
        device.OpenLid(1);
        h.SetTime(h.At(7, 4));
        device.CloseLid(1);
        Assert.Null(await device.FlushAsync());
        Assert.Equal(2, device.Queue.Count);

        device.PowerOff(); // power loss: queue and RTC survive
        h.SetTime(h.At(7, 8));
        device.PowerOn();
        Assert.Equal(2, device.Queue.Count);

        await using (var db = h.CreateContext())
        {
            Assert.False(await db.DeviceEvents.AnyAsync(x => x.DeviceId == device.DeviceKey));
        }

        h.SetTime(h.At(7, 10));
        device.Online = true;
        await device.HeartbeatAsync();
        var results = await device.FlushAsync();
        Assert.All(results!, x => Assert.Equal("Accepted", x.GetProperty("status").GetString()));
        Assert.Empty(device.Queue);

        await using var verify = h.CreateContext();
        Assert.Equal(1, await verify.DeviceEvents.CountAsync(x => x.DeviceId == device.DeviceKey && x.EventType == DeviceEventType.LidOpened));
        var dose = await DoseAtAsync(h, h.At(7, 0));
        Assert.Equal(DoseStatus.Confirmed, dose.Status);
        Assert.Equal(h.At(7, 3), dose.ConfirmedAt);
    }

    /// <summary>AC-05 — the same batch sent 3 times (shuffled): one event per EventId, one Confirmed, no alert.</summary>
    [Fact]
    public async Task AC05_SameBatchSentThreeTimes_OneResultAndDuplicates()
    {
        await using var h = await StartAsync();
        var owner = await h.RegisterAsync("owner");
        var device = await h.ProvisionAndPairAsync(owner);
        await h.CreateScheduleAsync(owner, 1, "07:00", 15, "Thuốc A");
        await h.GenerateDosesAsync();

        h.SetTime(h.At(7, 2));
        device.OpenLid(1);
        h.SetTime(h.At(7, 3));
        device.CloseLid(1);
        await device.HeartbeatAsync();
        var first = await device.FlushAsync();
        var second = await device.ResendLastBatchAsync(shuffle: true);
        var third = await device.ResendLastBatchAsync(shuffle: true);

        Assert.All(first!, x => Assert.Equal("Accepted", x.GetProperty("status").GetString()));
        Assert.All(second.Concat(third), x => Assert.Equal("Duplicate", x.GetProperty("status").GetString()));

        h.SetTime(h.At(7, 30));
        await h.EvaluateAlertsAsync();
        await using var db = h.CreateContext();
        var eventIds = device.LastBatch.Select(x => x.EventId).ToList();
        foreach (var eventId in eventIds)
        {
            Assert.Equal(1, await db.DeviceEvents.CountAsync(x => x.DeviceId == device.DeviceKey && x.EventId == eventId));
        }

        Assert.Equal(2, await db.DeviceEvents.CountAsync(x => x.DeviceId == device.DeviceKey));
        var dose = await DoseAtAsync(h, h.At(7, 0));
        Assert.Equal(DoseStatus.Confirmed, dose.Status);
        Assert.Equal(h.At(7, 2), dose.ConfirmedAt);
        Assert.Equal(0, await db.Alerts.CountAsync());
        Assert.Equal(0, await db.AlertRecipients.CountAsync());
    }

    /// <summary>AC-06 — offline at DeadlineAt → Unknown + "không thể xác nhận do mất kết nối"; reconnecting later does not change it.</summary>
    [Fact]
    public async Task AC06_OfflineAtDeadline_UnknownWithCannotConfirmWording()
    {
        await using var h = await StartAsync();
        var owner = await h.RegisterAsync("owner");
        var relative = await h.RegisterAsync("relative");
        var device = await h.ProvisionAndPairAsync(owner);
        await h.InviteAndAcceptAsync(owner, relative);
        await h.CreateScheduleAsync(owner, 1, "07:00", 15, "Thuốc A");
        await h.GenerateDosesAsync();
        h.SetTime(h.At(6, 58));
        await device.HeartbeatAsync();
        device.Online = false;

        h.SetTime(h.At(7, 30)); // job runs after the deadline, device still offline
        await h.EvaluateAlertsAsync();
        h.SetTime(h.At(7, 40));
        device.Online = true;
        await device.HeartbeatAsync(); // reconnect after the deadline
        await h.EvaluateAlertsAsync();
        var sentBefore = h.Emails.Sent.Count;
        Assert.All(await h.DeliverEmailsAsync(), x => Assert.Equal(EmailDeliveryResult.Sent, x));

        Assert.Equal(DoseStatus.Unknown, (await DoseAtAsync(h, h.At(7, 0))).Status);
        foreach (var user in new[] { owner, relative })
        {
            var dashboard = await GetJsonAsync(user.Client, $"/api/dashboard?deviceId={device.DeviceKey}");
            var dose = dashboard.GetProperty("today").GetProperty("doses").EnumerateArray()
                .Single(x => x.GetProperty("scheduledAt").GetDateTimeOffset() == h.At(7, 0));
            Assert.Equal("Unknown", dose.GetProperty("status").GetString());
            var alert = Assert.Single(dashboard.GetProperty("alerts").GetProperty("latest").EnumerateArray());
            Assert.Equal("UnconfirmedOffline", alert.GetProperty("alertType").GetString());
            var message = alert.GetProperty("message").GetString()!;
            Assert.Contains("không thể xác nhận do mất kết nối", message, StringComparison.OrdinalIgnoreCase);
            AssertNoForbiddenWording(message);

            var email = Assert.Single(h.Emails.Sent.Skip(sentBefore), x => x.To == user.Email);
            Assert.Contains("không thể xác nhận do mất kết nối", email.Subject + email.Body, StringComparison.OrdinalIgnoreCase);
            AssertNoForbiddenWording(email.Subject + email.Body);

            foreach (var page in new[] { "/dashboard", "/alerts" })
            {
                using var html = await user.Client.GetAsync(page);
                Assert.Equal(HttpStatusCode.OK, html.StatusCode);
                AssertNoForbiddenWording(await html.Content.ReadAsStringAsync());
            }
        }
    }

    /// <summary>AC-07 — accepted relative can read but not change schedules/invitations, and cannot read prescription images.</summary>
    [Fact]
    public async Task AC07_RelativeReadsButCannotWriteOrSeeImages_OwnerCan()
    {
        await using var h = await StartAsync();
        var owner = await h.RegisterAsync("owner");
        var relative = await h.RegisterAsync("relative");
        var device = await h.ProvisionAndPairAsync(owner);
        await h.InviteAndAcceptAsync(owner, relative);
        var scheduleId = await h.CreateScheduleAsync(owner, 1, "07:00", 15, "Thuốc A");
        using (var pending = await RelativesTestSupport.InviteAsync(owner.Client, $"pending-{Guid.NewGuid():N}@example.com"))
        {
            Assert.Equal(HttpStatusCode.Created, pending.StatusCode);
        }

        var invitationId = (await GetJsonAsync(owner.Client, $"/api/relatives/invitations?deviceId={device.DeviceKey}"))
            .GetProperty("items").EnumerateArray().Single(x => x.GetProperty("status").GetString() == "Pending").GetProperty("id").GetGuid();
        Guid imageId;
        using (var upload = await owner.Client.PostAsync("/api/prescriptions/images",
                   PrescriptionUploadApiTests.Multipart(PrescriptionTestSupport.Jpeg(), "don.jpg")))
        {
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
            imageId = (await RelativesTestSupport.ReadJsonAsync(upload)).GetProperty("id").GetGuid();
        }

        try
        {
            foreach (var url in new[]
                     {
                         $"/api/schedules?deviceId={device.DeviceKey}", $"/api/schedules/{scheduleId}", $"/api/dashboard?deviceId={device.DeviceKey}",
                         $"/api/dose-history?deviceId={device.DeviceKey}", $"/api/alerts?deviceId={device.DeviceKey}",
                     })
            {
                await GetJsonAsync(relative.Client, url);
            }

            var body = h.ScheduleBody(2, "12:00", 15, "Thuốc B");
            await AssertForbiddenAsync(relative.Client.PostAsJsonAsync("/api/schedules", body));
            await AssertForbiddenAsync(relative.Client.PutAsJsonAsync($"/api/schedules/{scheduleId}", body));
            await AssertForbiddenAsync(relative.Client.DeleteAsync($"/api/schedules/{scheduleId}"));
            await AssertForbiddenAsync(RelativesTestSupport.InviteAsync(relative.Client, $"x-{Guid.NewGuid():N}@example.com"));
            await AssertForbiddenAsync(relative.Client.PostAsync($"/api/relatives/invitations/{invitationId}/revoke", null));
            await AssertForbiddenAsync(relative.Client.GetAsync($"/api/prescriptions/images/{imageId}/content"));

            // Owner, same operations → success.
            using (var create = await owner.Client.PostAsJsonAsync("/api/schedules", body)) Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            using (var update = await owner.Client.PutAsJsonAsync($"/api/schedules/{scheduleId}", h.ScheduleBody(1, "07:30", 15, "Thuốc A")))
                Assert.Equal(HttpStatusCode.OK, update.StatusCode);
            using (var invite = await RelativesTestSupport.InviteAsync(owner.Client, $"y-{Guid.NewGuid():N}@example.com"))
                Assert.Equal(HttpStatusCode.Created, invite.StatusCode);
            using (var revoke = await owner.Client.PostAsync($"/api/relatives/invitations/{invitationId}/revoke", null))
                Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
            using (var content = await owner.Client.GetAsync($"/api/prescriptions/images/{imageId}/content"))
                Assert.Equal(HttpStatusCode.OK, content.StatusCode);
            using (var delete = await owner.Client.DeleteAsync($"/api/schedules/{scheduleId}"))
                Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }
        finally
        {
            await DeleteStoredImagesAsync(h);
        }
    }

    /// <summary>AC-08 — pairing code once; rescan 409; no key/hash in QR or response; concurrent pair → exactly one wins.</summary>
    [Fact]
    public async Task AC08_PairOnce_RescanRejected_NoKeyLeak_ConcurrentOneWins()
    {
        await using var h = await StartAsync();
        var owner = await h.RegisterAsync("owner");
        var other = await h.RegisterAsync("other");
        var device = await h.ProvisionDeviceAsync();
        string hash;
        await using (var db = h.CreateContext())
        {
            hash = await db.Devices.Where(x => x.Id == device.DeviceKey).Select(x => x.DeviceApiKeyHash).SingleAsync();
        }

        var qr = PairingCode.QrPayload(device.PairingCode);
        Assert.DoesNotContain(device.ApiKey, qr);
        Assert.DoesNotContain(hash, qr);
        Assert.Equal("Unpaired", (await device.HeartbeatAsync()).GetProperty("pairingState").GetString());

        using (var pair = await PairAsync(owner, device.PairingCode))
        {
            Assert.Equal(HttpStatusCode.OK, pair.StatusCode);
            var json = await pair.Content.ReadAsStringAsync();
            Assert.DoesNotContain(device.ApiKey, json);
            Assert.DoesNotContain(hash, json);
            Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var user in new[] { owner, other })
        {
            using var rescan = await PairAsync(user, device.PairingCode);
            Assert.Equal(HttpStatusCode.Conflict, rescan.StatusCode);
            Assert.Equal("PAIRING_CODE_USED", await RelativesTestSupport.ReadCodeAsync(rescan));
        }

        Assert.Equal("Paired", (await device.HeartbeatAsync()).GetProperty("pairingState").GetString());
        using (var wrongKey = await device.SendAsync(HttpMethod.Post, "/api/device/heartbeat", new { }, apiKey: "wrong-key"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, wrongKey.StatusCode);
        }

        var contested = await h.ProvisionDeviceAsync();
        var a = await h.RegisterAsync("a");
        var b = await h.RegisterAsync("b");
        var responses = await Task.WhenAll(PairAsync(a, contested.PairingCode), PairAsync(b, contested.PairingCode));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in responses) response.Dispose();
        await using var verify = h.CreateContext();
        Assert.NotNull(await verify.Devices.Where(x => x.Id == contested.DeviceKey).Select(x => x.OwnerUserId).SingleAsync());
    }

    /// <summary>AC-09 — owner + 2 accepted + 1 revoked: 3 recipients; one failing address retried without resending the others.</summary>
    [Fact]
    public async Task AC09_OverdueOnline_ThreeRecipients_RetryOnlyFailedAddress()
    {
        await using var h = await StartAsync();
        var owner = await h.RegisterAsync("owner");
        var relative1 = await h.RegisterAsync("relative1");
        var relative2 = await h.RegisterAsync("relative2");
        var revoked = await h.RegisterAsync("revoked");
        var device = await h.ProvisionAndPairAsync(owner);
        await h.InviteAndAcceptAsync(owner, relative1);
        await h.InviteAndAcceptAsync(owner, relative2);
        var revokedRelationship = await h.InviteAndAcceptAsync(owner, revoked);
        using (var revoke = await owner.Client.DeleteAsync($"/api/relatives/relationships/{revokedRelationship}"))
        {
            Assert.True(revoke.IsSuccessStatusCode);
        }

        await h.CreateScheduleAsync(owner, 1, "07:00", 15, "Thuốc A");
        await h.GenerateDosesAsync();
        h.SetTime(h.At(7, 14));
        await device.HeartbeatAsync(); // online at the deadline (07:15)

        h.SetTime(h.At(7, 16));
        await h.EvaluateAlertsAsync();
        await h.EvaluateAlertsAsync(); // a second run must not duplicate
        var sentBefore = h.Emails.Sent.Count;
        h.Emails.FailFor(relative2.Email, 1);
        var firstRound = await h.DeliverEmailsAsync();

        Assert.Equal(2, firstRound.Count(x => x == EmailDeliveryResult.Sent));
        Assert.Equal(1, firstRound.Count(x => x == EmailDeliveryResult.Retried));
        Assert.Equal(DoseStatus.Overdue, (await DoseAtAsync(h, h.At(7, 0))).Status);

        h.SetTime(h.At(7, 18)); // D-13: first retry after 1 minute
        Assert.Equal([EmailDeliveryResult.Sent], await h.DeliverEmailsAsync());
        h.SetTime(h.At(9, 0));
        Assert.Empty(await h.DeliverEmailsAsync());

        var alertEmails = h.Emails.Sent.Skip(sentBefore).ToList();
        Assert.Equal(3, alertEmails.Count);
        foreach (var user in new[] { owner, relative1, relative2 })
        {
            var email = Assert.Single(alertEmails, x => x.To == user.Email);
            AssertNoForbiddenWording(email.Subject + email.Body);
        }

        Assert.DoesNotContain(alertEmails, x => x.To == revoked.Email);
        await using var db = h.CreateContext();
        var alert = await db.Alerts.SingleAsync();
        Assert.Equal(AlertType.OverdueUnconfirmed, alert.AlertType);
        var recipients = await db.AlertRecipients.Where(x => x.AlertId == alert.Id).Select(x => x.RecipientUserId).ToListAsync();
        Assert.Equal(new[] { owner.Id, relative1.Id, relative2.Id }.Order(), recipients.Order());
    }

    /// <summary>AC-10 — dose generated 07:00/15/A keeps its snapshot after the schedule becomes 08:00/30/B.</summary>
    [Fact]
    public async Task AC10_EditScheduleAfterGeneration_HistoryAndSnapshotUnchanged()
    {
        await using var h = await StartAsync();
        var owner = await h.RegisterAsync("owner");
        var device = await h.ProvisionAndPairAsync(owner);
        var scheduleId = await h.CreateScheduleAsync(owner, 1, "07:00", 15, "Thuốc A");
        await h.GenerateDosesAsync();
        h.SetTime(h.At(7, 5));
        device.OpenLid(1);
        await device.HeartbeatAsync();
        await device.FlushAsync();

        h.SetTime(h.At(7, 30));
        using (var update = await owner.Client.PutAsJsonAsync($"/api/schedules/{scheduleId}", h.ScheduleBody(1, "08:00", 30, "Thuốc B")))
        {
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        }

        await h.GenerateDosesAsync();

        var old = await DoseAtAsync(h, h.At(7, 0));
        Assert.Equal(15, old.GracePeriodMinutes);
        Assert.Equal(h.At(7, 15), old.DeadlineAt);
        Assert.Equal(DoseStatus.Confirmed, old.Status);
        await using (var db = h.CreateContext())
        {
            Assert.Equal(["Thuốc A"], await db.DoseItemSnapshots.Where(x => x.DoseInstanceId == old.Id).Select(x => x.MedicineName).ToListAsync());
        }

        var history = await GetJsonAsync(owner.Client, $"/api/dose-history?deviceId={device.DeviceKey}");
        var item = history.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == old.Id);
        Assert.Equal(h.At(7, 0), item.GetProperty("scheduledAt").GetDateTimeOffset());
        Assert.Equal(15, item.GetProperty("gracePeriodMinutes").GetInt32());
        Assert.Equal("Thuốc A", item.GetProperty("items")[0].GetProperty("medicineName").GetString());

        var schedule = await GetJsonAsync(owner.Client, $"/api/schedules/{scheduleId}");
        Assert.Equal("08:00", schedule.GetProperty("timeOfDay").GetString());
        Assert.Equal(30, schedule.GetProperty("gracePeriodMinutes").GetInt32());
        Assert.Equal("Thuốc B", schedule.GetProperty("items")[0].GetProperty("medicineName").GetString());

        var next = await DoseAtAsync(h, h.At(8, 0, dayOffset: 1));
        Assert.Equal(30, next.GracePeriodMinutes);
    }

    /// <summary>AC-11 — upload rules: .exe as .jpg 415; 5,242,881 bytes 413; EXIF GPS stripped; no binary in DB; outside wwwroot; relative 403.</summary>
    [Fact]
    public async Task AC11_PrescriptionUploadRules()
    {
        await using var h = await StartAsync();
        var owner = await h.RegisterAsync("owner");
        var relative = await h.RegisterAsync("relative");
        await h.ProvisionAndPairAsync(owner);
        await h.InviteAndAcceptAsync(owner, relative);
        const string url = "/api/prescriptions/images";
        try
        {
            byte[] exe = [.. "MZ\x90\0\x03\0\0\0\x04\0\0\0\xFF\xFF"u8, .. new byte[200]];
            await AssertCodeAsync(owner.Client.PostAsync(url, PrescriptionUploadApiTests.Multipart(exe, "don-thuoc.jpg")),
                HttpStatusCode.UnsupportedMediaType, "FILE_TYPE_NOT_ALLOWED");
            Assert.Equal(5_242_881, PrescriptionImageService.MaxBytes + 1);
            await AssertCodeAsync(owner.Client.PostAsync(url, PrescriptionUploadApiTests.Multipart(PrescriptionUploadApiTests.PaddedJpeg(5_242_881), "big.jpg")),
                HttpStatusCode.RequestEntityTooLarge, "FILE_TOO_LARGE");

            Guid id;
            using (var upload = await owner.Client.PostAsync(url, PrescriptionUploadApiTests.Multipart(PrescriptionUploadApiTests.JpegWithGps(), "gps.jpg")))
            {
                Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
                id = (await RelativesTestSupport.ReadJsonAsync(upload)).GetProperty("id").GetGuid();
            }

            await using var db = h.CreateContext();
            var row = await db.PrescriptionImages.AsNoTracking().SingleAsync(x => x.Id == id);
            var rootPath = Path.GetFullPath(h.Factory.Services.GetRequiredService<IOptions<FileStorageOptions>>().Value.RootPath);
            var webRoot = Path.GetFullPath(h.Factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath);
            var path = Path.Combine(rootPath, row.StorageKey);
            Assert.False(path.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            var stored = await File.ReadAllBytesAsync(path);
            Assert.True(stored.AsSpan().IndexOf("Exif"u8) < 0);
            Assert.True(stored.AsSpan().IndexOf(Encoding.ASCII.GetBytes(PrescriptionUploadApiTests.Gps)) < 0);

            var binaryColumns = await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'PrescriptionImages' AND DATA_TYPE IN ('varbinary','binary','image')")
                .SingleAsync();
            Assert.Equal(0, binaryColumns);

            using (var asRelative = await relative.Client.GetAsync($"{url}/{id}/content")) Assert.Equal(HttpStatusCode.Forbidden, asRelative.StatusCode);
            using (var asOwner = await owner.Client.GetAsync($"{url}/{id}/content")) Assert.Equal(HttpStatusCode.OK, asOwner.StatusCode);
            using (var direct = await h.CreateClient().GetAsync($"/{row.StorageKey}")) Assert.NotEqual(HttpStatusCode.OK, direct.StatusCode);
        }
        finally
        {
            await DeleteStoredImagesAsync(h);
        }
    }

    /// <summary>AC-12 — register → verification email → verify → login; forgot → token → reset → login with new password; old token rejected.</summary>
    [Fact]
    public async Task AC12_AccountChain_RegisterVerifyLoginForgotReset()
    {
        await using var h = await StartAsync();
        var user = await h.RegisterAsync("account"); // register + email + verify + login, asserted inside
        Assert.True((await GetJsonAsync(user.Client, "/api/users/me")).TryGetProperty("email", out _));

        var anonymous = h.CreateClient(); // forgotten password: the user is not logged in
        using (var forgot = await anonymous.PostAsJsonAsync("/api/auth/forgot-password", new { email = user.Email }))
        {
            Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);
        }

        var token = h.LastTokenTo(user.Email);
        const string newPassword = "Matkhaumoi456";
        using (var reset = await anonymous.PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword }))
        {
            Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        }

        var fresh = h.CreateClient();
        using (var oldPassword = await LoginAsync(fresh, user.Email, TestSupport.TestUsers.Password, ensureSuccess: false))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        }

        (await LoginAsync(fresh, user.Email, newPassword)).Dispose();
        Assert.Equal(user.Id, (await GetJsonAsync(fresh, "/api/users/me")).GetProperty("id").GetGuid());

        using var reuse = await anonymous.PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword = "Matkhaukhac789" });
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
        Assert.Equal("TOKEN_INVALID", await RelativesTestSupport.ReadCodeAsync(reuse));
    }

    // ---- helpers ----

    private static async Task OpenCloseAsync(AcceptanceHarness h, DeviceSimulator device, int slot, DateTimeOffset at)
    {
        h.SetTime(at);
        device.OpenLid(slot);
        h.SetTime(at.AddSeconds(20));
        device.CloseLid(slot);
        await device.FlushAsync();
    }

    private static async Task<DoseInstance> DoseAtAsync(AcceptanceHarness h, DateTimeOffset scheduledAt)
    {
        await using var db = h.CreateContext();
        return await db.DoseInstances.AsNoTracking().SingleAsync(x => x.ScheduledAt == scheduledAt);
    }

    private static void AssertNoForbiddenWording(string text)
    {
        foreach (var phrase in ForbiddenPhrases)
        {
            Assert.DoesNotContain(phrase, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static Task AssertForbiddenAsync(Task<HttpResponseMessage> call) => AssertCodeAsync(call, HttpStatusCode.Forbidden, null);

    private static async Task AssertCodeAsync(Task<HttpResponseMessage> call, HttpStatusCode status, string? code)
    {
        using var response = await call;
        Assert.Equal(status, response.StatusCode);
        if (code is not null) Assert.Equal(code, await RelativesTestSupport.ReadCodeAsync(response));
    }

    // Uploaded files land in the shared App_Data folder, not the per-test DB: remove them like PrescriptionUploadApiTests does.
    private static async Task DeleteStoredImagesAsync(AcceptanceHarness h)
    {
        var rootPath = Path.GetFullPath(h.Factory.Services.GetRequiredService<IOptions<FileStorageOptions>>().Value.RootPath);
        await using var db = h.CreateContext();
        foreach (var key in await db.PrescriptionImages.Select(x => x.StorageKey).ToListAsync())
        {
            File.Delete(Path.Combine(rootPath, key));
        }
    }
}
