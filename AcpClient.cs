using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace AIHub;

internal sealed class AcpRpcException : IOException
{
    internal int Code { get; }
    internal AcpRpcException(int code, string? message) : base(Describe(code, message)) => Code = code;
    private static string Describe(int code, string? message)
    {
        // Do not put arbitrary provider error data, paths or credentials in diagnostics.
        string lower = (message ?? "").ToLowerInvariant();
        if (lower.Contains("auth") || lower.Contains("login") || lower.Contains("sign in"))
            return "Copilot requires GitHub sign-in. Use Sign in, then connect again.";
        if (lower.Contains("model")) return "Copilot could not use that model. Refresh the available models and try again.";
        if (lower.Contains("session")) return "Copilot could not open that saved session. Start a new chat to continue; your saved conversation is retained.";
        if (code == -32601) return "This Copilot version does not support the requested feature. Update Copilot and reconnect.";
        return $"Copilot rejected the request (code {code}). Reconnect or update Copilot and try again.";
    }
}

// JSON-RPC over bounded newline-delimited UTF-8 text. The owner supplies and
// disposes the streams. Inbound requests run independently from the read loop:
// an approval dialog must never block cancellation, output or another response.
internal sealed class AcpClient : IAsyncDisposable
{
    internal const int MaxMessageCharacters = 4 * 1024 * 1024;
    private readonly TextReader input;
    private readonly TextWriter output;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending = new();
    private readonly ConcurrentDictionary<long, Task> handlers = new();
    private long nextId, nextHandler;
    private int failed, disposed;
    private readonly Task reader;
    internal event Action<string, JsonElement>? Notification;
    internal Func<string, JsonElement, CancellationToken, Task<JsonElement>>? RequestReceived { get; set; }
    internal int PendingCount => pending.Count;

    internal AcpClient(TextReader input, TextWriter output)
    {
        this.input = input; this.output = output;
        reader = Task.Run(ReadAsync);
    }

    internal async Task<JsonElement> CallAsync(string method, object parameters, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        ThrowIfClosed();
        long id = Interlocked.Increment(ref nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pending.TryAdd(id, completion)) throw new InvalidOperationException("Duplicate ACP request.");
        try
        {
            await WriteAsync(new { jsonrpc = "2.0", id, method, @params = parameters }, cancellation).ConfigureAwait(false);
            return await completion.Task.WaitAsync(cancellation).ConfigureAwait(false);
        }
        finally { pending.TryRemove(id, out _); }
    }

    internal Task NotifyAsync(string method, object parameters, CancellationToken cancellation = default)
        => WriteAsync(new { jsonrpc = "2.0", method, @params = parameters }, cancellation);

