namespace AIHub;

internal static class CopilotArchiveChecks
{
    internal static void Run(string folder, List<string> lines)
    {
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            lines.Add("PASS " + description);
        }
        string directory = Path.Combine(folder, "copilot-archive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "chats.json");
        var store = new CopilotChatStore(path);
        CopilotChat Chat(string workspace, string name) => new(Guid.NewGuid().ToString("N"), workspace, name,
            Path.GetFullPath(directory), "session-" + Guid.NewGuid().ToString("N"), "model-one", DateTimeOffset.UtcNow,
            [new("user", "Keep my original question", DateTimeOffset.UtcNow), new("assistant", "Keep my original answer", DateTimeOffset.UtcNow)]);
        var first = store.Save(Chat("deleted-project", "First conversation"));
        var second = store.Save(Chat("deleted-project", new string('a', 96)));
        var personal = store.Save(Chat("personal", "Existing personal chat"));
        var other = store.Save(Chat("other-project", "Unrelated project chat"));
        store.ArchiveWorkspace("deleted-project", "EKMS simulator");
        var archived = store.ForWorkspace("personal");
        Check(store.ForWorkspace("deleted-project").Length == 0 && archived.Length == 3,
            "Deleting a project moves its Copilot chats into the personal workspace without deleting them");
        foreach (var original in new[] { first, second })
        {
            var retained = archived.Single(chat => chat.Id == original.Id);
            Check(retained.Folder == original.Folder && retained.SessionId == original.SessionId && retained.ModelId == original.ModelId
                && retained.Updated == original.Updated && retained.Messages.SequenceEqual(original.Messages)
                && retained.Name.StartsWith("[EKMS simulator] ", StringComparison.Ordinal) && retained.Name.Length <= 96,
                "Archived Copilot chat retains its folder, agent session, model, history and time: " + original.Id[..8]);
        }
        Check(archived.Single(chat => chat.Id == personal.Id).Name == personal.Name
            && store.ForWorkspace("other-project").Single().Id == other.Id,
            "Archiving a project leaves existing personal chats and other projects unchanged");
        var loaded = new CopilotChatStore(path);
        Check(loaded.Warning is null && loaded.ForWorkspace("personal").Select(c => c.Id).Order().SequenceEqual(archived.Select(c => c.Id).Order()),
            "Archived Copilot conversations remain discoverable after restarting the store");
        byte[] once = File.ReadAllBytes(path);
        store.ArchiveWorkspace("deleted-project", "EKMS simulator");
        Check(File.ReadAllBytes(path).SequenceEqual(once), "Repeating project archival is idempotent and does not duplicate name prefixes");
        bool moveRejected = false;
        try { store.Save(archived.Single(c => c.Id == first.Id) with { WorkspaceKey = "another-project" }); }
        catch (InvalidOperationException) { moveRejected = true; }
        Check(moveRejected, "Normal Copilot saves still reject moving a chat between workspaces after archival");

        string lockedPath = Path.Combine(directory, "locked-chats.json");
        var lockedStore = new CopilotChatStore(lockedPath);
        var lockedChat = lockedStore.Save(Chat("locked-project", "Keep through failed save"));
        byte[] before = File.ReadAllBytes(lockedPath);
        bool failed = false;
        using (var held = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { lockedStore.ArchiveWorkspace("locked-project", "Locked project"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
        }
        Check(failed && lockedStore.ForWorkspace("locked-project").Single().Id == lockedChat.Id
            && lockedStore.ForWorkspace("personal").Length == 0 && File.ReadAllBytes(lockedPath).SequenceEqual(before),
            "A failed Copilot archive write preserves both the prior disk file and in-memory workspace identity");
        lockedStore.ArchiveWorkspace("locked-project", "Locked project");
        Check(lockedStore.ForWorkspace("personal").Single().Id == lockedChat.Id,
            "Copilot project archival can retry after its history file becomes writable");
        bool personalRejected = false, invalidRejected = false;
        try { store.ArchiveWorkspace("personal", "Personal"); } catch (ArgumentException) { personalRejected = true; }
        try { store.ArchiveWorkspace("bad\nkey", "Invalid"); } catch (ArgumentException) { invalidRejected = true; }
        Check(personalRejected && invalidRejected && File.ReadAllBytes(path).SequenceEqual(once),
            "Copilot archival rejects personal and invalid workspace keys without changing saved history");

        var orphaned = store.Save(Chat("already-deleted-project", "Retry this retained conversation"));
        store.ArchiveMissingWorkspaces(["other-project"]);
        Check(store.ForWorkspace("personal").Single(c => c.Id == orphaned.Id).Name == "[Archived project] " + orphaned.Name
            && store.ForWorkspace("other-project").Single().Id == other.Id,
            "Reopening Copilot recovers orphaned project chats while retaining chats of known projects");
        byte[] recovered = File.ReadAllBytes(path);
        store.ArchiveMissingWorkspaces(["other-project"]);
        Check(File.ReadAllBytes(path).SequenceEqual(recovered), "Repeated orphan recovery leaves already archived chat history unchanged");
        bool badRegistryRejected = false;
        try { store.ArchiveMissingWorkspaces(["other-project", "bad\nkey"]); }
        catch (ArgumentException) { badRegistryRejected = true; }
        Check(badRegistryRejected && File.ReadAllBytes(path).SequenceEqual(recovered),
            "Orphan recovery validates every supplied project identity before changing any chat");

        string malformed = Path.Combine(directory, "malformed-chats.json");
        File.WriteAllText(malformed, "{malformed original history");
        var damaged = new CopilotChatStore(malformed);
        bool guarded = false;
        try { damaged.ArchiveWorkspace("deleted-project", "Project"); } catch (InvalidOperationException) { guarded = true; }
        Check(guarded && File.ReadAllText(malformed) == "{malformed original history",
            "Copilot archival never overwrites malformed history while saving is disabled");
        bool missingGuarded = false;
        try { damaged.ArchiveMissingWorkspaces([]); } catch (InvalidOperationException) { missingGuarded = true; }
        Check(missingGuarded && File.ReadAllText(malformed) == "{malformed original history",
            "Orphan recovery refuses to rewrite malformed Copilot history");
    }
}
