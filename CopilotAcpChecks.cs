using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace AIHub;

internal static class CopilotAcpChecks
{
    internal static async Task Run(List<string> lines)
    {
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            lines.Add("PASS " + label);
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = timeout.Token;
        await using (var wire = new Wire())
        {
            var first = wire.Client.CallAsync("first", new { }, ct);
            var second = wire.Client.CallAsync("second", new { }, ct);
            var sentFirst = await wire.NextAsync(ct); var sentSecond = await wire.NextAsync(ct);
            long id1 = sentFirst.GetProperty("id").GetInt64(), id2 = sentSecond.GetProperty("id").GetInt64();
            wire.Send(new { jsonrpc = "2.0", id = id1.ToString(), result = new { wrong = true } });
            wire.Send(new { jsonrpc = "2.0", id = id2, result = new { value = "second" } });
            Check((await second).GetProperty("value").GetString() == "second" && !first.IsCompleted,
                "ACP routes out-of-order replies by exact numeric request identity");
            wire.Send(new { jsonrpc = "2.0", id = id1, result = new { value = "first" } });
            Check((await first).GetProperty("value").GetString() == "first" && wire.Client.PendingCount == 0,
                "ACP removes completed requests and retains cloned response data");

            var observed = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var permission = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            wire.Client.Notification += (method, payload) => { if (method == "session/update") observed.TrySetResult(payload); };
            wire.Client.RequestReceived = async (_, _, cancellation) => { requested.TrySetResult(); return await permission.Task.WaitAsync(cancellation); };
            wire.Send(new { jsonrpc = "2.0", id = "approval-1", method = "session/request_permission", @params = new { sessionId = "s1" } });
            await requested.Task.WaitAsync(ct);
            var duringPermission = wire.Client.CallAsync("during-permission", new { }, ct);
            var sentDuring = await wire.NextAsync(ct);
            wire.Send(new { jsonrpc = "2.0", method = "session/update", @params = new { sessionId = "s1", update = new { sessionUpdate = "agent_message_chunk" } } });
            wire.Send(new { jsonrpc = "2.0", id = sentDuring.GetProperty("id").GetInt64(), result = new { ok = true } });
            Check((await duringPermission).GetProperty("ok").GetBoolean() && (await observed.Task.WaitAsync(ct)).GetProperty("sessionId").GetString() == "s1",
                "A pending ACP approval does not block streamed updates or other responses");
            permission.TrySetResult(JsonSerializer.SerializeToElement(new { outcome = new { outcome = "cancelled" } }));
            var permissionReply = await wire.NextAsync(ct);
            Check(permissionReply.GetProperty("id").GetString() == "approval-1" && permissionReply.GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString() == "cancelled",
                "ACP replies preserve the agent's string request ID and explicit refusal");

            wire.Client.RequestReceived = null;
            wire.Send(new { jsonrpc = "2.0", id = "unsupported", method = "fs/write_text_file", @params = new { path = "unadvertised" } });
            var unsupported = await wire.NextAsync(ct);
            Check(unsupported.GetProperty("error").GetProperty("code").GetInt32() == -32601,
                "Unadvertised ACP filesystem and terminal requests fail instead of executing locally");

            using var requestCancellation = new CancellationTokenSource();
            var abandoned = wire.Client.CallAsync("cancelled-request", new { }, requestCancellation.Token);
            var abandonedRequest = await wire.NextAsync(ct);
            requestCancellation.Cancel();
            bool wasCancelled = false;
            try { await abandoned; } catch (OperationCanceledException) { wasCancelled = true; }
            wire.Send(new { jsonrpc = "2.0", id = abandonedRequest.GetProperty("id").GetInt64(), result = new { ignored = true } });
            Check(wasCancelled && wire.Client.PendingCount == 0, "Cancelling an ACP request removes its waiter and ignores a late response");

            var errorRequest = wire.Client.CallAsync("failure", new { }, ct);
            var sentError = await wire.NextAsync(ct);
            wire.Send(new { jsonrpc = "2.0", id = sentError.GetProperty("id").GetInt64(), error = new { code = -32000, message = "sensitive-secret-token-data", data = "more-secret-data" } });
            bool sanitized = false;
            try { await errorRequest; } catch (AcpRpcException ex) { sanitized = ex.Code == -32000 && !ex.Message.Contains("secret"); }
            Check(sanitized, "ACP errors report their code without echoing provider credentials or arbitrary error data");
        }

        await using (var wire = new Wire())
        {
            await using var agent = new CopilotAgent(wire.Client, "session-one");
            object Permission(string session, string request) => new
            {
                jsonrpc = "2.0", id = request, method = "session/request_permission",
                @params = new { sessionId = session, toolCall = new { title = "Write file", kind = "edit" }, options = new[] { new { optionId = "once", name = "Allow once", kind = "allow_once" } } }
            };
            wire.Send(Permission("session-one", "no-handler"));
            var denied = await wire.NextAsync(ct);
            Check(IsCancelled(denied), "Copilot permission requests default to refusal without an approval UI");
            agent.PermissionRequested = (_, _) => Task.FromResult<string?>("unadvertised-choice");
            wire.Send(Permission("session-one", "invalid-choice"));
            Check(IsCancelled(await wire.NextAsync(ct)), "Copilot cannot approve an option absent from the agent's permission request");
            int approvalCalls = 0;
            agent.PermissionRequested = (_, _) => { approvalCalls++; return Task.FromResult<string?>("once"); };
            wire.Send(Permission("other-session", "foreign-session"));
            Check(IsCancelled(await wire.NextAsync(ct)) && approvalCalls == 0, "Permission requests for another Copilot session are rejected");
            wire.Send(Permission("session-one", "valid-choice"));
            Check((await wire.NextAsync(ct)).GetProperty("result").GetProperty("outcome").GetProperty("optionId").GetString() == "once",
                "Copilot forwards only the explicitly selected advertised permission option");

            var prompt = agent.PromptAsync("Check the code", ct);
            var promptRequest = await wire.NextAsync(ct);
            var cancel = agent.CancelAsync();
            var cancelNotice = await wire.NextAsync(ct);
            Check(cancelNotice.GetProperty("method").GetString() == "session/cancel"
                && cancelNotice.GetProperty("params").GetProperty("sessionId").GetString() == "session-one",
                "Stopping Copilot targets the active session through ACP cancellation");
            wire.Send(new { jsonrpc = "2.0", id = promptRequest.GetProperty("id").GetInt64(), result = new { stopReason = "cancelled" } });
            await cancel;
            Check((await prompt).GetProperty("stopReason").GetString() == "cancelled",
                "A cancelled Copilot turn completes before the connection accepts another prompt");
        }

        await using (var wire = new Wire())
        {
            await using var agent = new CopilotAgent(wire.Client, "unresponsive-session");
            var prompt = agent.PromptAsync("Wait for this fixture", ct);
            await wire.NextAsync(ct);
            await agent.CancelAsync(); // The fixture deliberately never acknowledges cancellation.
            bool stopped = false;
            try { await prompt; } catch (Exception ex) when (ex is IOException or OperationCanceledException) { stopped = true; }
            Check(stopped, "An unresponsive Copilot turn is retired after bounded cancellation instead of blocking shutdown");
        }
        await using (var wire = new Wire())
        {
            var pending = wire.Client.CallAsync("pending", new { }, ct); await wire.NextAsync(ct);
            wire.SendRaw("this is not JSON\n");
            bool failed = false;
            try { await pending; } catch (IOException) { failed = true; }
            Check(failed && wire.Client.PendingCount == 0, "Malformed ACP output fails outstanding requests without hanging");
        }
        await using (var wire = new Wire())
        {
            var pending = wire.Client.CallAsync("pending", new { }, ct); await wire.NextAsync(ct);
            wire.SendRaw(new string('x', AcpClient.MaxMessageCharacters + 1));
            bool failed = false;
            try { await pending; } catch (IOException) { failed = true; }
            Check(failed, "ACP rejects an oversized unterminated message with bounded buffering");
        }
        await using (var wire = new Wire())
        {
            var pending = wire.Client.CallAsync("pending", new { }, ct); await wire.NextAsync(ct);
            await wire.Client.DisposeAsync();
            bool failed = false;
            try { await pending; } catch (IOException) { failed = true; }
            Check(failed, "Closing the ACP transport releases pending requests immediately");
        }
    }

