using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AIHub;

internal sealed class CopilotAgent : IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim promptGate = new(1, 1);
    private readonly object sync = new();
    private Process? process;
    private AcpClient? client;
    private Task? errorReader;
    private Task<JsonElement>? activePrompt;
    private CancellationTokenSource? permissionTurn;
    private Task? cancellationTask;
    private Task? disposalTask;
    private int disposed;
    internal event Action<JsonElement>? Updated;
    internal Func<JsonElement, CancellationToken, Task<string?>>? PermissionRequested { get; set; }
    internal string? SessionId { get; private set; }
    internal bool CanResume { get; private set; }

    internal CopilotAgent() { }
    // An existing transport can also be supplied by deterministic protocol checks.
    internal CopilotAgent(AcpClient connection, string sessionId)
    {
        client = connection; SessionId = sessionId;
        client.Notification += OnNotification; client.RequestReceived = OnRequestAsync;
    }

    internal async Task<JsonElement> StartAsync(string executable, string workingDirectory, string? sessionId,
        CancellationToken cancellation = default)
    {
        if (client is not null || Volatile.Read(ref disposed) != 0) throw new InvalidOperationException("Create a new Copilot connection before starting another session.");
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable)) throw new FileNotFoundException("Choose an installed Copilot executable.");
        if (!Path.IsPathFullyQualified(workingDirectory) || !Directory.Exists(workingDirectory)) throw new DirectoryNotFoundException("Choose an existing project folder before connecting to Copilot.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = Path.GetFullPath(workingDirectory)
        };
        start.ArgumentList.Add("--acp"); start.ArgumentList.Add("--stdio");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            lock (sync)
            {
                // A cancelled connection must not start a child after disposal
                // has already captured the resources it needs to retire.
                deadline.Token.ThrowIfCancellationRequested();
                if (disposed != 0) throw new ObjectDisposedException(nameof(CopilotAgent));
                process = Process.Start(start) ?? throw new IOException("Copilot could not start.");
                errorReader = DrainErrorAsync(process.StandardError, lifetime.Token);
                client = new AcpClient(process.StandardOutput, process.StandardInput);
                client.Notification += OnNotification;
                client.RequestReceived = OnRequestAsync;
            }
            var initialized = await client.CallAsync("initialize", new
            {
                protocolVersion = 1, clientCapabilities = new { },
                clientInfo = new { name = "ai-hub", title = "AI Hub", version = AppInfo.Version }
            }, deadline.Token).ConfigureAwait(false);
            if (!initialized.TryGetProperty("protocolVersion", out var protocol) || !protocol.TryGetInt32(out int selected) || selected != 1)
                throw new InvalidDataException("This Copilot version does not support the ACP version used by AI Hub. Update Copilot and try again.");
            CanResume = initialized.TryGetProperty("agentCapabilities", out var capabilities)
                && capabilities.TryGetProperty("loadSession", out var load) && load.ValueKind == JsonValueKind.True;
            JsonElement session;
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                if (!CanResume) throw new InvalidOperationException("This Copilot version cannot resume saved chats. Update it or start a new chat; the existing conversation is retained.");
                SessionId = sessionId;
                session = await client.CallAsync("session/load", new { sessionId, cwd = start.WorkingDirectory, mcpServers = Array.Empty<object>() }, deadline.Token).ConfigureAwait(false);
            }
            else
            {
                session = await client.CallAsync("session/new", new { cwd = start.WorkingDirectory, mcpServers = Array.Empty<object>() }, deadline.Token).ConfigureAwait(false);
                if (!session.TryGetProperty("sessionId", out var id) || id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()))
                    throw new InvalidDataException("Copilot did not create a session.");
                SessionId = id.GetString();
            }
            return session;
        }
        catch { await DisposeAsync().ConfigureAwait(false); throw; }
    }

    internal async Task<JsonElement> PromptAsync(string text, CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Enter a message for Copilot.", nameof(text));
        await promptGate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var connection = RequireConnection();
            cancellation.ThrowIfCancellationRequested();
            lock (sync)
            {
                cancellationTask = null;
                permissionTurn = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                activePrompt = connection.CallAsync("session/prompt", new
                {
                    sessionId = SessionId, prompt = new[] { new { type = "text", text } }
                }, lifetime.Token);
            }
            using var stop = cancellation.Register(() => _ = ObserveCancellationAsync());
            var response = await activePrompt.ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            return response;
        }
        finally
        {
            lock (sync)
            {
                activePrompt = null;
                permissionTurn?.Cancel(); permissionTurn?.Dispose(); permissionTurn = null;
            }
            promptGate.Release();
        }
    }

    internal async Task<JsonElement> SetConfigAsync(string configId, string value, CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(configId)) throw new ArgumentException("Choose a session option.", nameof(configId));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        return await RequireConnection().CallAsync("session/set_config_option", new { sessionId = SessionId, configId, value }, deadline.Token).ConfigureAwait(false);
    }
    internal async Task SetModelAsync(string modelId, CancellationToken cancellation = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        await RequireConnection().CallAsync("session/set_model", new { sessionId = SessionId, modelId }, deadline.Token).ConfigureAwait(false);
    }
    internal Task CancelAsync()
    {
        lock (sync) return cancellationTask ??= CancelCoreAsync(activePrompt, permissionTurn);
    }
    private async Task ObserveCancellationAsync() { try { await CancelAsync().ConfigureAwait(false); } catch (Exception) { } }
    private async Task CancelCoreAsync(Task<JsonElement>? prompt, CancellationTokenSource? permissions)
    {
        permissions?.Cancel();
        if (client is null || SessionId is null || Volatile.Read(ref disposed) != 0) return;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await client.NotifyAsync("session/cancel", new { sessionId = SessionId }, deadline.Token).ConfigureAwait(false);
            if (prompt is not null) await prompt.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            // Only the process created by this connection is stopped. Never use
            // process names or the external-window docking machinery to stop it.
            await DisposeAsync().ConfigureAwait(false);
        }
    }
    private AcpClient RequireConnection()
    {
        if (client is null || SessionId is null || Volatile.Read(ref disposed) != 0) throw new InvalidOperationException("Connect to Copilot before sending a message.");
        return client;
    }
    private void OnNotification(string method, JsonElement parameters)
    {
        if (method != "session/update") return;
        if (SessionId is not null && parameters.TryGetProperty("sessionId", out var id) && id.GetString() != SessionId) return;
        Updated?.Invoke(parameters);
    }
    private async Task<JsonElement> OnRequestAsync(string method, JsonElement parameters, CancellationToken cancellation)
    {
        if (method != "session/request_permission") throw new AcpRpcException(-32601, null);
        if (SessionId is null || !parameters.TryGetProperty("sessionId", out var id) || id.GetString() != SessionId)
            return CancelledPermission();
        CancellationToken turn;
        lock (sync) turn = permissionTurn?.Token ?? cancellation;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation, turn);
        string? selected = null;
        try
        {
            if (PermissionRequested is { } request) selected = await request(parameters, deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
        if (deadline.IsCancellationRequested || selected is null || !parameters.TryGetProperty("options", out var options)
            || options.ValueKind != JsonValueKind.Array || !options.EnumerateArray().Any(option => option.TryGetProperty("optionId", out var value)
                && value.ValueKind == JsonValueKind.String && value.GetString() == selected)) return CancelledPermission();
        return JsonSerializer.SerializeToElement(new { outcome = new { outcome = "selected", optionId = selected } });
    }
    private static JsonElement CancelledPermission() => JsonSerializer.SerializeToElement(new { outcome = new { outcome = "cancelled" } });
    private static async Task DrainErrorAsync(TextReader error, CancellationToken cancellation)
    {
        var buffer = new char[4096];
        try { while (await error.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false) > 0) { } }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
    }
    public ValueTask DisposeAsync()
    {
        lock (sync)
        {
            if (disposalTask is not null) return new ValueTask(disposalTask);
            Volatile.Write(ref disposed, 1);
            // Share the retirement task so Stop and page disposal both wait for
            // the same child process, including when connection setup is racing.
            disposalTask = Task.Run(DisposeCoreAsync);
            return new ValueTask(disposalTask);
        }
    }
    private async Task DisposeCoreAsync()
    {
        lifetime.Cancel();
        if (client is not null) await client.DisposeAsync().ConfigureAwait(false);
        if (process is { } owned)
        {
            try { owned.StandardInput.BaseStream.Close(); } catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException) { }
            try
            {
                if (!owned.HasExited)
                {
                    try { await owned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
                    catch (TimeoutException)
                    {
                        if (!owned.HasExited) owned.Kill(entireProcessTree: true);
                        await owned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException) { }
            owned.Dispose();
        }
        if (errorReader is not null)
        {
            try { await errorReader.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
            catch (TimeoutException) { }
        }
    }
}
