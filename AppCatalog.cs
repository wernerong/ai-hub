using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace AIHub;

internal sealed record AppEntry(string Key, string Name, string Description, string Badge, Color Accent)
{
    internal string[] Processes { get; init; } = [];
    internal string[] InstallNames { get; init; } = [];
    internal string[] WebNames { get; init; } = [];
    internal string? LaunchTarget { get; set; }
    internal string? Executable { get; init; }
    internal string? WindowTitle { get; init; }
    internal bool Custom => Key.StartsWith("custom-", StringComparison.Ordinal);
    internal static readonly AppEntry[] All = [
        new("openai", "OpenAI", "ChatGPT · Codex · Dot", "O", Color.FromArgb(106, 229, 205)) { Processes = ["ChatGPT", "Codex"], InstallNames = ["ChatGPT", "Codex", "OpenAI"], WebNames = ["ChatGPT"] },
        new("grok", "Grok", "Chat · Search · Imagine", "G", Color.FromArgb(205, 236, 255)) { Processes = ["Grok"], InstallNames = ["Grok"], WebNames = ["Grok"] },
        new("grokbot", "Grok Bot", "Your Cursor teammates", "B", Color.FromArgb(184, 167, 255)) { Processes = ["Grok Bot"], InstallNames = ["Grok Bot"] },
        new("claude", "Claude", "Anthropic assistant", "C", Color.FromArgb(229, 180, 154)) { Processes = ["Claude"], InstallNames = ["Claude"], WebNames = ["Claude"] },
        new("gemini", "Gemini", "Google assistant", "G", Color.FromArgb(157, 184, 255)) { Processes = ["Gemini"], InstallNames = ["Gemini"], WebNames = ["Gemini", "Google Gemini"] },
        new("perplexity", "Perplexity", "Answers and research", "P", Color.FromArgb(116, 219, 226)) { Processes = ["Perplexity"], InstallNames = ["Perplexity"], WebNames = ["Perplexity"] },
        new("deepseek", "DeepSeek", "Chat and reasoning", "D", Color.FromArgb(125, 167, 255)) { Processes = ["DeepSeek"], InstallNames = ["DeepSeek"], WebNames = ["DeepSeek"] },
        new("lmstudio", "LM Studio", "Your local models", "L", Color.FromArgb(197, 169, 255)) { Processes = ["LM Studio"], InstallNames = ["LM Studio"] },
        new("msty", "Msty", "Local and online models", "M", Color.FromArgb(127, 221, 203)) { Processes = ["Msty", "Msty Studio"], InstallNames = ["Msty", "Msty Studio"] }
    ];
    internal static bool IsBrowser(string process) => new[] { "chrome", "msedge", "firefox", "brave", "vivaldi", "opera" }.Contains(process, StringComparer.OrdinalIgnoreCase);
    internal bool Matches(WindowInfo window)
    {
        if (Custom)
            return Executable is not null && string.Equals(Executable, window.Executable, StringComparison.OrdinalIgnoreCase)
                && (WindowTitle is null || string.Equals(WindowTitle, window.Title, StringComparison.Ordinal));
        if (Processes.Contains(window.ProcessName, StringComparer.OrdinalIgnoreCase)) return true;
        if (!IsBrowser(window.ProcessName)) return false;
        string title = window.Title;
        foreach (var suffix in new[] { " - Google Chrome", " - Microsoft Edge", " — Mozilla Firefox", " - Brave", " - Vivaldi", " - Opera" })
            if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { title = title[..^suffix.Length]; break; }
        return WebNames.Any(name => title.Equals(name, StringComparison.OrdinalIgnoreCase) || title.EndsWith(" - " + name, StringComparison.OrdinalIgnoreCase));
    }
    internal void Launch()
    {
        if (LaunchTarget is null) throw new InvalidOperationException($"Open {Name}, then select it here. For a website, open it in its own browser window.");
        if (LaunchTarget.StartsWith("shell:AppsFolder\\", StringComparison.Ordinal))
        {
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            start.ArgumentList.Add(LaunchTarget); Process.Start(start);
        }
        else Process.Start(new ProcessStartInfo(LaunchTarget) { UseShellExecute = true });
    }
}

internal sealed record InstalledApp(string Name, string Target);
internal sealed record CustomConnection(string Key, string Name, string Executable, string? WindowTitle)
{
    internal AppEntry Entry => new(Key, Name, WindowTitle is null ? "Connected desktop app" : "Connected browser window", Name[..1].ToUpperInvariant(), UsageStyle.Good)
    { Executable = Executable, WindowTitle = WindowTitle, LaunchTarget = WindowTitle is null && File.Exists(Executable) ? Executable : null };
}