    private static bool IsCancelled(JsonElement response) => response.GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString() == "cancelled";
    private sealed class Wire : IAsyncDisposable
    {
        private readonly Channel<string> inbound = Channel.CreateUnbounded<string>();
        private readonly Channel<string> outbound = Channel.CreateUnbounded<string>();
        internal AcpClient Client { get; }
        internal Wire() => Client = new AcpClient(new ChannelReader(inbound.Reader), new ChannelWriter(outbound.Writer));
        internal void Send(object message) => SendRaw(JsonSerializer.Serialize(message) + "\n");
        internal void SendRaw(string text) => inbound.Writer.TryWrite(text);
        internal async Task<JsonElement> NextAsync(CancellationToken cancellation)
        {
            using var document = JsonDocument.Parse(await outbound.Reader.ReadAsync(cancellation));
            return document.RootElement.Clone();
        }
        public ValueTask DisposeAsync() { inbound.Writer.TryComplete(); return Client.DisposeAsync(); }
    }
    private sealed class ChannelReader(ChannelReader<string> channel) : TextReader
    {
        private string? buffered;
        private int offset;
        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            if (buffered is null || offset == buffered.Length)
            {
                if (!await channel.WaitToReadAsync(cancellationToken)) return 0;
                buffered = await channel.ReadAsync(cancellationToken); offset = 0;
            }
            int size = Math.Min(buffer.Length, buffered.Length - offset);
            buffered.AsSpan(offset, size).CopyTo(buffer.Span); offset += size;
            return size;
        }
    }
    private sealed class ChannelWriter(ChannelWriter<string> channel) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
            => channel.WriteAsync(buffer.ToString(), cancellationToken).AsTask();
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
