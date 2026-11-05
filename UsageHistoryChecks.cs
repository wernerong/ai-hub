using AIHub.Usage;
using System.Text.Json;

namespace AIHub;

internal static class UsageHistoryChecks
{
    internal static void Run(string folder, List<string> lines)
    {
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); lines.Add("PASS " + message); }
        Directory.CreateDirectory(folder);
        var now = DateTimeOffset.Parse("2030-01-01T12:00:00Z"); var reset = now.AddHours(1);
        UsageState State(DateTimeOffset fetched, double? used, DateTimeOffset? end = null, string id = "codex", string label = "5 hours") =>
            new(id, true, new("PRIVATE_PROVIDER_TEXT", [new(label, used, end ?? reset, "PRIVATE_NOTE", "PRIVATE_STATUS")], "PRIVATE_BALANCE", fetched));
        var path = Path.Combine(folder, "usage-history.json");
        var store = new UsageHistoryStore(path);
        Check(store.Enabled && store.Samples.Length == 0 && store.Warning is null, "Usage history starts empty with local recording enabled");
        var reading = State(now, 20);
        Check(store.Record([reading], now) && !store.Record([reading], now.AddSeconds(1)) && store.Samples.Length == 1,
            "Repeated monitor notifications record each fetched quota only once");
        var encoded = File.ReadAllText(path);
        Check(!encoded.Contains("PRIVATE_") && !encoded.Contains("Balance") && !encoded.Contains("Note"),
            "History persists numeric quotas and timestamps without provider response text or balances");
        var reloaded = new UsageHistoryStore(path);
        Check(!reloaded.Record([reading], now.AddSeconds(2)) && reloaded.Samples.SequenceEqual(store.Samples), "Sample deduplication survives an app restart");
        var rejectedPath = Path.Combine(folder, "rejected-history.json");
        var rejected = new UsageHistoryStore(rejectedPath);
        Check(!store.Record([
            State(now.AddMinutes(-4), 12), State(now.AddSeconds(1), 23), State(now, null), State(now, double.NaN),
            State(now, -1), State(now, 101), State(now, 20) with { Error = "Offline" },
            State(now, 20) with { Enabled = false }, State(now, 20) with { Refreshing = true }
        ], now), "Stale, future, unavailable, invalid, failed, disabled and in-flight readings do not enter history");
        Check(!rejected.Record([State(now, 20) with { Error = "Offline" }, State(now, 20) with { Enabled = false },
            State(now, 20) with { Refreshing = true }], now) && !File.Exists(rejectedPath),
            "Failed, disabled and pending states cannot create history even before any deduplication key exists");
        var ambiguous = new UsageState("grok", true, new("Grok", [new("Weekly", 10, reset), new("Weekly", 30, reset)], "", now));
        Check(!store.Record([ambiguous], now), "Duplicate quota labels in one response are not merged into a misleading series");
        Check(store.Record([State(now.AddMinutes(1), 21), State(now.AddMinutes(1), 80, id: "grok", label: "Weekly")], now.AddMinutes(1))
            && store.Samples.Select(s => (s.ProviderId, s.QuotaLabel)).Distinct().Count() == 2, "Different products and quota windows retain separate history series");
        store.SetEnabled(false); reloaded = new UsageHistoryStore(path);
        Check(!reloaded.Enabled && !reloaded.Record([State(now.AddMinutes(2), 22)], now.AddMinutes(2)) && reloaded.Samples.Length == 3,
            "Disabling local recording persists without erasing existing history or collecting new samples");
        store.SetEnabled(true); store.Clear(now.AddMinutes(2));
        Check(store.Samples.Length == 0 && !store.Record([State(now.AddMinutes(1), 22)], now.AddMinutes(2)),
            "Clearing history cannot immediately repopulate it from an old current reading");
        Check(new UsageHistoryStore(path).Record([State(now.AddMinutes(3), 23)], now.AddMinutes(3)),
            "A fresh provider reading starts history again after an explicit clear");
        reloaded = new UsageHistoryStore(path);
        var beforeFailure = File.ReadAllText(path); var originalSamples = reloaded.Samples;
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Check(!reloaded.Record([State(now.AddMinutes(4), 24)], now.AddMinutes(4)) && reloaded.Warning is not null
                && reloaded.Samples.SequenceEqual(originalSamples) && File.ReadAllText(path) == beforeFailure,
                "An atomic replacement failure preserves previous history on disk and in memory");
        }
        Check(reloaded.Record([State(now.AddMinutes(6), 26)], now.AddMinutes(6)) && reloaded.Warning is null
            && Directory.GetFiles(folder, "usage-history.json.*.tmp").Length == 0,
            "History recording recovers after an I/O failure without leaving temporary files");

        var old = new UsageHistorySample("codex", "5 hours", now.AddDays(-31), 12, now.AddDays(-30));
        var recent = old with { Fetched = now.AddDays(-1), Reset = reset };
        File.WriteAllText(path, JsonSerializer.Serialize(new { Version = 1, Enabled = true, Samples = new[] { old, recent } }));
        reloaded = new UsageHistoryStore(path); reloaded.Prune(now);
        Check(reloaded.Samples.SequenceEqual(new[] { recent }) && new UsageHistoryStore(path).Samples.Length == 1,
            "History retention removes samples older than 30 days from memory and disk");
        var many = Enumerable.Range(0, UsageHistoryStore.MaxSamples).Select(i => recent with { Fetched = now.AddMilliseconds(-UsageHistoryStore.MaxSamples + i) }).ToArray();
        File.WriteAllText(path, JsonSerializer.Serialize(new { Version = 1, Enabled = true, Samples = many }));
        reloaded = new UsageHistoryStore(path); reloaded.Record([State(now, 13)], now);
        Check(reloaded.Samples.Length == UsageHistoryStore.MaxSamples && reloaded.Samples[0].Fetched == many[1].Fetched
            && reloaded.Samples[^1].Fetched == now, "The sample budget retains the newest observations and evicts the oldest");
        foreach (string malformed in new[] { "{broken", "{}", "{\"Version\":9,\"Samples\":[]}",
            "{\"Version\":\"1\",\"Samples\":[]}", "{\"Version\":1,\"Samples\":[null]}" })
        {
            File.WriteAllText(path, malformed); reloaded = new UsageHistoryStore(path);
            bool blocked = false; try { reloaded.Clear(now); } catch (InvalidOperationException) { blocked = true; }
            Check(reloaded.Warning is not null && !reloaded.Record([reading], now) && blocked && File.ReadAllText(path) == malformed,
                "Malformed or unsupported usage history remains preserved until explicitly repaired");
        }

        UsageHistorySample[] Ramp(DateTimeOffset? end) => [new("codex", "5 hours", now.AddMinutes(-10), 20, end),
            new("codex", "5 hours", now.AddMinutes(-5), 25, end), new("codex", "5 hours", now, 30, end)];
        var forecast = UsageHistoryAnalysis.Estimate(Ramp(reset), "codex", "5 hours", State(now, 30), now);
        Check(forecast.Available && Math.Abs(forecast.PercentPerHour!.Value - 60) < .001 && forecast.ExhaustedAt == now.AddMinutes(70)
            && forecast.LastsUntilReset == true, "Comparable fresh readings produce a labelled rate and allowance-through-reset estimate");
        var laterReset = now.AddHours(3);
        forecast = UsageHistoryAnalysis.Estimate(Ramp(laterReset), "codex", "5 hours", State(now, 30, laterReset), now);
        Check(forecast.Available && forecast.LastsUntilReset == false, "Estimated exhaustion before the reported reset is distinguished from allowance lasting until reset");
        Check(!UsageHistoryAnalysis.Estimate(Ramp(reset).Take(2), "codex", "5 hours", State(now.AddMinutes(-5), 25), now.AddMinutes(-5)).Available,
            "Forecasting waits for at least three comparable observations");
        var burst = Ramp(reset); burst[0] = burst[0] with { Fetched = now.AddMinutes(-2) }; burst[1] = burst[1] with { Fetched = now.AddMinutes(-1) };
        Check(!UsageHistoryAnalysis.Estimate(burst, "codex", "5 hours", State(now, 30), now).Available,
            "Rapid manual refreshes cannot produce an estimate before five minutes of observations");
        var changed = Ramp(reset); changed[0] = changed[0] with { Reset = reset.AddDays(-1) };
        Check(!UsageHistoryAnalysis.Estimate(changed, "codex", "5 hours", State(now, 30), now).Available,
            "A changed reset identity cannot share consumption history with the current quota window");
        var drop = Ramp(reset); drop[1] = drop[1] with { Used = 15 };
        Check(!UsageHistoryAnalysis.Estimate(drop, "codex", "5 hours", State(now, 30), now).Available,
            "A quota decrease starts a new estimate segment instead of implying negative consumption");
        var gap = Ramp(reset); gap[0] = gap[0] with { Fetched = now.AddMinutes(-30) };
        Check(!UsageHistoryAnalysis.Estimate(gap, "codex", "5 hours", State(now, 30), now).Available,
            "Long observation gaps prevent forecasts across missing activity");
        Check(!UsageHistoryAnalysis.Estimate(Ramp(reset), "codex", "5 hours", State(now, 30) with { Error = "Offline" }, now).Available
            && !UsageHistoryAnalysis.Estimate(Ramp(reset), "codex", "5 hours", State(now, 30), now.AddMinutes(4)).Available,
            "Failed or stale current readings cannot produce an active forecast");
        Check(!UsageHistoryAnalysis.Estimate(Ramp(null), "codex", "5 hours", new("codex", true, new("Codex", [new("5 hours", 30, null)], "", now)), now).Available
            && !UsageHistoryAnalysis.Estimate(Ramp(now.AddMinutes(-1)), "codex", "5 hours", State(now, 30, now.AddMinutes(-1)), now).Available,
            "Unknown or expired resets remain unforecastable rather than inventing a fresh allowance");
        Check(!UsageHistoryAnalysis.Estimate(Ramp(reset).Select(s => s with { Used = 30 }), "codex", "5 hours", State(now, 30), now).Available,
            "Flat usage is reported as no measurable consumption rather than infinite allowance");
        Check(!UsageHistoryAnalysis.Estimate(Ramp(reset), "grok", "Weekly", State(now, 30, id: "grok", label: "Weekly"), now).Available,
            "Forecasts cannot borrow observations from another product or quota scope");
        Check(!UsageHistoryAnalysis.Estimate(Ramp(reset), "codex", "5 hours", State(now.AddSeconds(1), 31), now).Available
            && !UsageHistoryAnalysis.Estimate(Ramp(reset), "codex", "5 hours", State(now, 31), now).Available,
            "Future-dated or unrecorded current readings cannot be substituted into an estimate");
    }
}
