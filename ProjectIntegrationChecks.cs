namespace AIHub;

internal static class ProjectIntegrationChecks
{
    internal static void CheckSessionSwitch(string folder, List<string> lines)
    {
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            lines.Add("PASS " + label);
        }
        string path = Path.Combine(folder, "session-switch-" + Guid.NewGuid().ToString("N") + ".json");
        var store = new WorkspaceStore(path);
        var layout = new WorkspaceBounds(80, 100, 1200, 800, 96, false);
        var split = new WorkspaceSplit("openai", "grok", "grok", Ratio: 65);
        var previous = store.SaveProject(new("", "Previous workspace", ["openai", "grok"], "grok", "Keep previous notes", [], layout, split));
        var target = store.SaveProject(new("", "Target workspace", ["openai"], "openai", "Keep target notes", [], layout with { Width = 1400 }));
        store.SaveSession(previous.Id, "grok", layout, split);
        byte[] before = File.ReadAllBytes(path);
        string? selected = "grok";
        WorkspaceSplit? runtimeSplit = split;
        int released = 0;
        void ReleaseCurrent() { released++; selected = null; runtimeSplit = null; }
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            bool projectRejected = false, personalRejected = false;
            try { WorkspaceSessionSwitch.Begin(store, target.Id, target.SelectedAppKey, target.Layout, target.Split, ReleaseCurrent); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { projectRejected = true; }
            try { WorkspaceSessionSwitch.Begin(store, null, null, layout, null, ReleaseCurrent); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { personalRejected = true; }
            Check(projectRejected && personalRejected && released == 0 && selected == "grok" && runtimeSplit == split
                && store.ActiveProjectId == previous.Id && store.LastSelectedAppKey == "grok" && store.LastSplit == split,
                "Failed project and personal switches retain the previous workspace without releasing windows");
        }
        Check(File.ReadAllBytes(path).SequenceEqual(before), "Failed workspace switches leave persisted project data unchanged");

        // A later autosave after the lock clears must still describe the intact
        // previous workspace, reproducing the original delayed-overwrite risk.
        store.SaveSession(store.ActiveProjectId, selected, layout, runtimeSplit);
        var reloaded = new WorkspaceStore(path);
        var retained = reloaded.Projects.Single(p => p.Id == previous.Id);
        Check(reloaded.ActiveProjectId == previous.Id && retained.SelectedAppKey == "grok" && retained.Split == split
            && retained.Notes == previous.Notes && reloaded.Projects.Single(p => p.Id == target.Id).SelectedAppKey == "openai",
            "An autosave after a failed workspace switch preserves both projects' saved selections and notes");

        bool projectCommittedBeforeRelease = false;
        WorkspaceSessionSwitch.Begin(store, target.Id, target.SelectedAppKey, target.Layout, target.Split, () =>
        {
            var saved = new WorkspaceStore(path);
            projectCommittedBeforeRelease = saved.ActiveProjectId == target.Id && saved.LastSelectedAppKey == "openai" && saved.LastLayout == target.Layout;
            ReleaseCurrent();
        });
        Check(projectCommittedBeforeRelease && released == 1, "A project switch persists its destination before releasing the previous workspace");
        bool personalCommittedBeforeRelease = false;
        WorkspaceSessionSwitch.Begin(store, null, null, layout, null, () =>
        {
            var saved = new WorkspaceStore(path);
            personalCommittedBeforeRelease = saved.ActiveProjectId is null && saved.LastSelectedAppKey is null && saved.LastSplit is null;
            ReleaseCurrent();
        });
        Check(personalCommittedBeforeRelease && released == 2, "A personal workspace switch clears saved project identity before releasing windows");
    }

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
        string key = "custom-preview", secondKey = "custom-preview-second";
        if (!catalog.Connections.Any(c => c.Key == key))
        {
            var connections = new[] {
                new CustomConnection(key, "Preview Alpha", Environment.ProcessPath!, "AI Hub preview Alpha"),
                new CustomConnection(secondKey, "Preview Beta", Environment.ProcessPath!, "AI Hub preview Beta") };
            File.WriteAllText(Path.Combine(folder, "connections.json"), System.Text.Json.JsonSerializer.Serialize(connections));
        }
        if (!File.Exists(Path.Combine(folder, "workspaces.json")))
        {
            var store = new WorkspaceStore(Path.Combine(folder, "workspaces.json"));
            var split = new WorkspaceSplit(key, secondKey, key);
            var project = store.SaveProject(new("", "Release preview", [key, secondKey], key,
                "Disposable workspace for checking split view, context handoffs and session resume.", [], null, split));
            store.SaveSession(project.Id, key, null, split);
        }
        using var sample = Program.StartSelf("--sample", "--title", "AI Hub preview Alpha");
        using var second = Program.StartSelf("--sample", "--title", "AI Hub preview Beta");
        using var hub = new HubForm(Path.Combine(folder, "preview-windows.json"), isolated: true);
        Application.Run(hub);
        // The fixture owns this disposable process; real provider processes are untouched.
        if (!sample.HasExited) { sample.CloseMainWindow(); sample.WaitForExit(3000); }
        if (!second.HasExited) { second.CloseMainWindow(); second.WaitForExit(3000); }
    }
}
