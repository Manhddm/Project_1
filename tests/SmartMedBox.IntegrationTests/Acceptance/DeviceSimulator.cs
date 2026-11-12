using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Domain.Devices;
using SmartMedBox.Infrastructure.Devices;

namespace SmartMedBox.IntegrationTests.Acceptance;

internal sealed record QueuedEvent(string EventId, int CompartmentSlot, string EventType, DateTimeOffset OccurredAt);

internal sealed record LocalSchedule(
    int Slot, TimeOnly TimeOfDay, IReadOnlyList<int> DaysOfWeek, DateOnly StartDate, DateOnly? EndDate, int Grace, string TimeZoneId);

/// <summary>LED per slot (index 0 = slot 1) and the buzzer at one RTC instant.</summary>
internal sealed record ReminderState(bool[] Leds, bool Buzzer)
{
    public IEnumerable<int> LitSlots => Leds.Select((on, i) => (on, slot: i + 1)).Where(x => x.on).Select(x => x.slot);
}

/// <summary>
/// P7-02b — software stand-in for the ESP32 firmware (HW-02…HW-04). NOT real hardware: LEDs, buzzer, lid sensors,
/// RTC and flash storage are emulated in memory, so firmware behaviour verified here still needs a bench test on the device.
/// What it models (AGENTS §9.2): own RTC (settable skew), local schedule from schedule-sync, lid edges only on
/// closed→open / open→closed, boot initialises lid state from the sensors (no false edge), persistent queue with stable
/// EventIds sent in batches when online, batch resend/shuffle, heartbeat, power loss (queue and RTC survive).
/// </summary>
internal sealed class DeviceSimulator(HttpClient http, ProvisioningResult provisioning, IClock serverClock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly bool[] _physicalLidOpen = new bool[3]; // real world, changes even while powered off
    private readonly bool[] _firmwareLidOpen = new bool[3]; // last state the firmware sampled
    private readonly List<QueuedEvent> _queue = [];          // "flash": survives power loss
    private int _sequence;                                    // "flash": keeps EventIds unique across reboots

    public string DeviceId => provisioning.Provisioned.Device.DeviceId;
    public Guid DeviceKey => provisioning.Provisioned.Device.Id;
    public string ApiKey => provisioning.DeviceApiKey;
    public string PairingCode => provisioning.PairingCode.Code;

    /// <summary>RTC = server clock + skew (battery-backed: keeps running through power loss).</summary>
    public TimeSpan RtcSkew { get; set; }
    public DateTimeOffset RtcNow => serverClock.UtcNow + RtcSkew;

    public bool Online { get; set; } = true;
    public bool Powered { get; private set; } = true;

    public int LocalScheduleVersion { get; private set; }
    public IReadOnlyList<LocalSchedule> LocalSchedules { get; private set; } = [];
    public IReadOnlyList<QueuedEvent> Queue => _queue;
    public List<QueuedEvent> LastBatch { get; private set; } = [];
    public List<string> Log { get; } = [];

    // ---- Lids ----

    public void OpenLid(int slot) => SetLid(slot, true);
    public void CloseLid(int slot) => SetLid(slot, false);

    /// <summary>Physical change. A powered firmware samples it and queues an event only on a real edge.</summary>
    public void SetLid(int slot, bool open)
    {
        _physicalLidOpen[slot - 1] = open;
        if (Powered) SampleLids();
    }

    private void SampleLids()
    {
        for (var i = 0; i < 3; i++)
        {
            if (_physicalLidOpen[i] == _firmwareLidOpen[i]) continue;
            _firmwareLidOpen[i] = _physicalLidOpen[i];
            _queue.Add(new QueuedEvent($"{DeviceId}-{++_sequence:D6}", i + 1, _physicalLidOpen[i] ? "LidOpened" : "LidClosed", RtcNow));
        }
    }

    public void PowerOff() => Powered = false;

    /// <summary>Boot: lid state is initialised from the sensors, so a lid that is already open creates no event (AGENTS §4.1).</summary>
    public void PowerOn()
    {
        Array.Copy(_physicalLidOpen, _firmwareLidOpen, 3);
        Powered = true;
    }

    public void Reboot()
    {
        PowerOff();
        PowerOn();
    }

    // ---- Reminders (pure logic over the local schedule and the RTC) ----

    /// <summary>Slots whose window [TimeOfDay, TimeOfDay + grace] contains the RTC time, in each schedule's own timezone.</summary>
    public ReminderState ReminderAt(DateTimeOffset rtc)
    {
        var leds = new bool[3];
        foreach (var s in LocalSchedules)
        {
            var local = TimeZoneInfo.ConvertTime(rtc, TimeZoneInfo.FindSystemTimeZoneById(s.TimeZoneId)).DateTime;
            // A window may start the previous local day (e.g. 23:50 + 15 min).
            foreach (var date in new[] { DateOnly.FromDateTime(local), DateOnly.FromDateTime(local).AddDays(-1) })
            {
                if (date < s.StartDate || date > s.EndDate || !s.DaysOfWeek.Contains(IsoDay(date))) continue;
                var start = date.ToDateTime(s.TimeOfDay);
                if (local >= start && local <= start.AddMinutes(s.Grace)) leds[s.Slot - 1] = true;
            }
        }

        return new ReminderState(leds, leds.Any(x => x));
    }

    /// <summary>One firmware loop iteration: drive LEDs/buzzer from the RTC and log what is lit (telemetry).</summary>
    public ReminderState Tick()
    {
        var state = ReminderAt(RtcNow);
        if (Powered && state.Buzzer)
        {
            Log.Add($"{RtcNow:O} remind slots={string.Join(',', state.LitSlots)} buzzer=on");
        }

        return state;
    }

    private static int IsoDay(DateOnly date) => date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;

    // ---- Network ----

    /// <summary>POST heartbeat; corrects the RTC when off by more than 2 s and pulls a newer schedule (D-26, API_CONTRACT §9).</summary>
    public async Task<JsonElement> HeartbeatAsync()
    {
        EnsureConnected();
        using var response = await SendAsync(HttpMethod.Post, "/api/device/heartbeat",
            new { firmwareVersion = "sim-1.0", rtcTimeUtc = RtcNow, pendingEventCount = _queue.Count });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        var serverTime = body.GetProperty("serverTimeUtc").GetDateTimeOffset();
        if ((RtcNow - serverTime).Duration() > TimeSpan.FromSeconds(2)) RtcSkew = TimeSpan.Zero;
        if (body.GetProperty("pairingState").GetString() == "Paired" && body.GetProperty("scheduleVersion").GetInt32() != LocalScheduleVersion)
        {
            await SyncScheduleAsync();
        }

        return body;
    }

    /// <summary>Replaces the local schedule only when the whole package parsed; otherwise the old one stays.</summary>
    public async Task SyncScheduleAsync()
    {
        EnsureConnected();
        using var response = await SendAsync(HttpMethod.Get, $"/api/device/schedule-sync?version={LocalScheduleVersion}");
        if (response.StatusCode == HttpStatusCode.NoContent) return;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        var parsed = body.GetProperty("schedules").EnumerateArray().Select(x => new LocalSchedule(
            x.GetProperty("compartmentSlot").GetInt32(),
            TimeOnly.ParseExact(x.GetProperty("timeOfDay").GetString()!, "HH:mm", CultureInfo.InvariantCulture),
            x.GetProperty("daysOfWeek").EnumerateArray().Select(d => d.GetInt32()).ToList(),
            DateOnly.Parse(x.GetProperty("startDate").GetString()!, CultureInfo.InvariantCulture),
            x.GetProperty("endDate").ValueKind == JsonValueKind.Null ? null : DateOnly.Parse(x.GetProperty("endDate").GetString()!, CultureInfo.InvariantCulture),
            x.GetProperty("gracePeriodMinutes").GetInt32(),
            x.GetProperty("timeZoneId").GetString()!)).ToList();
        LocalSchedules = parsed;
        LocalScheduleVersion = body.GetProperty("version").GetInt32();
    }

    /// <summary>Sends the whole queue as one batch when online; drops events the server acknowledged. Null when offline.</summary>
    public async Task<List<JsonElement>?> FlushAsync(bool shuffle = false)
    {
        if (!Online || !Powered || _queue.Count == 0) return null;
        LastBatch = _queue.Take(DeviceEventRules.MaxBatchSize).ToList();
        var results = await SendBatchAsync(LastBatch, shuffle);
        var acknowledged = results.Select(x => x.GetProperty("eventId").GetString()).ToHashSet();
        _queue.RemoveAll(x => acknowledged.Contains(x.EventId));
        return results;
    }

    /// <summary>Lost-ACK simulation: resend the exact same events (same EventIds), optionally in a different order.</summary>
    public Task<List<JsonElement>> ResendLastBatchAsync(bool shuffle = false) => SendBatchAsync(LastBatch, shuffle);

    private async Task<List<JsonElement>> SendBatchAsync(List<QueuedEvent> events, bool shuffle)
    {
        EnsureConnected();
        var ordered = shuffle ? events.OrderBy(_ => Random.Shared.Next()).ToList() : events;
        using var response = await SendAsync(HttpMethod.Post, "/api/device/events/batch", new
        {
            batchId = Guid.NewGuid().ToString("N"),
            events = ordered.Select(x => new { eventId = x.EventId, compartmentSlot = x.CompartmentSlot, eventType = x.EventType, occurredAt = x.OccurredAt }),
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return body.GetProperty("results").EnumerateArray().ToList();
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null, string? apiKey = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Device-Id", DeviceId);
        request.Headers.Add("X-Device-Api-Key", apiKey ?? ApiKey);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await http.SendAsync(request);
    }

    private void EnsureConnected()
    {
        if (!Online || !Powered) throw new InvalidOperationException("Simulated device has no network/power.");
    }
}
