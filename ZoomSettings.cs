using System.Text.Json;

namespace AIHub;

internal static class ZoomLevels
{
    private static readonly int[] Browser = [50, 67, 75, 80, 90, 100, 110, 125, 150, 175, 200, 250, 300];
    private static readonly int[] Desktop = Enumerable.Range(5, 26).Select(n => n * 10).ToArray();
    internal static IReadOnlyList<int> For(string key) => key switch
    {
        "openai" or "grokbot" => Desktop,
        "grok" => Browser,
        _ => throw new ArgumentException("Unknown AI panel.", nameof(key))
    };
    internal static char[] Commands(string key, int percent)
    {
        var levels = For(key).ToArray();
        int target = Array.IndexOf(levels, percent), normal = Array.IndexOf(levels, 100);
        if (target < 0) throw new ArgumentOutOfRangeException(nameof(percent));
        return ['0', .. Enumerable.Repeat(target > normal ? '+' : '-', Math.Abs(target - normal))];
    }
}

internal sealed class ZoomSettings
{
    private readonly string path;
    private Dictionary<string, int> values;
    internal string? LoadWarning { get; }
    internal ZoomSettings(string path)
    {
        this.path = path;
        values = [];
        try
        {
            if (File.Exists(path))
            {
                var saved = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path)) ?? [];
                foreach (var app in AppEntry.All)
                    if (saved.TryGetValue(app.Key, out int percent) && ZoomLevels.For(app.Key).Contains(percent)) values[app.Key] = percent;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { LoadWarning = "Saved zoom settings could not be read. Your apps keep their current zoom until you choose a level."; }
    }
    internal int? Get(string key) => values.TryGetValue(key, out int value) ? value : null;
    internal void Set(string key, int? percent)
    {
        if (percent is int value && !ZoomLevels.For(key).Contains(value)) throw new ArgumentOutOfRangeException(nameof(percent));
        var next = new Dictionary<string, int>(values);
        if (percent is int chosen) next[key] = chosen; else next.Remove(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(next, new JsonSerializerOptions { WriteIndented = true }));
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (File.Exists(path)) File.Replace(path + ".tmp", path, null);
                else File.Move(path + ".tmp", path);
                break;
            }
            catch (Exception ex) when (attempt < 3 && (ex is IOException or UnauthorizedAccessException))
            { Thread.Sleep(50 * (attempt + 1)); }
        }
        values = next;
    }
}

internal static class ZoomController
{
    // Recheck identity and focus between every complete key chord. Never send
    // zoom shortcuts to an unrelated app if the user switches away mid-apply.
    internal static async Task Apply(string key, int percent, Func<bool> targetReady, Action<char> sendChord)
    {
        foreach (char command in ZoomLevels.Commands(key, percent))
        {
            if (!targetReady()) throw new InvalidOperationException("The app lost focus. Select its panel again to retry zoom.");
            sendChord(command);
            await Task.Delay(70);
        }
    }
}
