namespace AIHub;

internal static class CommandChecks
{
    internal static void Run(List<string> lines)
    {
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); lines.Add("PASS " + message); }
        HubCommand[] available = [
            new("app:openai", "Open OpenAI", "ChatGPT · Codex · Dot", "assistant chat"),
            new("project:ekms", "Open eKMS", "Project workspace", "cabinet controller"),
            new("usage", "Usage overview", "Remaining allowance and resets", "quota credits"),
            new("reconnect:browser", "Reconnect research", "Choose its browser window", "repair connection")
        ];
        var initial = available.ToArray();
        Check(CommandPaletteDialog.Filter(available, "  \t\r\n").SequenceEqual(available), "An empty command search retains available commands in their supplied order");
        Check(CommandPaletteDialog.Filter(available, "OPENAI").Single().Id == "app:openai", "Command search matches app names without case sensitivity");
        Check(CommandPaletteDialog.Filter(available, "codex").Single().Id == "app:openai", "Command search finds product names in descriptions");
        Check(CommandPaletteDialog.Filter(available, "quota").Single().Id == "usage", "Command search finds alternative names through keywords");
        Check(CommandPaletteDialog.Filter(available, "project\tCONTROLLER").Single().Id == "project:ekms", "Command search requires every term across titles, descriptions and keywords");
        Check(CommandPaletteDialog.Filter(available, "Open quota").Length == 0, "Unrelated search terms do not offer an incorrect command");
        Check(CommandPaletteDialog.Filter(available, "Claude").Length == 0, "The command palette never invents commands for unavailable apps");
        Check(CommandPaletteDialog.Filter([], "Open").Length == 0, "The command palette handles an empty command inventory");
        Check(available.SequenceEqual(initial), "Searching commands does not mutate the supplied inventory");
        Check(CommandShortcut.IsSupported(Keys.Space, Keys.Control | Keys.Alt)
            && CommandShortcut.IsSupported(Keys.K, Keys.Control | Keys.Alt)
            && CommandShortcut.IsSupported(Keys.P, Keys.Control | Keys.Alt), "Global commands support the three configurable Ctrl+Alt shortcuts");
        Check(!CommandShortcut.IsSupported(Keys.K, Keys.Control) && !CommandShortcut.IsSupported(Keys.Space, Keys.None)
            && !CommandShortcut.IsSupported(Keys.LWin, Keys.Control | Keys.Alt), "Global shortcuts reject ordinary app shortcuts and unsupported keys");
    }
}
