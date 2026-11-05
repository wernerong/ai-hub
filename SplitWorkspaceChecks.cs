using System.Text.Json;

namespace AIHub;

internal static class SplitWorkspaceChecks
{
    internal static void Run(string folder, List<string> lines)
    {
        void Check(bool result, string label) { if (!result) throw new InvalidOperationException(label); lines.Add("PASS " + label); }
        string path = Path.Combine(folder, "split-workspaces.json");
        var store = new WorkspaceStore(path);
        var layout = new WorkspaceBounds(50, 60, 1400, 900, 96, false);
        var split = new WorkspaceSplit("openai", "grokbot", "grokbot", true, 65);
        var project = store.SaveProject(new("", "Compare AIs", ["openai", "grokbot"], "grokbot", "Review before sending", [], layout, split));
        store.SaveSession(project.Id, "grokbot", layout, split);
        var restored = new WorkspaceStore(path);
        Check(restored.LastSplit == split && restored.Projects.Single().Split == split && restored.LastSelectedAppKey == "grokbot",
            "Both split apps, focused pane, orientation and divider ratio survive restart");
        var moved = SplitSelection.Place(split, "grok");
        Check(moved.FirstAppKey == "openai" && moved.SecondAppKey == "grok" && moved.FocusedAppKey == "grok" && moved.Stacked && moved.Ratio == 65,
            "Selecting a third AI replaces only the focused pane without losing the layout");
        var focused = SplitSelection.Place(moved, "openai");
        Check(focused.FirstAppKey == moved.FirstAppKey && focused.SecondAppKey == moved.SecondAppKey && focused.FocusedAppKey == "openai",
            "Selecting an AI already in the other pane changes focus without duplicating windows");
        restored.SaveSession(project.Id, "grok", layout, moved);
        restored = new WorkspaceStore(path);
        Check(restored.LastSplit == moved && restored.Projects.Single().Split is null && restored.Projects.Single().AppKeys.SequenceEqual(project.AppKeys),
            "A temporary split outside saved membership resumes without silently adding apps to a project");
        restored.SaveProject(project with { AppKeys = ["openai"] });
        Check(restored.Projects.Single().Split is null, "Removing an app from a project clears a split that can no longer be restored");
        string before = File.ReadAllText(path);
        foreach (var invalid in new[] { split with { SecondAppKey = "openai" }, split with { FocusedAppKey = "grok" }, split with { Ratio = 90 } })
        {
            bool rejected = false;
            try { restored.SaveSession(project.Id, "grokbot", layout, invalid); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && File.ReadAllText(path) == before, "Invalid split identities or divider ratios preserve existing workspace data");
        }
        restored.SaveSession(null, "openai", layout);
        Check(new WorkspaceStore(path).LastSplit is null, "Returning to one AI clears the last split on disk");
        string oldPath = Path.Combine(folder, "v06-workspaces.json");
        File.WriteAllText(oldPath, JsonSerializer.Serialize(new { Version = 1, Projects = new[] {
            new { project.Id, project.Name, project.AppKeys, project.SelectedAppKey, project.Notes, project.Resources, project.Layout }
        }, ActiveProjectId = project.Id, ResumeOnStartup = true, LastSelectedAppKey = "openai", LastLayout = layout }));
        var old = new WorkspaceStore(oldPath);
        Check(old.Warning is null && old.Projects.Single().Notes == project.Notes && old.LastSplit is null && old.Projects.Single().Split is null,
            "Existing v0.6 projects migrate to a single AI without losing their saved context");
        string lockedPath = Path.Combine(folder, "temporarily-locked-settings.json"), prepared = lockedPath + ".tmp";
        File.WriteAllText(lockedPath, "previous settings"); File.WriteAllText(prepared, "new settings");
        using var held = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = Task.Run(() => { Thread.Sleep(100); held.Dispose(); });
        LocalFile.Replace(prepared, lockedPath); release.GetAwaiter().GetResult();
        Check(File.ReadAllText(lockedPath) == "new settings" && !File.Exists(prepared),
            "A short Windows file lock delays atomic settings replacement without losing the saved update");
    }
}
