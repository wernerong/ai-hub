using System.Text.Json;

namespace AIHub;

internal static class ContextShelfChecks
{
    internal static void Run(string folder, List<string> lines)
    {
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); lines.Add("PASS " + label); }
        bool Rejects(Action action)
        {
            try { action(); return false; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException) { return true; }
        }
        string root = Path.Combine(folder, "context-shelf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "context-shelf.json"), projectA = Guid.NewGuid().ToString("N"), projectB = Guid.NewGuid().ToString("N");
        var store = new ContextShelfStore(path);
        Check(store.Warning is null && store.EntriesFor(null).Length == 0 && store.EntriesFor(projectA).Length == 0
            && store.TemplateFor(projectA) == new HandoffTemplate(projectA, "", "", "", "", "") && !File.Exists(path),
            "A new context shelf starts empty with an independent draft for each project and no background writes");
        var personal = store.SaveEntry(new("", null, "Personal guidance", "Keep explanations concise.", []));
        var entryA = store.SaveEntry(new("", projectA, "Behavior contract", "Preserve the reader event order.", []));
        var entryB = store.SaveEntry(new("", projectB, "Research context", "Compare the available evidence.", []));
        var template = new HandoffTemplate(projectA, "Review the change", "Keep the current protocol", "Test case 12", "No deployment", "List any regressions");
        store.SaveTemplate(template);
        store.SaveTemplate(new(null, "Personal task", "", "", "", ""));
        store.SaveTemplate(new(projectB, "Research task", "", "", "", ""));
        var loaded = new ContextShelfStore(path);
        Check(Guid.TryParse(entryA.Id, out _) && loaded.EntriesFor(projectA).Single().Id == entryA.Id
            && loaded.EntriesFor(projectB).Single().Id == entryB.Id && loaded.EntriesFor(null).Single().Id == personal.Id
            && loaded.TemplateFor(projectA) == template && loaded.TemplateFor(null).Goal == "Personal task",
            "Context entries and editable handoff fields survive restart without crossing project or personal scopes");
        string before = File.ReadAllText(path);
        Check(Rejects(() => loaded.SaveEntry(entryA with { ProjectId = projectB })) && File.ReadAllText(path) == before,
            "Editing an existing context item cannot silently move it into another project");
        string filePath = Path.Combine(root, "reference.txt"), privateContents = "FILE-CONTENTS-MUST-NOT-ENTER-THE-HANDOFF-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(filePath, privateContents);
        ContextShelfEntry referenced;
        string withFiles;
        using (var exclusive = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            referenced = loaded.SaveEntry(new("", projectA, "Source file", "Read this only after I attach it.", [filePath, filePath]));
            withFiles = HandoffText.Render("Reader project", "Grok Bot", template, [referenced]);
        }
        Check(referenced.Files.Length == 1 && withFiles.Contains(filePath) && withFiles.Contains("Attach the files manually")
            && !withFiles.Contains(privateContents) && !File.ReadAllText(path).Contains(privateContents),
            "Saving and rendering an exclusively locked file stores only its path and never reads or embeds file contents");
        referenced.Files[0] = "changed-return-value";
        var exposed = loaded.EntriesFor(projectA).Single(e => e.Title == "Source file"); exposed.Files[0] = "changed-snapshot";
        Check(loaded.EntriesFor(projectA).Single(e => e.Title == "Source file").Files.Single() == filePath,
            "Returned context item arrays cannot mutate persisted file references");
        File.Delete(filePath);
        loaded = new ContextShelfStore(path);
        var missingReference = loaded.EntriesFor(projectA).Single(e => e.Title == "Source file");
        loaded.SaveEntry(missingReference with { Text = "The original file can be reattached later." });
        Check(loaded.Warning is null && loaded.EntriesFor(projectA).Single(e => e.Id == missingReference.Id).Files.Single() == filePath,
            "An unavailable referenced file stays saved when the user edits the item's text");
        var exact = HandoffText.Render("Reader project", "Grok Bot", template, [entryA]);
        string expected = "AI Hub handoff\r\nWorkspace: Reader project\r\nFor: Grok Bot"
            + "\r\n\r\nGoal\r\nReview the change\r\n\r\nDecisions\r\nKeep the current protocol\r\n\r\nEvidence\r\nTest case 12"
            + "\r\n\r\nConstraints\r\nNo deployment\r\n\r\nNext step\r\nList any regressions"
            + "\r\n\r\nContext: Behavior contract\r\nPreserve the reader event order.";
        Check(exact == expected && !exact.Contains(entryB.Text) && !exact.Contains(personal.Text),
            "The deterministic handoff preview includes exactly the chosen draft fields and selected project context");
        Check(!HandoffText.Render("Reader", null, template, []).Contains("Project notes")
            && HandoffText.Render("Reader", null, template, [], "Explicitly included notes").Contains("Project notes\r\nExplicitly included notes"),
            "Project notes are absent unless explicitly supplied for inclusion in the handoff");
        var indented = entryA with { Text = "    Preserve indentation\r\n        for code snippets\r\n" };
        Check(HandoffText.Render("Reader", null, template, [indented]).Contains(indented.Text),
            "Handoff rendering preserves snippet indentation and line breaks");
        Check(Rejects(() => HandoffText.Render("Reader", "Grok", template, [entryB]))
            && Rejects(() => HandoffText.Render("Reader", "Grok", template, [entryA, entryA])),
            "A handoff rejects context from another project and duplicate selections");
        before = File.ReadAllText(path);
        Check(Rejects(() => loaded.SaveEntry(entryA with { Title = " " }))
            && Rejects(() => loaded.SaveEntry(entryA with { Title = new string('x', ContextShelfStore.MaxTitleLength + 1) }))
            && Rejects(() => loaded.SaveEntry(entryA with { Text = new string('x', ContextShelfStore.MaxTextLength + 1) }))
            && Rejects(() => loaded.SaveEntry(entryA with { Text = "", Files = [] }))
            && Rejects(() => loaded.SaveTemplate(template with { Evidence = new string('x', ContextShelfStore.MaxFieldLength + 1) }))
            && File.ReadAllText(path) == before,
            "Invalid titles, empty items and oversized context or draft fields leave saved data untouched");
        Check(Rejects(() => loaded.SaveEntry(entryA with { Files = ["relative.txt"] }))
            && Rejects(() => loaded.SaveEntry(entryA with { Files = [root] }))
            && Rejects(() => loaded.SaveEntry(entryA with { Files = [filePath] }))
            && Rejects(() => loaded.SaveEntry(entryA with { Files = Enumerable.Repeat(filePath, ContextShelfStore.MaxFiles + 1).ToArray() }))
            && File.ReadAllText(path) == before,
            "New references require existing files and respect path and item limits");
        var tooMuch = Enumerable.Range(0, 10).Select(i => entryA with { Id = Guid.NewGuid().ToString("N"), Text = new string('x', ContextShelfStore.MaxTextLength) });
        Check(Rejects(() => HandoffText.Render("Reader", null, template, tooMuch)),
            "Oversized handoffs are rejected before they can be copied");
        loaded.RemoveEntry(missingReference.Id);
        Check(loaded.EntriesFor(projectA).Single().Id == entryA.Id && loaded.EntriesFor(projectB).Single().Id == entryB.Id,
            "Removing one context item preserves the other items and project scopes");
        loaded.RemoveProject(projectA); loaded = new ContextShelfStore(path);
        Check(loaded.EntriesFor(projectA).Length == 0 && loaded.TemplateFor(projectA).Goal == ""
            && loaded.EntriesFor(projectB).Single().Id == entryB.Id && loaded.TemplateFor(projectB).Goal == "Research task"
            && loaded.EntriesFor(null).Single().Id == personal.Id && loaded.TemplateFor(null).Goal == "Personal task",
            "Deleting a project removes its context and draft together while preserving personal and other project data");
        before = File.ReadAllText(path);
        Check(Rejects(() => loaded.RemoveProject(null!)) && Rejects(() => loaded.RemoveProject("invalid")) && File.ReadAllText(path) == before,
            "Project cleanup cannot target personal context or an invalid project identity");
        string corruptPath = Path.Combine(root, "corrupt-shelf.json"), malformed = "{\"Version\":1,\"Entries\":[";
        File.WriteAllText(corruptPath, malformed);
        var corrupt = new ContextShelfStore(corruptPath);
        Check(corrupt.Warning?.Contains("corrupt-shelf.json") == true && Rejects(() => corrupt.SaveEntry(entryA))
            && Rejects(() => corrupt.SaveTemplate(template)) && Rejects(() => corrupt.RemoveEntry(entryA.Id))
            && Rejects(() => corrupt.RemoveProject(projectA)) && File.ReadAllText(corruptPath) == malformed,
            "A malformed context shelf warns the user and preserves its bytes through every mutation path");
        string futurePath = Path.Combine(root, "future-shelf.json");
        File.WriteAllText(futurePath, "{\"Version\":2,\"Entries\":[],\"Templates\":[]}");
        Check(new ContextShelfStore(futurePath).Warning is not null,
            "A future context shelf version is preserved instead of silently reset");
        string duplicatePath = Path.Combine(root, "duplicate-shelf.json");
        File.WriteAllText(duplicatePath, JsonSerializer.Serialize(new { Version = 1, Entries = new[] { entryA, entryA }, Templates = Array.Empty<HandoffTemplate>() }));
        Check(new ContextShelfStore(duplicatePath).Warning is not null,
            "Duplicate shelf identities are rejected before they can overwrite one another");
        string fullPath = Path.Combine(root, "full-shelf.json");
        File.WriteAllText(fullPath, JsonSerializer.Serialize(new { Version = 1,
            Entries = Enumerable.Range(0, ContextShelfStore.MaxEntriesPerProject).Select(i => entryA with { Id = Guid.NewGuid().ToString("N"), Title = "Item " + i }).ToArray(),
            Templates = Array.Empty<HandoffTemplate>() }));
        var full = new ContextShelfStore(fullPath);
        Check(full.Warning is null && Rejects(() => full.SaveEntry(entryA with { Id = "" }))
            && full.EntriesFor(projectA).Length == ContextShelfStore.MaxEntriesPerProject,
            "The per-project context limit allows existing entries to load and rejects another entry");
        string blockedPath = Path.Combine(root, "blocked-shelf"); Directory.CreateDirectory(blockedPath);
        var blocked = new ContextShelfStore(blockedPath);
        Check(Rejects(() => blocked.SaveEntry(entryA)) && blocked.EntriesFor(projectA).Length == 0 && Directory.Exists(blockedPath)
            && !Directory.EnumerateFiles(root, "*.tmp").Any(),
            "A failed atomic shelf write leaves memory and existing filesystem data intact without abandoned temporary files");
    }
}