internal sealed class AppCatalog
{
    private readonly string path;
    private List<CustomConnection> custom = [];
    private InstalledApp[] installed = [];
    private DateTimeOffset nextScan;
    private bool scanning;
    internal AppEntry[] Available { get; private set; } = [];
    internal string? Warning { get; private set; }
    internal AppCatalog(string path)
    {
        this.path = path;
        try
        {
            if (File.Exists(path)) custom = (JsonSerializer.Deserialize<List<CustomConnection>>(File.ReadAllText(path)) ?? [])
                .Where(c => c.Key?.StartsWith("custom-", StringComparison.Ordinal) == true && !string.IsNullOrWhiteSpace(c.Name)
                    && !string.IsNullOrEmpty(c.Executable) && Path.IsPathFullyQualified(c.Executable)).DistinctBy(c => c.Key).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { Warning = "Saved connections could not be read. Reconnect an AI to save a new list."; }
    }
    internal static AppEntry[] Discover(IEnumerable<InstalledApp> installed, IEnumerable<WindowInfo> windows,
        IEnumerable<AppEntry> custom, IEnumerable<string> attached)
    {
        var inventory = installed.ToArray(); var open = windows.ToArray(); var retained = attached.ToHashSet();
        return AppEntry.All.Select(definition =>
        {
            var match = inventory.FirstOrDefault(item => definition.InstallNames.Contains(item.Name, StringComparer.OrdinalIgnoreCase));
            return definition with { LaunchTarget = match?.Target };
        }).Concat(custom).Where(entry => entry.LaunchTarget is not null || open.Any(entry.Matches) || retained.Contains(entry.Key)).ToArray();
    }
    internal async Task<bool> Refresh(IEnumerable<string> attached, bool force = false)
    {
        if (scanning) return false;
        scanning = true;
        try
        {
            if (force || DateTimeOffset.Now >= nextScan)
            {
                try { installed = await ScanInstalled().WaitAsync(TimeSpan.FromSeconds(8)); }
                catch (TimeoutException) { /* Open-window discovery remains usable if the Windows shell stalls. */ }
                nextScan = DateTimeOffset.Now.AddMinutes(1);
            }
            var found = Discover(installed, Native.Windows(), custom.Select(c => c.Entry), attached);
            bool changed = !Available.Select(a => (a.Key, a.Name, a.LaunchTarget)).SequenceEqual(found.Select(a => (a.Key, a.Name, a.LaunchTarget)));
            Available = found; return changed;
        }
        finally { scanning = false; }
    }
    internal AppEntry Connect(string name, WindowInfo window)
    {
        var known = Available.FirstOrDefault(a => a.Matches(window)) ?? AppEntry.All.FirstOrDefault(a => a.Matches(window));
        if (known is not null) return known;
        if (window.Executable is null) throw new InvalidOperationException("Windows did not allow this app's identity to be read. Open the app normally and try again.");
        name = name.Trim();
        if (name.Length is < 1 or > 40) throw new InvalidOperationException("Choose a name between 1 and 40 characters.");
        var connection = new CustomConnection("custom-" + Guid.NewGuid().ToString("N"), name, window.Executable,
            AppEntry.IsBrowser(window.ProcessName) ? window.Title : null);
        Save([.. custom, connection]);
        return connection.Entry;
    }
    internal void Forget(string key) => Save(custom.Where(c => c.Key != key).ToList());
    internal CustomConnection[] Connections => custom.ToArray();
    private void Save(List<CustomConnection> next)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(next, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true); custom = next; Warning = null;
    }
    private static Task<InstalledApp[]> ScanInstalled()
    {
        // Shell enumeration runs on its own STA, never on the UI thread.
        var result = new TaskCompletionSource<InstalledApp[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var found = new List<InstalledApp>();
            foreach (var folder in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms })
            {
                var root = Environment.GetFolderPath(folder);
                try
                {
                    if (Directory.Exists(root)) foreach (var shortcut in Directory.EnumerateFiles(root, "*.lnk", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                        found.Add(new(Path.GetFileNameWithoutExtension(shortcut), shortcut));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            object? shell = null, folderObject = null, itemsObject = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
                folderObject = ((dynamic)shell!).NameSpace("shell:::{4234d49b-0245-4df3-b780-3893943456e1}");
                itemsObject = ((dynamic)folderObject!).Items();
                int count = ((dynamic)itemsObject).Count;
                for (int i = 0; i < count; i++)
                {
                    object item = ((dynamic)itemsObject).Item(i);
                    try
                    {
                        string name = ((dynamic)item).Name, target = ((dynamic)item).Path;
                        if (!string.IsNullOrWhiteSpace(target)) found.Add(new(name, target.Contains('!') ? "shell:AppsFolder\\" + target : target));
                    }
                    finally { Marshal.ReleaseComObject(item); }
                }
            }
            catch (Exception) { /* Start-menu discovery still works when AppsFolder is unavailable. */ }
            finally
            {
                foreach (var obj in new[] { itemsObject, folderObject, shell }) if (obj is not null && Marshal.IsComObject(obj)) Marshal.ReleaseComObject(obj);
            }
            result.SetResult(found.ToArray());
        }) { IsBackground = true, Name = "AI Hub app discovery" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return result.Task;
    }
}
