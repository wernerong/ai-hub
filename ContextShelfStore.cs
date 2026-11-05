using System.Text;
using System.Text.Json;

namespace AIHub;

internal sealed record ContextShelfEntry(string Id, string? ProjectId, string Title, string Text, string[] Files);
internal sealed record HandoffTemplate(string? ProjectId, string Goal, string Decisions, string Evidence, string Constraints, string NextStep);

internal sealed class ContextShelfStore
{
    internal const int MaxEntries = 200, MaxEntriesPerProject = 64, MaxTitleLength = 80, MaxTextLength = 12000,
        MaxFiles = 12, MaxFieldLength = 6000, MaxTemplates = 51;
    private const int MaxFileBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string path;
    private ShelfData data = new();
    internal string? Warning { get; private set; }

    internal ContextShelfStore(string path)
    {
        this.path = Path.GetFullPath(path);
        try
        {
            if (!File.Exists(this.path)) return;
            if (new FileInfo(this.path).Length > MaxFileBytes) throw new InvalidDataException("The shelf file is too large.");
            using var document = JsonDocument.Parse(File.ReadAllText(this.path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Version", out var version)
                || !version.TryGetInt32(out int number) || number != 1
                || !root.TryGetProperty("Entries", out var entries) || entries.ValueKind != JsonValueKind.Array
                || !root.TryGetProperty("Templates", out var templates) || templates.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("The shelf format is invalid or unsupported.");
            var loaded = root.Deserialize<ShelfData>(Json) ?? throw new InvalidDataException("The shelf file is empty.");
            if (loaded.Entries is null || loaded.Entries.Length > MaxEntries || loaded.Templates is null || loaded.Templates.Length > MaxTemplates)
                throw new InvalidDataException("The shelf contains too many saved items.");
            loaded.Entries = loaded.Entries.Select(e => NormalizeEntry(e, true)).ToArray();
            loaded.Templates = loaded.Templates.Select(NormalizeTemplate).ToArray();
            if (loaded.Entries.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != loaded.Entries.Length
                || loaded.Entries.GroupBy(e => e.ProjectId).Any(g => g.Count() > MaxEntriesPerProject)
                || loaded.Templates.Select(t => t.ProjectId).Distinct(StringComparer.Ordinal).Count() != loaded.Templates.Length)
                throw new InvalidDataException("The shelf contains duplicate identities or too many items in a project.");
            data = loaded;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            Warning = "The context shelf could not be read. The original file is preserved and saving is disabled. Restore or rename "
                + Path.GetFileName(this.path) + ", then restart AI Hub.";
        }
    }

    internal ContextShelfEntry[] EntriesFor(string? projectId)
    {
        ValidateScope(projectId);
        return data.Entries.Where(e => e.ProjectId == projectId).Select(Copy).ToArray();
    }
    internal HandoffTemplate TemplateFor(string? projectId)
    {
        ValidateScope(projectId);
        return data.Templates.FirstOrDefault(t => t.ProjectId == projectId) ?? new(projectId, "", "", "", "", "");
    }
    internal ContextShelfEntry SaveEntry(ContextShelfEntry entry)
    {
        EnsureWritable(); ArgumentNullException.ThrowIfNull(entry);
        var previous = data.Entries.FirstOrDefault(e => e.Id == entry.Id);
        if (previous is not null && previous.ProjectId != entry.ProjectId)
            throw new InvalidOperationException("An existing shelf item cannot move to a different project. Add a new item there instead.");
        var saved = NormalizeEntry(entry, false, previous);
        if (previous is null && (data.Entries.Length >= MaxEntries || data.Entries.Count(e => e.ProjectId == saved.ProjectId) >= MaxEntriesPerProject))
            throw new InvalidOperationException($"Keep up to {MaxEntriesPerProject} items per project and {MaxEntries} items overall. Remove an item before adding another.");
        var entries = previous is null ? data.Entries.Append(saved).ToArray() : data.Entries.Select(e => e.Id == saved.Id ? saved : e).ToArray();
        Save(data with { Entries = entries }); return Copy(saved);
    }
    internal void RemoveEntry(string id)
    {
        EnsureWritable();
        if (data.Entries.Any(e => e.Id == id)) Save(data with { Entries = data.Entries.Where(e => e.Id != id).ToArray() });
    }
    internal void RemoveProject(string projectId)
    {
        EnsureWritable();
        if (projectId is null) throw new ArgumentNullException(nameof(projectId));
        ValidateScope(projectId);
        if (data.Entries.Any(e => e.ProjectId == projectId) || data.Templates.Any(t => t.ProjectId == projectId))
            Save(data with { Entries = data.Entries.Where(e => e.ProjectId != projectId).ToArray(), Templates = data.Templates.Where(t => t.ProjectId != projectId).ToArray() });
    }
    internal void SaveTemplate(HandoffTemplate template)
    {
        EnsureWritable(); var saved = NormalizeTemplate(template);
        bool exists = data.Templates.Any(t => t.ProjectId == saved.ProjectId);
        if (!exists && data.Templates.Length >= MaxTemplates) throw new InvalidOperationException("The maximum number of saved handoff drafts has been reached.");
        Save(data with { Templates = exists ? data.Templates.Select(t => t.ProjectId == saved.ProjectId ? saved : t).ToArray() : [.. data.Templates, saved] });
    }

    private static ContextShelfEntry Copy(ContextShelfEntry entry) => entry with { Files = [.. entry.Files] };
    private static ContextShelfEntry NormalizeEntry(ContextShelfEntry entry, bool loading, ContextShelfEntry? previous = null)
    {
        if (entry is null) throw new InvalidDataException("A shelf item is missing.");
        ValidateScope(entry.ProjectId);
        string id = entry.Id;
        if (string.IsNullOrEmpty(id) && !loading) id = Guid.NewGuid().ToString("N");
        if (!Guid.TryParse(id, out _)) throw new InvalidOperationException("The shelf item identity is invalid.");
        string title = entry.Title?.Trim() ?? "", text = entry.Text ?? "";
        if (title.Length is < 1 or > MaxTitleLength || title.Any(char.IsControl))
            throw new InvalidOperationException($"Enter a title between 1 and {MaxTitleLength} characters.");
        if (text.Length > MaxTextLength || text.Contains('\0')) throw new InvalidOperationException($"A shelf item can contain up to {MaxTextLength:N0} characters.");
        if (entry.Files is null || entry.Files.Length > MaxFiles) throw new InvalidOperationException($"An item can reference up to {MaxFiles} files.");
        var files = entry.Files.Select(file =>
        {
            string value = file?.Trim() ?? "";
            if (value.Length is < 1 or > 2048 || value.Any(char.IsControl) || !Path.IsPathFullyQualified(value)
                || !(loading || previous?.Files.Contains(value, StringComparer.OrdinalIgnoreCase) == true || File.Exists(value)))
                throw new InvalidOperationException("Choose an existing file. Only its full path is saved; the file is not read or attached.");
            return value;
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (string.IsNullOrWhiteSpace(text) && files.Length == 0) throw new InvalidOperationException("Add some text or a file reference to this item.");
        return new(id, entry.ProjectId, title, text, files);
    }
    internal static HandoffTemplate NormalizeTemplate(HandoffTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template); ValidateScope(template.ProjectId);
        string Validate(string? value)
        {
            value ??= "";
            if (value.Length > MaxFieldLength || value.Contains('\0')) throw new InvalidOperationException($"Each handoff field can contain up to {MaxFieldLength:N0} characters.");
            return value;
        }
        return new(template.ProjectId, Validate(template.Goal), Validate(template.Decisions), Validate(template.Evidence), Validate(template.Constraints), Validate(template.NextStep));
    }
    private static void ValidateScope(string? projectId)
    {
        if (projectId is not null && !Guid.TryParse(projectId, out _)) throw new InvalidOperationException("The project identity is invalid.");
    }
    private void EnsureWritable() { if (Warning is not null) throw new InvalidOperationException(Warning); }
    private void Save(ShelfData next)
    {
        EnsureWritable();
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(next, Json);
        if (bytes.Length > MaxFileBytes) throw new InvalidOperationException("The context shelf is full. Shorten or remove some items before saving.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            LocalFile.Replace(temporary, path); data = next;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private sealed record ShelfData
    {
        public ShelfData() { }
        public int Version { get; init; } = 1;
        public ContextShelfEntry[] Entries { get; set; } = [];
        public HandoffTemplate[] Templates { get; set; } = [];
    }
}

internal static class HandoffText
{
    internal const int MaxLength = 100000;
    internal static string Render(string projectName, string? targetName, HandoffTemplate template,
        IEnumerable<ContextShelfEntry> entries, string? projectNotes = null)
    {
        template = ContextShelfStore.NormalizeTemplate(template);
        var selected = entries.ToArray();
        if (selected.Any(e => e.ProjectId != template.ProjectId)) throw new InvalidOperationException("Only items from this project's shelf can be included.");
        if (selected.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != selected.Length)
            throw new InvalidOperationException("A shelf item can be included only once.");
        var result = new StringBuilder("AI Hub handoff");
        result.Append("\r\nWorkspace: ").Append(projectName);
        if (!string.IsNullOrWhiteSpace(targetName)) result.Append("\r\nFor: ").Append(targetName);
        void Section(string title, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            result.Append("\r\n\r\n").Append(title).Append("\r\n").Append(value);
            if (result.Length > MaxLength) throw new InvalidOperationException("This handoff is too long. Include fewer shelf items or shorten the text.");
        }
        Section("Goal", template.Goal); Section("Decisions", template.Decisions); Section("Evidence", template.Evidence);
        Section("Constraints", template.Constraints); Section("Next step", template.NextStep);
        Section("Project notes", projectNotes);
        foreach (var entry in selected)
        {
            Section("Context: " + entry.Title, entry.Text);
            if (entry.Files.Length > 0)
                Section("File references: " + entry.Title, string.Join("\r\n", entry.Files.Select(file => "- " + file))
                    + "\r\nThese are file paths only. Attach the files manually in the target AI; their contents are not included.");
        }
        if (result.Length > MaxLength) throw new InvalidOperationException("This handoff is too long. Shorten the workspace name or text.");
        return result.ToString();
    }
}
