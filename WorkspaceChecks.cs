using System.Text.Json;

namespace AIHub;

internal static class WorkspaceChecks
{
    internal static void Run(string folder, List<string> lines)
    {
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); lines.Add("PASS " + message); }
        bool Rejects(Action action)
        {
            try { action(); return false; }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException) { return true; }
        }
        string root = Path.Combine(folder, "workspace-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "projects.json"), linkedFolder = Path.Combine(root, "project-files");
        Directory.CreateDirectory(linkedFolder);
        var store = new WorkspaceStore(path);
        Check(store.Projects.Length == 0 && store.ActiveProjectId is null && store.ResumeOnStartup && store.LastLayout is null
            && store.LastSelectedAppKey is null && store.Warning is null && !File.Exists(path),
            "A fresh projects store enables resume without writing settings or launching apps");
        store.SetResumeOnStartup(false);
        Check(new WorkspaceStore(path) is { ResumeOnStartup: false, Projects.Length: 0 },
            "The resume preference saves independently without requiring a named project");
        var layout = new WorkspaceBounds(-1200, 180, 1150, 800, 144, false);
        var first = store.SaveProject(new("", "  Reader work  ", ["openai", "grokbot", "openai", "custom-unavailable"], "grokbot",
            "Keep the existing reader behavior.\r\nReview changes before deploying.", ["https://example.com/project", linkedFolder], layout));
        store.SetResumeOnStartup(true); store.SaveSession(first.Id, "openai", layout with { Maximized = true });
        var loaded = new WorkspaceStore(path);
        var restored = loaded.Projects.Single();
        Check(Guid.TryParse(first.Id, out _) && restored.Name == "Reader work"
            && restored.AppKeys.SequenceEqual(new[] { "openai", "grokbot", "custom-unavailable" })
            && restored.SelectedAppKey == "openai" && restored.Layout?.Maximized == true
            && restored.Notes == first.Notes && restored.Resources.SequenceEqual(first.Resources)
            && loaded.ActiveProjectId == first.Id && loaded.ResumeOnStartup && loaded.LastSelectedAppKey == "openai" && loaded.LastLayout?.Dpi == 144,
            "Project apps, notes, resources, active selection, layout and resume preference survive restart");
        first.AppKeys[0] = "changed-return-value"; restored.Resources[0] = "https://example.com/changed-snapshot";
        Check(store.Projects.Single().AppKeys[0] == "openai" && loaded.Projects.Single().Resources[0] == "https://example.com/project",
            "Mutating a returned project cannot silently change saved project state");
        var second = loaded.SaveProject(new("", "Research", ["grok"], "grok", "Separate notes", [], null));
        var edit = loaded.Projects.Single(p => p.Id == first.Id);
        loaded.SaveProject(edit with { Notes = "Updated notes" });
        Check(loaded.Projects.Single(p => p.Id == first.Id).AppKeys.Contains("custom-unavailable")
            && loaded.Projects.Single(p => p.Id == second.Id).Notes == "Separate notes",
            "Editing notes preserves unavailable AI app identities and other projects");
        Directory.Delete(linkedFolder);
        loaded = new WorkspaceStore(path);
        var offline = loaded.Projects.Single(p => p.Id == first.Id);
        loaded.SaveProject(offline with { Notes = "Folder may be offline" });
        Check(loaded.Warning is null && loaded.Projects.Single(p => p.Id == first.Id).Resources.Contains(linkedFolder),
            "A previously saved folder remains in its project when it is temporarily unavailable");
        string beforeInvalid = File.ReadAllText(path);
        Check(Rejects(() => loaded.SaveProject(offline with { Name = new string('n', WorkspaceStore.MaxNameLength + 1) }))
            && Rejects(() => loaded.SaveProject(offline with { Name = " " }))
            && Rejects(() => loaded.SaveProject(offline with { Name = "RESEARCH" }))
            && Rejects(() => loaded.SaveProject(offline with { Notes = new string('n', WorkspaceStore.MaxNotesLength + 1) }))
            && Rejects(() => loaded.SaveProject(offline with { AppKeys = ["bad app key"] }))
            && Rejects(() => loaded.SaveProject(offline with { AppKeys = Enumerable.Range(0, WorkspaceStore.MaxApps + 1).Select(i => "app-" + i).ToArray() }))
            && File.ReadAllText(path) == beforeInvalid,
            "Invalid or duplicate names, oversized notes and invalid app lists leave saved projects untouched");
        Check(Rejects(() => loaded.SaveProject(offline with { Resources = ["javascript:alert(1)"] }))
            && Rejects(() => loaded.SaveProject(offline with { Resources = ["ftp://example.com/files"] }))
            && Rejects(() => loaded.SaveProject(offline with { Resources = [Path.Combine(root, "not-a-folder")] }))
            && Rejects(() => loaded.SaveProject(offline with { Resources = [path] }))
            && Rejects(() => loaded.SaveProject(offline with { Resources = Enumerable.Range(0, WorkspaceStore.MaxResources + 1).Select(i => "https://example.com/" + i).ToArray() }))
            && File.ReadAllText(path) == beforeInvalid,
            "Resource validation accepts only web links or folders and never overwrites a project on invalid input");
        Check(Rejects(() => loaded.SaveSession(first.Id, "openai", layout with { Width = -1 }))
            && Rejects(() => loaded.SaveSession("missing-project", "openai", layout))
            && File.ReadAllText(path) == beforeInvalid,
            "Invalid geometry and unknown active project identities cannot corrupt session persistence");
        loaded.SaveSession(first.Id, "grok", layout);
        Check(loaded.Projects.Single(p => p.Id == first.Id).SelectedAppKey is null && loaded.LastSelectedAppKey == "grok",
            "A session can remember another AI without adding it to the active project's saved apps");
        loaded.RemoveProject(first.Id); loaded = new WorkspaceStore(path);
        Check(loaded.ActiveProjectId is null && loaded.Projects.Single().Id == second.Id && loaded.LastSelectedAppKey == "grok"
            && loaded.ResumeOnStartup && loaded.Projects.Single().Notes == "Separate notes",
            "Deleting the active project clears its identity while preserving another project and session preferences");
        loaded.SaveSession(null, null, null); loaded.SetResumeOnStartup(false);
        Check(new WorkspaceStore(path) is { ResumeOnStartup: false, LastSelectedAppKey: null, LastLayout: null },
            "Resume can be disabled and an empty last session survives restart");
        string startupPath = Path.Combine(root, "startup.json");
        var startup = new WorkspaceStore(startupPath);
        var previousProject = startup.SaveProject(second with { Notes = "Keep these project instructions", Layout = layout });
        startup.SaveSession(previousProject.Id, "grok", layout);
        Check(WorkspaceStartup.TryStartPersonal(startup, out var startupError) && startupError is null,
            "A manual startup action can enter a personal session before project resume finishes");
        // A later selection/close is the operation that previously overwrote the
        // unopened project. Exercise that write and then reload from disk.
        startup.SaveSession(startup.ActiveProjectId, "openai", layout with { Width = 1400 });
        var afterCanceledResume = new WorkspaceStore(startupPath);
        var untouchedProject = afterCanceledResume.Projects.Single();
        Check(afterCanceledResume.ActiveProjectId is null && afterCanceledResume.LastSelectedAppKey == "openai"
            && untouchedProject.SelectedAppKey == "grok" && untouchedProject.Layout == layout
            && untouchedProject.Notes == previousProject.Notes && untouchedProject.AppKeys.SequenceEqual(previousProject.AppKeys),
            "Saving a manual startup session preserves the unopened project's selected AI, layout and notes");
        string corruptPath = Path.Combine(root, "corrupt.json"), original = "{\"Version\":1,\"Projects\":[unfinished";
        File.WriteAllText(corruptPath, original);
        var corrupt = new WorkspaceStore(corruptPath);
        Check(corrupt.Warning?.Contains("corrupt.json") == true && corrupt.Projects.Length == 0
            && Rejects(() => corrupt.SaveProject(second)) && Rejects(() => corrupt.RemoveProject(second.Id))
            && Rejects(() => corrupt.SaveSession(null, "grok", layout)) && Rejects(() => corrupt.SetResumeOnStartup(true))
            && File.ReadAllText(corruptPath) == original,
            "Malformed project data shows a warning and every write path preserves the original file");
        Check(!WorkspaceStartup.TryStartPersonal(corrupt, out var resetError) && resetError is not null
            && File.ReadAllText(corruptPath) == original,
            "A failed startup reset reports failure without enabling session persistence or replacing project data");
        string futurePath = Path.Combine(root, "future.json");
        File.WriteAllText(futurePath, "{\"Version\":2,\"Projects\":[]}");
        Check(new WorkspaceStore(futurePath).Warning is not null && File.ReadAllText(futurePath).Contains("\"Version\":2"),
            "An unsupported future projects format is preserved rather than silently reset");
        string invalidPath = Path.Combine(root, "invalid.json");
        File.WriteAllText(invalidPath, JsonSerializer.Serialize(new { Version = 1, Projects = new[] { second, second } }));
        Check(new WorkspaceStore(invalidPath).Warning is not null,
            "Duplicate saved project identities are rejected before the file can be edited");
        string limitedPath = Path.Combine(root, "limits.json");
        File.WriteAllText(limitedPath, JsonSerializer.Serialize(new { Version = 1, Projects = Enumerable.Range(0, WorkspaceStore.MaxProjects)
            .Select(i => second with { Id = Guid.NewGuid().ToString("N"), Name = "Project " + i }).ToArray() }));
        var limited = new WorkspaceStore(limitedPath);
        Check(limited.Warning is null && limited.Projects.Length == WorkspaceStore.MaxProjects
            && Rejects(() => limited.SaveProject(second with { Id = "", Name = "One too many" })),
            "The project limit permits existing projects to load but rejects an additional project");
        Check(!Directory.EnumerateFiles(root, "*.tmp").Any(), "Completed project writes leave no temporary settings files");
    }
}
