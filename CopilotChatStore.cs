using System.Text;
using System.Text.Json;

namespace AIHub;

internal sealed record CopilotChatMessage(string Role, string Text, DateTimeOffset At);
internal sealed record CopilotChat(string Id, string WorkspaceKey, string Name, string Folder,
    string? SessionId, string? ModelId, DateTimeOffset Updated, CopilotChatMessage[] Messages);

internal sealed class CopilotChatStore
{
    internal const int MaxChats = 200, MaxMessages = 500, MaxMessageChars = 200000, MaxChatChars = 1000000;
    private const int MaxBytes = 16 * 1024 * 1024;
    private readonly string path;
    private ChatData data = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    internal string? Warning { get; private set; }

    internal CopilotChatStore(string path)
    {
        this.path = Path.GetFullPath(path);
        try
        {
            if (!File.Exists(this.path)) return;
            if (new FileInfo(this.path).Length > MaxBytes) throw new InvalidDataException("Chat history is too large.");
            string source = File.ReadAllText(this.path);
            using var document = JsonDocument.Parse(source);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Version", out var version)
                || !version.TryGetInt32(out int format) || format != 1
                || !root.TryGetProperty("Chats", out var chats) || chats.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Chat history has an unsupported format.");
            var loaded = JsonSerializer.Deserialize<ChatData>(source, Json)
                ?? throw new InvalidDataException("Chat history is empty.");
            if (loaded.Version != 1 || loaded.Chats is null || loaded.Chats.Length > MaxChats)
                throw new InvalidDataException("Chat history has an unsupported format.");
            loaded = loaded with { Chats = loaded.Chats.Select(Validate).ToArray() };
            if (loaded.Chats.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != loaded.Chats.Length)
                throw new InvalidDataException("Chat history contains duplicate identities.");
            data = loaded;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        {
            Warning = "Copilot chat history could not be read. The original file is preserved and saving is disabled. Restore or rename "
                + Path.GetFileName(this.path) + ", then restart AI Hub.";
        }
    }

    internal CopilotChat[] ForWorkspace(string key)
    {
        ValidateWorkspace(key);
        return data.Chats.Where(c => c.WorkspaceKey == key).OrderByDescending(c => c.Updated).Select(Copy).ToArray();
    }

    internal CopilotChat Save(CopilotChat chat)
    {
        if (Warning is not null) throw new InvalidOperationException(Warning);
        var next = Validate(chat);
        var previous = data.Chats.FirstOrDefault(c => c.Id == next.Id);
        if (previous is not null && (previous.WorkspaceKey != next.WorkspaceKey
            || !StringComparer.OrdinalIgnoreCase.Equals(previous.Folder, next.Folder)))
            throw new InvalidOperationException("Create a new chat to change its project or working folder.");
        if (previous is null && data.Chats.Length >= MaxChats)
            throw new InvalidOperationException("Copilot chat history is full. Archive its file before starting more chats.");
        var proposed = data with { Chats = previous is null ? [.. data.Chats, next]
            : data.Chats.Select(c => c.Id == next.Id ? next : c).ToArray() };
        Commit(proposed);
        return Copy(next);
    }

    internal void ArchiveWorkspace(string key, string name)
    {
        ValidateWorkspace(key);
        if (key.Equals("personal", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The personal workspace cannot be archived.", nameof(key));
        if (Warning is not null) throw new InvalidOperationException(Warning);
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter the archived project's name.", nameof(name));
        if (!data.Chats.Any(chat => chat.WorkspaceKey == key)) return;
        string projectName = BoundedName(new string(name.Trim().Take(60).Select(c => char.IsControl(c) ? ' ' : c).ToArray()), 60).Trim();
        if (projectName.Length == 0) throw new ArgumentException("Enter the archived project's name.", nameof(name));
        // This explicit archive transition is the only operation allowed to
        // rehome a saved identity. Ordinary Save still rejects workspace/folder changes.
        var proposed = data with
        {
            Chats = data.Chats.Select(chat => chat.WorkspaceKey == key
                ? Validate(chat with { WorkspaceKey = "personal", Name = BoundedName($"[{projectName}] {chat.Name}", 96) })
                : chat).ToArray()
        };
        Commit(proposed);
    }

    internal void ArchiveMissingWorkspaces(IEnumerable<string> knownKeys)
    {
        ArgumentNullException.ThrowIfNull(knownKeys);
        if (Warning is not null) throw new InvalidOperationException(Warning);
        // Validate a stable snapshot before making any change. The workspace
        // registry remains authoritative; callers must not use a damaged registry.
        var keys = knownKeys.ToArray();
        foreach (var key in keys) ValidateWorkspace(key);
        var known = keys.ToHashSet(StringComparer.Ordinal);
        var missing = data.Chats.Select(chat => chat.WorkspaceKey).Distinct(StringComparer.Ordinal)
            .Where(key => !key.Equals("personal", StringComparison.OrdinalIgnoreCase) && !known.Contains(key)).ToArray();
        foreach (var key in missing) ArchiveWorkspace(key, "Archived project");
    }

    private static string BoundedName(string value, int maximum)
    {
        if (value.Length > maximum) value = value[..maximum];
        return value.Length > 0 && char.IsHighSurrogate(value[^1]) ? value[..^1] : value;
    }

    private void Commit(ChatData proposed)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(proposed, Json);
        if (bytes.Length > MaxBytes) throw new InvalidOperationException("Copilot chat history is full. Archive its file before continuing.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string prepared = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(prepared, bytes); LocalFile.Replace(prepared, path); data = proposed; }
        finally { try { if (File.Exists(prepared)) File.Delete(prepared); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
    }

    internal static void ValidateWorkspace(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || key.Any(char.IsControl))
            throw new ArgumentException("Choose a valid workspace.");
    }

    private static CopilotChat Validate(CopilotChat chat)
    {
        ArgumentNullException.ThrowIfNull(chat);
        ValidateWorkspace(chat.WorkspaceKey);
        if (!Guid.TryParseExact(chat.Id, "N", out _) || string.IsNullOrWhiteSpace(chat.Name) || chat.Name.Length > 96
            || chat.Name.Any(char.IsControl) || string.IsNullOrWhiteSpace(chat.Folder) || !Path.IsPathFullyQualified(chat.Folder)
            || chat.SessionId?.Length > 512 || chat.ModelId?.Length > 8192 || chat.Messages is null || chat.Messages.Length > MaxMessages)
            throw new InvalidDataException("A saved Copilot chat is invalid or too large.");
        long length = 0;
        foreach (var message in chat.Messages)
        {
            if (message is null || message.Role is not ("user" or "assistant" or "tool" or "notice")
                || message.Text is null || message.Text.Length > MaxMessageChars)
                throw new InvalidDataException("A Copilot message is invalid or too large.");
            length += message.Text.Length;
        }
        if (length > MaxChatChars) throw new InvalidDataException("This Copilot chat is full. Start a new chat.");
        return Copy(chat with { Folder = Path.GetFullPath(chat.Folder) });
    }

    private static CopilotChat Copy(CopilotChat chat) => chat with { Messages = [.. chat.Messages] };
    private sealed record ChatData
    {
        public int Version { get; init; } = 1;
        public CopilotChat[] Chats { get; init; } = [];
    }
}

internal sealed record CopilotModelOption(string Id, string Name, string? ConfigId)
{
    public override string ToString() => Name;
}
internal sealed record CopilotModelChoices(CopilotModelOption[] Options, string? CurrentId);

internal static class CopilotDisplay
{
    internal static string? String(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    internal static JsonElement Property(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(key, out var item) ? item : default;

    internal static CopilotModelChoices Models(JsonElement setup)
    {
        List<CopilotModelOption> options = [];
        string? current = null;
        var configs = Property(setup, "configOptions");
        if (configs.ValueKind == JsonValueKind.Array)
        {
            foreach (var config in configs.EnumerateArray())
            {
                if (String(config, "category") != "model" && String(config, "id") != "model") continue;
                if (String(config, "type") is { } type && type != "select") continue;
                string? id = String(config, "id");
                if (string.IsNullOrEmpty(id)) continue;
                current = String(config, "currentValue");
                AddOptions(Property(config, "options"), id, options, 0);
                if (options.Count > 0) break;
            }
        }
        if (options.Count == 0)
        {
            var models = Property(setup, "models");
            current = String(models, "currentModelId");
            var available = Property(models, "availableModels");
            if (available.ValueKind == JsonValueKind.Array)
                foreach (var model in available.EnumerateArray().Take(500))
                    if (String(model, "modelId") is { Length: > 0 } id)
                        options.Add(new(id, String(model, "name") ?? id, null));
        }
        return new(options.DistinctBy(o => o.Id).ToArray(), current);
    }

    private static void AddOptions(JsonElement items, string configId, List<CopilotModelOption> result, int depth)
    {
        if (items.ValueKind != JsonValueKind.Array || depth > 8) return;
        foreach (var item in items.EnumerateArray())
        {
            if (result.Count >= 500) return;
            if (String(item, "value") is { Length: > 0 } value)
                result.Add(new(value, String(item, "name") ?? value, configId));
            else AddOptions(Property(item, "options"), configId, result, depth + 1);
        }
    }

    internal static string ToolDetails(JsonElement tool)
    {
        var output = new StringBuilder();
        output.AppendLine(String(tool, "title") ?? "Copilot tool");
        if (String(tool, "status") is { } status) output.AppendLine("Status: " + status);
        foreach (string name in new[] { "kind", "locations", "rawInput", "rawOutput" })
        {
            var value = Property(tool, name);
            if (value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
                output.AppendLine(name + ": " + JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        }
        var content = Property(tool, "content");
        if (content.ValueKind == JsonValueKind.Array)
            foreach (var item in content.EnumerateArray())
            {
                if (String(item, "type") == "diff")
                    output.AppendLine("File: " + String(item, "path") + "\nBefore:\n" + String(item, "oldText") + "\nAfter:\n" + String(item, "newText"));
                else if (String(Property(item, "content"), "text") is { } text) output.AppendLine(text);
                else output.AppendLine(JsonSerializer.Serialize(item, new JsonSerializerOptions { WriteIndented = true }));
            }
        return output.ToString().TrimEnd();
    }

    internal static string? Usage(JsonElement update)
    {
        var used = Property(update, "used"); var size = Property(update, "size");
        if (used.ValueKind != JsonValueKind.Number || !used.TryGetInt64(out long count) || count < 0) return null;
        return size.ValueKind == JsonValueKind.Number && size.TryGetInt64(out long capacity) && capacity > 0 && count <= capacity
            ? $"Context {count:N0} / {capacity:N0} tokens" : $"Context {count:N0} tokens";
    }

    internal static Dictionary<string, JsonElement> MergeTool(IReadOnlyDictionary<string, JsonElement>? previous, JsonElement update)
    {
        var merged = previous?.ToDictionary(pair => pair.Key, pair => pair.Value.Clone()) ?? [];
        if (update.ValueKind == JsonValueKind.Object)
            foreach (var property in update.EnumerateObject())
                if (property.Value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null)) merged[property.Name] = property.Value.Clone();
        return merged;
    }
}

// session/load may stream a complete transcript before it succeeds. Keep that
// candidate separate until the load and durable save both succeed.
internal sealed class CopilotReplayBuffer
{
    private readonly object sync = new();
    private readonly List<CopilotChatMessage> messages = [];
    private readonly Dictionary<string, (int Index, Dictionary<string, JsonElement> Fields)> tools = [];
    private Exception? error;
    internal void Accept(JsonElement update)
    {
        lock (sync)
        {
            if (error is not null) return;
            try
            {
                string? type = CopilotDisplay.String(update, "sessionUpdate");
                if (type is "user_message_chunk" or "agent_message_chunk")
                {
                    string? text = CopilotDisplay.String(CopilotDisplay.Property(update, "content"), "text");
                    if (text is null) return;
                    string role = type == "user_message_chunk" ? "user" : "assistant";
                    if (role == "assistant" && messages.Count > 0 && messages[^1].Role == role) messages[^1] = messages[^1] with { Text = messages[^1].Text + text };
                    else messages.Add(new(role, text, DateTimeOffset.UtcNow));
                }
                else if (type is "tool_call" or "tool_call_update")
                {
                    string? id = CopilotDisplay.String(update, "toolCallId"); if (id is null) return;
                    bool exists = tools.TryGetValue(id, out var old);
                    var fields = CopilotDisplay.MergeTool(exists ? old.Fields : null, update);
                    var message = new CopilotChatMessage("tool", CopilotDisplay.ToolDetails(JsonSerializer.SerializeToElement(fields)), DateTimeOffset.UtcNow);
                    int index = exists ? old.Index : messages.Count;
                    if (exists) messages[index] = message; else messages.Add(message);
                    tools[id] = (index, fields);
                }
                if (messages.Count > CopilotChatStore.MaxMessages || messages.Any(m => m.Text.Length > CopilotChatStore.MaxMessageChars)
                    || messages.Sum(m => (long)m.Text.Length) > CopilotChatStore.MaxChatChars)
                    throw new InvalidDataException("The saved Copilot session is too large to load here. Its previous local history is retained.");
            }
            catch (Exception ex) { error = ex; }
        }
    }
    internal CopilotChatMessage[] Complete(CopilotChatMessage[] previous)
    {
        lock (sync)
        {
            if (error is not null) throw new InvalidDataException("Copilot could not replay this session safely. Its previous local history is retained.", error);
            return messages.Count == 0 ? [.. previous] : [.. messages];
        }
    }
    internal Dictionary<string, (int Index, Dictionary<string, JsonElement> Fields)> ToolSnapshot()
    {
        lock (sync) return tools.ToDictionary(pair => pair.Key,
            pair => (pair.Value.Index, pair.Value.Fields.ToDictionary(field => field.Key, field => field.Value.Clone())));
    }
    internal bool HasMessages { get { lock (sync) return messages.Count > 0; } }
}
