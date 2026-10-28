using System.Text.Json;

namespace AIHub.Usage;

public sealed record UsageProvider(string Id, string Name, string AppKey, string Scope, string ConnectionHelp,
    Func<CancellationToken, Task<Reading>> Read);

public sealed record UsageState(string Id, bool Enabled, Reading? Reading = null, string? Error = null,
    DateTimeOffset? Attempted = null, bool Refreshing = false)
{
    public bool IsStale(DateTimeOffset now) => Reading is not null && (Error is not null || now - Reading.Fetched > TimeSpan.FromMinutes(3));
}

public sealed class UsageReaders : IDisposable
{
    private readonly GrokProvider grok = new();
    public UsageProvider[] Providers { get; }
    public UsageReaders() => Providers = [
        new("codex", "Codex", "openai", "Codex allowance · separate from ChatGPT and Dot",
            "Uses your Codex CLI login. Open Codex and sign in on this Windows account.", CodexProvider.Read),
        new("grok", "Grok CLI", "grok", "CLI / Build allowance · not Grok web chat",
            "Uses your Grok CLI / Build login. Sign in with grok login; the Grok web login is separate.", grok.Read),
        new("grokbot", "Grok Bot", "grokbot", "Grok Bot desktop allowance",
            "Uses the active account in Grok Bot. Open Grok Bot and sign in, then refresh.", GrokBotProvider.Read)
    ];
    public void Dispose() => grok.Dispose();
    public static bool IsAvailable(string id)
    {
        // Presence only: discovery never reads credentials or makes network requests.
        try
        {
            return id switch
            {
                "codex" => File.Exists(CodexProvider.FindBinary()),
                "grok" => File.Exists(Path.Combine(Environment.GetEnvironmentVariable("GROK_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grok"), "auth.json")),
                "grokbot" => File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Grok Bot", "sand-secrets.json")),
                _ => false
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException) { return false; }
    }
}

// The collector is independent of any UI framework. Events can arrive on worker threads.
public sealed class UsageMonitor : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, UsageState> states;
    private readonly Dictionary<string, CancellationTokenSource> requests = new();
    private readonly CancellationTokenSource stop = new();
    private readonly Func<DateTimeOffset> clock;
    private Task? refresh;
    private bool disposed;
    public UsageProvider[] Providers { get; }
    public event Action? Changed;
    public bool Busy { get { lock (gate) return refresh is { IsCompleted: false }; } }
    public UsageState[] States { get { lock (gate) return Providers.Select(p => states[p.Id]).ToArray(); } }

    public UsageMonitor(UsageProvider[] providers, Func<string, bool>? enabled = null, Func<DateTimeOffset>? clock = null)
    {
        Providers = providers;
        this.clock = clock ?? (() => DateTimeOffset.Now);
        states = providers.ToDictionary(p => p.Id, p => new UsageState(p.Id, enabled?.Invoke(p.Id) ?? true));
    }

    public void SetEnabled(string id, bool enabled)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (states[id].Enabled == enabled) return;
            if (requests.TryGetValue(id, out var request)) request.Cancel();
            // Replacing the immutable state also prevents a late result resurrecting it.
            states[id] = new(id, enabled);
        }
        Changed?.Invoke();
    }

    public Task RefreshAsync(bool manual = false)
    {
        lock (gate)
        {
            if (disposed) return Task.CompletedTask;
            if (refresh is { IsCompleted: false }) return refresh;
            var interval = TimeSpan.FromSeconds(manual ? 15 : 60);
            var due = Providers.Where(p => states[p.Id].Enabled &&
                (states[p.Id].Attempted is not { } attempted || clock() - attempted >= interval)).ToArray();
            if (due.Length == 0) return Task.CompletedTask;
            refresh = Task.Run(async () =>
            {
                await Task.WhenAll(due.Select(Update));
                if (!stop.IsCancellationRequested) Changed?.Invoke();
            });
            return refresh;
        }
    }

    private async Task Update(UsageProvider provider)
    {
        UsageState pending;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        request.CancelAfter(TimeSpan.FromSeconds(45));
        lock (gate)
        {
            if (disposed || !states[provider.Id].Enabled) return;
            states[provider.Id] = pending = states[provider.Id] with { Refreshing = true, Attempted = clock() };
            requests[provider.Id] = request;
        }
        Changed?.Invoke();
        Reading? reading = null;
        string? error = null;
        try { reading = await provider.Read(request.Token).ConfigureAwait(false); }
        catch (Exception ex) { error = SafeError(ex); }
        lock (gate)
        {
            requests.Remove(provider.Id);
            if (disposed || !ReferenceEquals(states[provider.Id], pending)) return;
            states[provider.Id] = pending with { Reading = reading ?? pending.Reading, Error = error, Refreshing = false };
        }
        Changed?.Invoke();
    }

    public static string SafeError(Exception ex) => ex switch
    {
        OperationCanceledException => "Refresh timed out. Retrying automatically.",
        HttpRequestException => "Cannot connect. Check your network.",
        JsonException => "The usage response or login format changed. Update the provider app and retry.",
        IOException or UnauthorizedAccessException => "Cannot read the provider login. Sign in in its app, then retry.",
        InvalidOperationException => ex.Message,
        _ => "Usage unavailable. Check the provider login."
    };

    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; stop.Cancel(); }
    }
}

public static class UsageDisplay
{
    public static Quota? Primary(Reading? reading) => reading?.Quotas
        .OrderByDescending(q => q.Used ?? -1).FirstOrDefault();
    public static string Remaining(Quota? quota) => quota?.Used is { } used ? $"{Math.Clamp(100 - used, 0, 100):0}% left" : "Unavailable";
    public static string WindowLabel(Quota? quota) => quota?.Label switch { "5 hours" => "5h", "Weekly" => "week", null => "", _ => quota.Label };
    public static string ResetText(DateTimeOffset? reset, DateTimeOffset now)
    {
        if (reset is null) return "Reset time unavailable";
        var remaining = reset.Value - now;
        if (remaining <= TimeSpan.Zero) return "Reset due · awaiting update";
        return remaining.TotalDays >= 1 ? $"Resets in {(int)remaining.TotalDays}d {remaining.Hours}h"
            : remaining.TotalHours >= 1 ? $"Resets in {(int)remaining.TotalHours}h {remaining.Minutes}m"
            : $"Resets in {Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))}m";
    }
}
