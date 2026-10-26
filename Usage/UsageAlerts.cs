namespace AIHub.Usage;

public sealed record UsageAlert(string Title, string Message);

public sealed class UsageAlerts
{
    private readonly Dictionary<string, Quota> previous = new();
    public HashSet<string> Sent { get; }
    public UsageAlerts(IEnumerable<string>? sent = null) => Sent = new(sent ?? []);

    public IReadOnlyList<UsageAlert> Evaluate(UsageProvider provider, UsageState state, DateTimeOffset now,
        bool lowEnabled, bool resetsEnabled, int usedThreshold)
    {
        var alerts = new List<UsageAlert>();
        if (!state.Enabled || state.Refreshing || state.Reading is null || state.IsStale(now)) return alerts;
        foreach (var quota in state.Reading.Quotas)
        {
            if (quota.Used is not { } used) continue;
            string key = provider.Id + "/" + quota.Label;
            previous.TryGetValue(key, out var old);
            // A passed clock deadline alone never proves the quota reset.
            bool resetConfirmed = old?.Reset is { } priorReset && priorReset <= now &&
                quota.Reset is { } nextReset && nextReset > priorReset && used < old.Used;
            string period = quota.Reset?.ToUnixTimeSeconds().ToString() ?? "unknown";
            if (resetConfirmed && resetsEnabled && Sent.Add(key + "/reset/" + period))
                alerts.Add(new(provider.Name + " allowance reset", $"{quota.Label}: {UsageDisplay.Remaining(quota)}. A fresh reading confirms the reset."));
            // Do not announce a cached, expired window as a current low allowance.
            if (lowEnabled && used >= usedThreshold && (quota.Reset is null || quota.Reset > now) &&
                Sent.Add(key + "/low/" + period + "/" + usedThreshold))
                alerts.Add(new(provider.Name + " allowance is low", $"{quota.Label}: {UsageDisplay.Remaining(quota)}. {UsageDisplay.ResetText(quota.Reset, now)}."));
            previous[key] = quota;
        }
        return alerts;
    }
}
