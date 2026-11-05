using AIHub.Usage;
using System.Text.Json;

namespace AIHub;

internal sealed record UsageHistorySample(string ProviderId, string QuotaLabel, DateTimeOffset Fetched, double Used, DateTimeOffset? Reset);
internal sealed record UsageEstimate(bool Available, string Reason, double? PercentPerHour = null, DateTimeOffset? ExhaustedAt = null, bool? LastsUntilReset = null);

internal sealed class UsageHistoryStore
{
    internal const int RetentionDays = 30, MaxSamples = 50000;
    private readonly object gate = new();
    private readonly string path;
    private HistoryData data = new();
    private bool readOnly;
    private long revision;
    private DateTimeOffset retryAfter;
    internal string? Warning { get; private set; }
    internal bool Enabled { get { lock (gate) return data.Enabled; } }
    internal long Revision { get { lock (gate) return revision; } }
    internal UsageHistorySample[] Samples { get { lock (gate) return [.. data.Samples]; } }

    internal UsageHistoryStore(string path)
    {
        this.path = Path.GetFullPath(path);
        try
        {
            if (!File.Exists(this.path)) return;
            if (new FileInfo(this.path).Length > 20 * 1024 * 1024) throw new InvalidDataException("History file too large.");
            using var document = JsonDocument.Parse(File.ReadAllText(this.path));
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("Version", out var version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out int number) || number != 1
                || !document.RootElement.TryGetProperty("Samples", out var stored) || stored.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("History schema is invalid.");
            var loaded = document.RootElement.Deserialize<HistoryData>() ?? throw new InvalidDataException("History is empty.");
            if (loaded.Version != 1 || loaded.Samples is null || loaded.Samples.Length > MaxSamples || loaded.Samples.Any(s => !Valid(s)))
                throw new InvalidDataException("History data is invalid.");
            data = loaded with { Samples = loaded.Samples.DistinctBy(s => (s.ProviderId, s.QuotaLabel, s.Fetched)).OrderBy(s => s.Fetched).ToArray() };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            readOnly = true;
            Warning = "Saved usage history could not be read. The original file is preserved and recording is paused. Restore or rename " + Path.GetFileName(this.path) + ", then restart AI Hub.";
        }
    }

    // Uses successful readings already collected by the monitor; never requests
    // provider data or stores balances, account details, tokens or response text.
    internal bool Record(IEnumerable<UsageState> states, DateTimeOffset now)
    {
        lock (gate)
        {
            if (readOnly || now < retryAfter) return false;
            var next = Retained(data.Samples, now).ToList();
            var seen = next.Select(s => (s.ProviderId, s.QuotaLabel, s.Fetched)).ToHashSet();
            if (data.Enabled)
                foreach (var state in states)
                {
                    if (!state.Enabled || state.Refreshing || state.Error is not null || state.Reading is not { } reading
                        || state.IsStale(now) || reading.Fetched > now || reading.Fetched <= data.ClearedAt) continue;
                    // A repeated label is ambiguous: do not merge distinct limits.
                    foreach (var quota in reading.Quotas.GroupBy(q => q.Label).Where(g => g.Count() == 1).Select(g => g.Single()))
                    {
                        if (quota.Used is not { } used) continue;
                        var sample = new UsageHistorySample(state.Id, quota.Label, reading.Fetched, used, quota.Reset);
                        if (Valid(sample) && seen.Add((sample.ProviderId, sample.QuotaLabel, sample.Fetched))) next.Add(sample);
                    }
                }
            var retained = Retained(next, now);
            if (retained.SequenceEqual(data.Samples)) return false;
            try { Save(data with { Samples = retained }); Warning = null; return true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                retryAfter = now.AddMinutes(1);
                Warning = "Usage history could not be saved. Recording will retry; current usage readings are still available.";
                return false;
            }
        }
    }

