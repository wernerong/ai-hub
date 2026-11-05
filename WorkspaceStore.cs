using System.Text.Json;

namespace AIHub;

internal sealed record WorkspaceBounds(int X, int Y, int Width, int Height, int Dpi, bool Maximized);
internal sealed record ProjectWorkspace(string Id, string Name, string[] AppKeys, string? SelectedAppKey,
    string Notes, string[] Resources, WorkspaceBounds? Layout);

internal sealed class WorkspaceStore
{
    internal const int MaxProjects = 50, MaxNameLength = 60, MaxNotesLength = 20000, MaxApps = 32, MaxResources = 32;
    private const int MaxFileBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string path;
    private StoreData data = new();
    internal ProjectWorkspace[] Projects => data.Projects.Select(Copy).ToArray();
    internal string? ActiveProjectId => data.ActiveProjectId;
    internal bool ResumeOnStartup => data.ResumeOnStartup;
    internal string? LastSelectedAppKey => data.LastSelectedAppKey;
    internal WorkspaceBounds? LastLayout => data.LastLayout;
    internal string? Warning { get; private set; }

    internal WorkspaceStore(string path)
    {
        this.path = Path.GetFullPath(path);
        try
        {
            if (!File.Exists(this.path)) return;
            if (new FileInfo(this.path).Length > MaxFileBytes) throw new InvalidDataException("The projects file is too large.");
            using var document = JsonDocument.Parse(File.ReadAllText(this.path));
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("Version", out var version)
                || !version.TryGetInt32(out int number) || number != 1)
                throw new InvalidDataException("The projects file uses an unsupported version.");
            if (!document.RootElement.TryGetProperty("Projects", out var projects) || projects.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("The projects list is missing.");
            var loaded = document.RootElement.Deserialize<StoreData>(Json) ?? throw new InvalidDataException("The projects file is empty.");
            if (loaded.Projects is null || loaded.Projects.Length > MaxProjects) throw new InvalidDataException("The projects list is invalid.");
            loaded.Projects = loaded.Projects.Select(p => Normalize(p, true)).ToArray();
            if (loaded.Projects.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != loaded.Projects.Length
                || loaded.Projects.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != loaded.Projects.Length)
                throw new InvalidDataException("The projects file contains duplicate projects.");
            if (loaded.ActiveProjectId is not null && !loaded.Projects.Any(p => p.Id == loaded.ActiveProjectId))
                throw new InvalidDataException("The active project is missing from the projects file.");
            ValidateKey(loaded.LastSelectedAppKey);
            ValidateLayout(loaded.LastLayout);
            data = loaded;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            Warning = "Saved projects could not be read. The original file is preserved and saving is disabled. Restore or rename " + Path.GetFileName(this.path) + ", then restart AI Hub.";
        }
    }

