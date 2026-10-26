using System.Text.Json;
using AIHub.Usage;

namespace AIHub;

internal static class UsageChecks
{
    internal static async Task Run(string folder, List<string> lines)
    {
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            lines.Add("PASS " + message);
        }
        JsonElement JsonOf(string json) { using var doc = JsonDocument.Parse(json); return doc.RootElement.Clone(); }
        var now = DateTimeOffset.Parse("2030-01-01T00:00:00Z");
        var codex = CodexProvider.Parse(JsonOf("""{"rateLimits":{"primary":{"usedPercent":99,"windowDurationMins":300}},"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":91,"windowDurationMins":10080,"resetsAt":1900000000},"secondary":{"usedPercent":42,"windowDurationMins":300}}}}"""));
        Check(codex.Quotas[0].Used == 42 && codex.Quotas[1].Used == 91, "Codex prefers its account bucket and maps quota windows by duration");
        Check(UsageDisplay.Primary(codex)?.Label == "Weekly", "Strip chooses the most constrained Codex window rather than hiding a weekly limit");
        Check(UsageDisplay.Remaining(UsageDisplay.Primary(codex)) == "9% left", "Used percentages convert to remaining exactly once");
        var unknown = CodexProvider.Parse(JsonOf("""{"rateLimits":{"primary":{"windowDurationMins":300}}}"""));
        Check(unknown.Quotas.All(q => q.Used is null) && UsageDisplay.Remaining(UsageDisplay.Primary(unknown)) == "Unavailable", "Missing allowance is unavailable, never a full balance");
        var resets = CodexProvider.Parse(JsonOf("""{"rateLimits":{},"rateLimitResetCredits":{"availableCount":0}}"""));
        Check(resets.FreeResets == 0 && unknown.FreeResets is null, "Zero free resets remain distinct from missing reset data");
        var grok = GrokProvider.Parse(JsonOf("""{"config":{"currentPeriod":{"type":"USAGE_PERIOD_TYPE_WEEKLY","end":"2030-01-07T00:00:00Z"},"creditUsagePercent":1,"prepaidBalance":{"val":2000}}}"""));
        Check(grok.Quotas[0].Used == 1 && grok.Balance.EndsWith("US$20.00"), "Grok percentage and cents retain their reported units");
        var monthly = GrokProvider.Parse(JsonOf("""{"config":{"monthlyLimit":{"val":2000},"used":{"val":0}}}"""));
        Check(monthly.Quotas[0].Used is null && monthly.Quotas[0].Label != "Weekly", "Monthly Grok billing is never relabeled as weekly allowance");
        var bot = GrokBotProvider.Parse(JsonOf("""{"usagePercent":61.25,"nextResetTimestampUtc":"2030-01-07T00:00:00Z"}"""), JsonOf("""{"spendLimitUsage":{"individualUsed":1250,"individualLimit":4000}}"""));
        Check(bot.Quotas[0].Used == 61.25 && bot.Balance.Contains("12.50 / $40.00"), "Grok Bot retains quota precision and converts spending cents");
        bool rejected = false;
        try { GrokBotProvider.Parse(JsonOf("""{"usagePercent":10,"usesPooledEnterpriseAllowance":true}"""), default); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Pooled Grok Bot allowances do not invent an individual percentage");
        rejected = false;
        try { GrokBotProvider.Parse(JsonOf("{}"), default); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Missing Grok Bot usage is rejected");
        Check(UsageDisplay.ResetText(now.AddSeconds(-1), now).Contains("awaiting") && UsageDisplay.ResetText(now.AddDays(2).AddHours(3), now) == "Resets in 2d 3h", "Reset countdown does not manufacture refreshed allowance");

        int reads = 0;
        bool fail = false;
        var sample = new Reading("Test", [new("Weekly", 50, now.AddDays(1))], "", now);
        var provider = new UsageProvider("test", "Test", "openai", "Test only", "", _ =>
        {
            reads++;
            return fail ? Task.FromException<Reading>(new HttpRequestException("synthetic secret must not surface")) : Task.FromResult(sample);
        });
        using var monitor = new UsageMonitor([provider], clock: () => now);
        await monitor.RefreshAsync();
        await monitor.RefreshAsync(true);
        Check(reads == 1, "Rapid refresh clicks respect the minimum polling interval");
        now = now.AddMinutes(1); fail = true;
        await monitor.RefreshAsync();
        var stale = monitor.States.Single();
        Check(stale.Reading == sample && stale.IsStale(now) && stale.Error == "Cannot connect. Check your network.", "A failed refresh preserves prior data and shows a safe stale error");
        monitor.SetEnabled("test", false);
        await monitor.RefreshAsync();
        Check(reads == 2 && monitor.States.Single().Reading is null, "Disabling a provider stops reads and clears its displayed quota");
        var waiting = new TaskCompletionSource<Reading>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        using var late = new UsageMonitor([provider with { Read = ct => { token = ct; started.SetResult(); return waiting.Task; } }]);
        var running = late.RefreshAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        late.SetEnabled("test", false);
        waiting.SetResult(sample); await running;
        Check(token.IsCancellationRequested && late.States.Single().Reading is null, "Disabling cancels in-flight collection and rejects late responses");
        int fastReads = 0;
        using var independent = new UsageMonitor([
            provider with { Id = "bad", Read = _ => Task.FromException<Reading>(new InvalidOperationException("No login")) },
            provider with { Id = "good", Read = _ => { fastReads++; return Task.FromResult(sample); } }
        ]);
        await independent.RefreshAsync();
        Check(fastReads == 1 && independent.States.Single(s => s.Id == "good").Reading is not null && independent.States.Single(s => s.Id == "bad").Error is not null, "An unavailable provider does not block other providers");

        now = DateTimeOffset.Parse("2030-01-01T00:00:00Z");
        var tracker = new UsageAlerts();
        UsageState State(double used, DateTimeOffset reset) => new("test", true, new("Test", [new("Weekly", used, reset)], "", now));
        var high = State(95, now.AddHours(1));
        Check(tracker.Evaluate(provider, high, now, false, false, 90).Count == 0, "Usage alerts are silent while disabled");
        Check(tracker.Evaluate(provider, high, now, true, true, 90).Count == 1 && tracker.Evaluate(provider, high, now, true, true, 90).Count == 0, "Low allowance emits one alert per quota window and threshold");
        var persisted = new UsageAlerts(tracker.Sent);
        Check(persisted.Evaluate(provider, high, now, true, true, 90).Count == 0, "Alert deduplication survives a restart");
        now = now.AddHours(2);
        Check(tracker.Evaluate(provider, high, now, true, true, 90).Count == 0, "An expired or stale reading never produces a reset alert");
        var reset = State(3, now.AddDays(7));
        Check(tracker.Evaluate(provider, reset, now, true, true, 90).Single().Title.Contains("reset"), "A newer reported window plus reduced usage confirms a reset");
        Check(tracker.Evaluate(provider, reset, now, true, true, 90).Count == 0, "Confirmed reset alerts do not repeat");
        Check(tracker.Evaluate(provider, high with { Error = "Offline" }, now, true, true, 90).Count == 0, "Stale provider data cannot trigger low or reset notifications");

        var path = Path.Combine(folder, "usage-settings-check.json");
        var prefs = new UsagePreferences { LowAlerts = true, ResetAlerts = true, UsedThreshold = 85, SentAlerts = tracker.Sent };
        prefs.Providers["grok"] = false; prefs.Save(path);
        var loaded = UsagePreferences.Load(path);
        Check(!loaded.IsEnabled("grok") && loaded.IsEnabled("codex") && loaded.UsedThreshold == 85 && loaded.LowAlerts && loaded.ResetAlerts && loaded.SentAlerts.SetEquals(tracker.Sent), "Provider choices, alert settings and deduplication persist together");
        File.WriteAllText(path, "{broken");
        loaded = UsagePreferences.Load(path);
        Check(!loaded.LowAlerts && !loaded.ResetAlerts && loaded.UsedThreshold == 90, "Invalid preference files fall back to silent alert defaults");
        File.Delete(path);
    }
}