    internal void SetEnabled(bool enabled)
    {
        lock (gate) { EnsureWritable(); if (data.Enabled != enabled) Save(data with { Enabled = enabled }); }
    }
    internal void Clear(DateTimeOffset now)
    {
        lock (gate) { EnsureWritable(); Save(data with { Samples = [], ClearedAt = now }); Warning = null; }
    }
    internal void Prune(DateTimeOffset now)
    {
        lock (gate)
        {
            EnsureWritable(); var next = Retained(data.Samples, now);
            if (!next.SequenceEqual(data.Samples)) Save(data with { Samples = next });
        }
    }
    private static UsageHistorySample[] Retained(IEnumerable<UsageHistorySample> samples, DateTimeOffset now) => samples
        .Where(s => s.Fetched >= now.AddDays(-RetentionDays) && s.Fetched <= now).OrderBy(s => s.Fetched).TakeLast(MaxSamples).ToArray();
    private static bool Valid(UsageHistorySample? sample) => sample is not null
        && !string.IsNullOrWhiteSpace(sample.ProviderId) && sample.ProviderId.Length <= 80
        && sample.ProviderId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
        && !string.IsNullOrWhiteSpace(sample.QuotaLabel) && sample.QuotaLabel.Length <= 120 && !sample.QuotaLabel.Any(char.IsControl)
        && sample.Fetched > DateTimeOffset.MinValue && double.IsFinite(sample.Used) && sample.Used is >= 0 and <= 100;
    private void EnsureWritable() { if (readOnly) throw new InvalidOperationException(Warning); }
    private void Save(HistoryData next)
    {
        EnsureWritable(); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, next); stream.Flush(true); }
            LocalFile.Replace(temporary, path); data = next; revision++;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private sealed record HistoryData
    {
        public HistoryData() { }
        public int Version { get; init; } = 1;
        public bool Enabled { get; init; } = true;
        public DateTimeOffset ClearedAt { get; init; } = DateTimeOffset.MinValue;
        public UsageHistorySample[] Samples { get; init; } = [];
    }
}

internal static class UsageHistoryAnalysis
{
    internal static readonly TimeSpan MaximumGap = TimeSpan.FromMinutes(10);
    internal static UsageEstimate Estimate(IEnumerable<UsageHistorySample> samples, string providerId, string quotaLabel,
        UsageState? current, DateTimeOffset now)
    {
        UsageEstimate Missing(string reason) => new(false, reason);
        if (current is null || current.Id != providerId || !current.Enabled || current.Error is not null || current.IsStale(now)
            || current.Reading is not { } reading || reading.Fetched > now)
            return Missing("Estimate unavailable: a fresh reading is required.");
        var quotas = reading.Quotas.Where(q => q.Label == quotaLabel).ToArray();
        if (quotas.Length != 1 || quotas[0].Used is not { } currentUsed || !double.IsFinite(currentUsed) || currentUsed is < 0 or > 100)
            return Missing("Estimate unavailable: this quota is not reported uniquely.");
        if (quotas[0].Reset is not { } reset || reset <= now)
            return Missing("Estimate unavailable: a future reset time is required.");
        var points = samples.Where(s => s.ProviderId == providerId && s.QuotaLabel == quotaLabel
            && s.Fetched <= reading.Fetched && s.Fetched >= now.AddHours(-6))
            .OrderBy(s => s.Fetched).ToArray();
        if (points.Length == 0 || points[^1].Fetched != reading.Fetched || Math.Abs(points[^1].Used - currentUsed) > .001)
            return Missing("Insufficient history: waiting for a recorded current reading.");
        // Only use the contiguous tail. A reset change, decrease or long gap
        // invalidates preceding points even when later values happen to match.
        int first = points.Length - 1;
        if (points[first].Reset != reset) return Missing("Insufficient history for the current reset window.");
        while (first > 0)
        {
            var left = points[first - 1]; var right = points[first];
            if (left.Reset != reset || right.Reset != reset || right.Fetched - left.Fetched > MaximumGap
                || right.Fetched <= left.Fetched || right.Used + .001 < left.Used) break;
            first--;
        }
        var start = points[first]; var latest = points[^1];
        if (points.Length - first < 3 || latest.Fetched - start.Fetched < TimeSpan.FromMinutes(5))
            return Missing("Insufficient history: need 3 comparable readings over at least 5 minutes.");
        double rate = (latest.Used - start.Used) / (latest.Fetched - start.Fetched).TotalHours;
        if (rate <= .001) return Missing("Estimate unavailable: no measurable consumption in this window.");
        double hours = Math.Max(0, 100 - latest.Used) / rate;
        if (!double.IsFinite(hours) || hours > (DateTimeOffset.MaxValue - latest.Fetched).TotalHours)
            return Missing("Estimate unavailable: the observed rate is too small.");
        var exhausted = latest.Fetched.AddHours(hours);
        return new(true, "Estimate uses up to 6 hours of comparable readings; future usage may differ.", rate, exhausted, exhausted >= reset);
    }
}
