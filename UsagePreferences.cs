using System.Text.Json;

namespace AIHub;

internal sealed class UsagePreferences
{
    public Dictionary<string, bool> Providers { get; set; } = new();
    public bool LowAlerts { get; set; }
    public bool ResetAlerts { get; set; }
    public int UsedThreshold { get; set; } = 90;
    public HashSet<string> SentAlerts { get; set; } = new();
    public bool IsEnabled(string id) => Providers.GetValueOrDefault(id, true);

    internal static UsagePreferences Load(string path)
    {
        try
        {
            var value = JsonSerializer.Deserialize<UsagePreferences>(File.ReadAllText(path)) ?? new();
            value.Providers ??= new(); value.SentAlerts ??= new();
            if (value.UsedThreshold is < 50 or > 99) value.UsedThreshold = 90;
            return value;
        }
        catch { return new(); }
    }

    internal void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
}
