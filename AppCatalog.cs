using System.Diagnostics;

namespace AIHub;

internal sealed record AppEntry(string Key, string Name, string Description, string Badge, Color Accent)
{
    internal static readonly AppEntry[] All = [
        new("openai", "OpenAI", "ChatGPT · Codex · Dot", "O", Color.FromArgb(136, 222, 196)),
        new("grok", "Grok", "Chat · Search · Imagine", "G", Color.FromArgb(220, 220, 230)),
        new("grokbot", "Grok Bot", "Your Cursor teammates", "B", Color.FromArgb(188, 170, 245))
    ];
    internal bool Matches(WindowInfo window) => Key switch
    {
        "openai" => window.ProcessName.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase) || window.ProcessName.Equals("Codex", StringComparison.OrdinalIgnoreCase),
        "grokbot" => window.ProcessName.Equals("Grok Bot", StringComparison.OrdinalIgnoreCase),
        "grok" => (window.ProcessName.Equals("chrome", StringComparison.OrdinalIgnoreCase) || window.ProcessName.Equals("msedge", StringComparison.OrdinalIgnoreCase) || window.ProcessName.Equals("Grok", StringComparison.OrdinalIgnoreCase))
            && (window.Title.Equals("Grok", StringComparison.OrdinalIgnoreCase) || window.Title.EndsWith(" - Grok", StringComparison.OrdinalIgnoreCase)),
        _ => false
    };
    internal void Launch()
    {
        if (Key == "openai")
        {
            Process.Start(new ProcessStartInfo("explorer.exe", @"shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App") { UseShellExecute = true });
            return;
        }
        string shortcutName = Key == "grok" ? "Grok.lnk" : "Grok Bot.lnk";
        foreach (var folder in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms })
        {
            var root = Environment.GetFolderPath(folder);
            var shortcut = Directory.Exists(root) ? Directory.EnumerateFiles(root, shortcutName, SearchOption.AllDirectories).FirstOrDefault() : null;
            if (shortcut is null) continue;
            Process.Start(new ProcessStartInfo(shortcut) { UseShellExecute = true });
            return;
        }
        if (Key == "grokbot")
        {
            var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Grok Bot", "Grok Bot.exe");
            if (File.Exists(exe)) { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true }); return; }
        }
        throw new InvalidOperationException($"Open {Name} first, then select it here again. AI Hub could not find its installed shortcut.");
    }
}
