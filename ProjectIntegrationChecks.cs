namespace AIHub;

internal static class ProjectIntegrationChecks
{
    internal static void CheckPlacement(List<string> lines)
    {
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            lines.Add("PASS " + label);
        }
        var area = new Rectangle(0, 0, 1920, 1040);
        var offscreen = WorkspacePlacement.Fit(new(-3000, -2000, 1400, 900, 96, false), area, 96);
        Check(area.Contains(offscreen) && offscreen.Size == new Size(1400, 900), "Project layout returns to a visible screen after a monitor is disconnected");
        var negative = new Rectangle(-1920, 0, 1920, 1080);
        Check(WorkspacePlacement.Fit(new(-1600, 50, 1200, 800, 96, false), negative, 96).X == -1600, "Project layout preserves valid negative monitor coordinates");
        var scaled = WorkspacePlacement.Fit(new(10, 10, 1200, 800, 144, false), area, 192);
        Check(scaled.Width == 1600 && area.Contains(scaled), "Project layout scales for DPI and stays within the working area");
        var small = new Rectangle(0, 0, 800, 600);
        Check(WorkspacePlacement.Fit(new(500, 500, 1800, 1200, 96, true), small, 96) == small, "Project bounds clamp to a small display without offscreen content");
    }

    // Interactive verification uses an isolated data folder and a disposable
    // provider window, never the user's projects, chats or usage credentials.
    internal static void Show(string folder)
    {
        folder = Path.GetFullPath(folder); Directory.CreateDirectory(folder);
        var catalog = new AppCatalog(Path.Combine(folder, "connections.json"));
        var key = "custom-preview";
        if (!catalog.Connections.Any(c => c.Key == key))
        {
            var connection = new CustomConnection(key, "Preview AI", Environment.ProcessPath!, "AI Hub test window");
            File.WriteAllText(Path.Combine(folder, "connections.json"), System.Text.Json.JsonSerializer.Serialize(new[] { connection }));
        }
        if (!File.Exists(Path.Combine(folder, "workspaces.json")))
        {
            var store = new WorkspaceStore(Path.Combine(folder, "workspaces.json"));
            var project = store.SaveProject(new("", "Release preview", [key], key,
                "Disposable workspace for checking projects, command search and session resume.", [], null));
            store.SaveSession(project.Id, key, null);
        }
        using var sample = Program.StartSelf("--sample");
        using var hub = new HubForm(Path.Combine(folder, "preview-windows.json"), isolated: true);
        Application.Run(hub);
        // The fixture owns this disposable process; real provider processes are untouched.
        if (!sample.HasExited) { sample.CloseMainWindow(); sample.WaitForExit(3000); }
    }
}