    private async Task WriteAsync(object message, CancellationToken cancellation)
    {
        ThrowIfClosed();
        string text = JsonSerializer.Serialize(message);
        if (text.Length > MaxMessageCharacters) throw new InvalidDataException("The Copilot message is too large.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        bool entered = false;
        try
        {
            await writeGate.WaitAsync(deadline.Token).ConfigureAwait(false); entered = true;
            ThrowIfClosed();
            await output.WriteAsync((text + "\n").AsMemory(), deadline.Token).ConfigureAwait(false);
            await output.FlushAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // An interrupted write may leave a partial JSON record. Retire this
            // connection rather than reusing a potentially corrupted stream.
            if (entered) Fail(new IOException("The Copilot connection stopped while sending a request."));
            throw;
        }
        finally { if (entered) writeGate.Release(); }
    }

    private async Task ReadAsync()
    {
        var chunk = new char[4096];
        var line = new StringBuilder();
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                int count = await input.ReadAsync(chunk.AsMemory(), lifetime.Token).ConfigureAwait(false);
                if (count == 0) throw new IOException("Copilot disconnected. Reconnect to continue the saved chat.");
                for (int index = 0; index < count; index++)
                {
                    char c = chunk[index];
                    if (c == '\n')
                    {
                        if (line.Length > 0)
                        {
                            string message = line.ToString(); line.Clear();
                            if (!string.IsNullOrWhiteSpace(message)) Dispatch(message);
                        }
                    }
                    else
                    {
                        if (line.Length >= MaxMessageCharacters) throw new InvalidDataException("Copilot sent an oversized protocol message.");
                        line.Append(c);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception)
        { Fail(new IOException("Copilot disconnected or sent an invalid protocol message. Reconnect to continue.")); }
        finally { Fail(new IOException("The Copilot connection is closed.")); }
    }

    private void Dispatch(string message)
    {
        using var document = JsonDocument.Parse(message, new JsonDocumentOptions { MaxDepth = 64 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("jsonrpc", out var version)
            || version.ValueKind != JsonValueKind.String || version.GetString() != "2.0")
            throw new InvalidDataException("Invalid ACP envelope.");
        bool hasId = root.TryGetProperty("id", out var id);
        if (root.TryGetProperty("method", out var methodElement))
        {
            if (methodElement.ValueKind != JsonValueKind.String) throw new InvalidDataException("Invalid ACP method.");
            string method = methodElement.GetString()!;
            JsonElement parameters = root.TryGetProperty("params", out var p) ? p.Clone() : JsonSerializer.SerializeToElement(new { });
            if (!hasId) { Notification?.Invoke(method, parameters); return; }
            if (id.ValueKind is not (JsonValueKind.Number or JsonValueKind.String)) throw new InvalidDataException("Invalid ACP request ID.");
            if (handlers.Count >= 32) throw new InvalidDataException("Too many pending Copilot requests.");
            long handlerId = Interlocked.Increment(ref nextHandler);
            var requestId = id.Clone();
            Task task = Task.Run(() => HandleRequestAsync(requestId, method, parameters));
            handlers[handlerId] = task;
            _ = task.ContinueWith(_ => handlers.TryRemove(handlerId, out var ignored), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return;
        }
        // Our outbound IDs are integers. A response with a string ID must not
        // accidentally match an integer request with the same printed value.
        if (!hasId || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out long number)
            || !pending.TryGetValue(number, out var completion)) return;
        bool hasResult = root.TryGetProperty("result", out var result), hasError = root.TryGetProperty("error", out var error);
        if (hasResult == hasError) throw new InvalidDataException("Invalid ACP response.");
        if (hasError)
        {
            int code = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var codeValue)
                && codeValue.TryGetInt32(out int parsed) ? parsed : -32603;
            string? text = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var errorMessage)
                && errorMessage.ValueKind == JsonValueKind.String ? errorMessage.GetString() : null;
            completion.TrySetException(new AcpRpcException(code, text));
        }
        else completion.TrySetResult(result.Clone());
    }

    private async Task HandleRequestAsync(JsonElement id, string method, JsonElement parameters)
    {
        try
        {
            if (RequestReceived is null)
            {
                await WriteAsync(new { jsonrpc = "2.0", id, error = new { code = -32601, message = "Unsupported client method." } }, lifetime.Token).ConfigureAwait(false);
                return;
            }
            JsonElement result = await RequestReceived(method, parameters, lifetime.Token).WaitAsync(lifetime.Token).ConfigureAwait(false);
            await WriteAsync(new { jsonrpc = "2.0", id, result }, lifetime.Token).ConfigureAwait(false);
        }
        catch (AcpRpcException ex)
        {
            try { await WriteAsync(new { jsonrpc = "2.0", id, error = new { code = ex.Code, message = "Unsupported client request." } }, lifetime.Token).ConfigureAwait(false); }
            catch (Exception) when (lifetime.IsCancellationRequested) { }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            // Never leave a provider waiting for a UI callback that failed.
            try { await WriteAsync(new { jsonrpc = "2.0", id, error = new { code = -32603, message = "Client request failed." } }, lifetime.Token).ConfigureAwait(false); }
            catch (Exception) { Fail(new IOException("The Copilot connection is closed.")); }
        }
    }

    private void ThrowIfClosed()
    {
        if (Volatile.Read(ref failed) != 0 || Volatile.Read(ref disposed) != 0) throw new IOException("The Copilot connection is closed. Reconnect to continue.");
    }
    private void Fail(Exception error)
    {
        if (Interlocked.Exchange(ref failed, 1) != 0) return;
        lifetime.Cancel();
        foreach (var item in pending) item.Value.TrySetException(error);
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Fail(new IOException("The Copilot connection was closed."));
        try { await reader.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (TimeoutException) { }
        // The lifetime token remains valid for any late callback owned by the UI.
    }
}