    internal ProjectWorkspace SaveProject(ProjectWorkspace project)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(project);
        var previous = data.Projects.FirstOrDefault(p => p.Id == project.Id);
        var nextProject = Normalize(project, false, previous);
        if (data.Projects.Any(p => p.Id != nextProject.Id && string.Equals(p.Name, nextProject.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A project with that name already exists. Choose another name.");
        if (previous is null && data.Projects.Length >= MaxProjects)
            throw new InvalidOperationException($"Keep up to {MaxProjects} projects. Delete a project before adding another.");
        var projects = previous is null ? data.Projects.Append(nextProject).ToArray()
            : data.Projects.Select(p => p.Id == nextProject.Id ? nextProject : p).ToArray();
        Save(data with { Projects = projects });
        return Copy(nextProject);
    }

    internal void RemoveProject(string id)
    {
        EnsureWritable();
        if (!data.Projects.Any(p => p.Id == id)) return;
        Save(data with { Projects = data.Projects.Where(p => p.Id != id).ToArray(),
            ActiveProjectId = data.ActiveProjectId == id ? null : data.ActiveProjectId });
    }

    internal void SaveSession(string? activeProjectId, string? selectedKey, WorkspaceBounds? layout)
    {
        EnsureWritable();
        if (activeProjectId is not null && !data.Projects.Any(p => p.Id == activeProjectId))
            throw new InvalidOperationException("That project is no longer saved.");
        ValidateKey(selectedKey); ValidateLayout(layout);
        var projects = data.Projects.Select(p => p.Id == activeProjectId
            ? p with { SelectedAppKey = selectedKey is not null && p.AppKeys.Contains(selectedKey) ? selectedKey : null, Layout = layout }
            : p).ToArray();
        Save(data with { ActiveProjectId = activeProjectId, LastSelectedAppKey = selectedKey, LastLayout = layout, Projects = projects });
    }

    internal void SetResumeOnStartup(bool enabled)
    {
        EnsureWritable();
        if (data.ResumeOnStartup != enabled) Save(data with { ResumeOnStartup = enabled });
    }

    private static ProjectWorkspace Copy(ProjectWorkspace project) => project with { AppKeys = [.. project.AppKeys], Resources = [.. project.Resources] };
    private static ProjectWorkspace Normalize(ProjectWorkspace project, bool loading, ProjectWorkspace? previous = null)
    {
        if (project is null) throw new InvalidDataException("A saved project is missing.");
        var id = project.Id;
        if (string.IsNullOrEmpty(id) && !loading) id = Guid.NewGuid().ToString("N");
        if (!Guid.TryParse(id, out _)) throw new InvalidOperationException("The project identity is invalid.");
        string name = project.Name?.Trim() ?? "";
        if (name.Length is < 1 or > MaxNameLength || name.Any(char.IsControl))
            throw new InvalidOperationException($"Choose a project name between 1 and {MaxNameLength} characters.");
        if (project.AppKeys is null || project.AppKeys.Length > MaxApps)
            throw new InvalidOperationException($"Choose up to {MaxApps} AI apps for a project.");
        foreach (var key in project.AppKeys) { if (key is null) throw new InvalidOperationException("An AI app identity is missing."); ValidateKey(key); }
        var keys = project.AppKeys.Distinct(StringComparer.Ordinal).ToArray();
        string notes = project.Notes ?? "";
        if (notes.Length > MaxNotesLength) throw new InvalidOperationException($"Project notes can contain up to {MaxNotesLength:N0} characters.");
        if (project.Resources is null || project.Resources.Length > MaxResources)
            throw new InvalidOperationException($"Keep up to {MaxResources} links or folders in a project.");
        var resources = project.Resources.Select(resource => NormalizeResource(resource, loading, previous)).Distinct(StringComparer.Ordinal).ToArray();
        ValidateKey(project.SelectedAppKey); ValidateLayout(project.Layout);
        return new(id!, name, keys, project.SelectedAppKey is { } selected && keys.Contains(selected) ? selected : null, notes, resources, project.Layout);
    }

    private static string NormalizeResource(string resource, bool loading, ProjectWorkspace? previous)
    {
        resource = resource?.Trim() ?? "";
        if (resource.Length is < 1 or > 2048 || resource.Any(char.IsControl))
            throw new InvalidOperationException("Each resource must be a link or folder path of up to 2,048 characters.");
        if (Uri.TryCreate(resource, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && !string.IsNullOrEmpty(uri.Host)) return resource;
        if (Path.IsPathFullyQualified(resource) && (loading || previous?.Resources.Contains(resource) == true || Directory.Exists(resource))) return resource;
        throw new InvalidOperationException("Resources must be http or https links, or full paths to existing folders. Put one resource on each line.");
    }

    private static void ValidateKey(string? key)
    {
        if (key is not null && (key.Length is < 1 or > 128 || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.')))
            throw new InvalidOperationException("An AI app identity is invalid. Reconnect that app and try again.");
    }

    private static void ValidateLayout(WorkspaceBounds? layout)
    {
        if (layout is not null && (layout.Width is < 100 or > 32768 || layout.Height is < 100 or > 32768
            || layout.X is < -100000 or > 100000 || layout.Y is < -100000 or > 100000 || layout.Dpi is < 48 or > 960))
            throw new InvalidOperationException("The saved window layout is invalid.");
    }

    private void EnsureWritable()
    {
        if (Warning is not null) throw new InvalidOperationException(Warning);
    }

    private void Save(StoreData next)
    {
        EnsureWritable();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, next, Json);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
            data = next;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record StoreData
    {
        public StoreData() { }
        public int Version { get; init; } = 1;
        public ProjectWorkspace[] Projects { get; set; } = [];
        public string? ActiveProjectId { get; init; }
        public bool ResumeOnStartup { get; init; } = true;
        public string? LastSelectedAppKey { get; init; }
        public WorkspaceBounds? LastLayout { get; init; }
    }
}
