using System.Text.Json;

namespace AIHub;

internal static class CopilotChatChecks
{
    internal static void Run(string folder, List<string> lines)
    {
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); lines.Add("PASS " + label); }
        static bool Rejects(Action action)
        {
            try { action(); return false; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException) { return true; }
        }
        static void Finish(Task task)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(5)) { Application.DoEvents(); Thread.Sleep(1); }
            if (!task.IsCompleted) throw new TimeoutException("The isolated Copilot UI operation did not complete.");
            task.GetAwaiter().GetResult();
        }
        string root = Path.Combine(folder, "copilot-chat-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "chats.json");
        var store = new CopilotChatStore(path);
        Check(store.Warning is null && store.ForWorkspace("personal").Length == 0 && !File.Exists(path),
            "Copilot history discovery starts without creating files or connecting to an agent");
        var first = store.Save(new(Guid.NewGuid().ToString("N"), "project-one", "Reader repair", root,
            "native-session-one", "model-one", DateTimeOffset.UtcNow,
            [new("user", "Review the reader", DateTimeOffset.UtcNow), new("assistant", "I will inspect its entry point.", DateTimeOffset.UtcNow)]));
        var second = store.Save(new(Guid.NewGuid().ToString("N"), "project-two", "Unrelated work", root,
            "native-session-two", null, DateTimeOffset.UtcNow, []));
        var loaded = new CopilotChatStore(path);
        var resumed = loaded.ForWorkspace("project-one").Single();
        Check(resumed.SessionId == "native-session-one" && resumed.ModelId == "model-one" && resumed.Folder == root
            && resumed.Messages.SequenceEqual(first.Messages) && loaded.ForWorkspace("project-two").Single().Id == second.Id,
            "Copilot sessions, folders, model choices and transcripts persist independently per project");
        first.Messages[0] = new("user", "Changed outside the store", DateTimeOffset.UtcNow);
        resumed.Messages[0] = first.Messages[0];
        Check(store.ForWorkspace("project-one").Single().Messages[0].Text == "Review the reader"
            && loaded.ForWorkspace("project-one").Single().Messages[0].Text == "Review the reader",
            "Returned Copilot history snapshots cannot mutate stored messages");
        var current = loaded.ForWorkspace("project-one").Single();
        byte[] before = File.ReadAllBytes(path);
        Check(Rejects(() => loaded.Save(current with { WorkspaceKey = "project-two" }))
            && Rejects(() => loaded.Save(current with { Folder = Path.GetPathRoot(root)! }))
            && File.ReadAllBytes(path).SequenceEqual(before),
            "A Copilot session cannot silently move to a different project or working directory");
        Check(Rejects(() => loaded.Save(current with { Messages = [new("unknown", "bad role", DateTimeOffset.UtcNow)] }))
            && Rejects(() => loaded.Save(current with { Messages = [new("assistant", new string('x', CopilotChatStore.MaxMessageChars + 1), DateTimeOffset.UtcNow)] }))
            && Rejects(() => loaded.Save(current with { Messages = Enumerable.Repeat(new CopilotChatMessage("user", "x", DateTimeOffset.UtcNow), CopilotChatStore.MaxMessages + 1).ToArray() }))
            && File.ReadAllBytes(path).SequenceEqual(before),
            "Invalid and oversized Copilot messages leave the existing history unchanged");
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Check(Rejects(() => loaded.Save(current with { Name = "Blocked update" }))
                && loaded.ForWorkspace("project-one").Single().Name == "Reader repair",
                "A blocked atomic Copilot save preserves both persisted and in-memory conversation state");
        }
        Check(File.ReadAllBytes(path).SequenceEqual(before), "Failed Copilot saves retain the original file bytes");
        string offline = Path.Combine(root, "temporarily-unavailable");
        var unavailable = loaded.Save(new(Guid.NewGuid().ToString("N"), "offline", "Offline folder", offline, "offline-session", null, DateTimeOffset.UtcNow, []));
        Check(new CopilotChatStore(path).ForWorkspace("offline").Single().Folder == offline,
            "An unavailable saved project folder does not destroy its Copilot transcript or session identity");
        foreach (string malformed in new[] { "{invalid", "{}", "{\"Version\":2,\"Chats\":[]}", "{\"Version\":1,\"Chats\":null}" })
        {
            string badPath = Path.Combine(root, "malformed-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(badPath, malformed);
            var bad = new CopilotChatStore(badPath);
            Check(bad.Warning is not null && Rejects(() => bad.Save(current)) && File.ReadAllText(badPath) == malformed,
                "Malformed or unsupported Copilot history stays intact and disables overwriting");
        }
        var grouped = JsonSerializer.SerializeToElement(new
        {
            configOptions = new[] { new { id = "model-choice", category = "model", type = "select", currentValue = "opaque/model",
                options = new object[] { new { group = "Available", options = new[] { new { value = "opaque/model", name = "Model One" }, new { value = "{\"model\":\"two\"}", name = "Model Two" } } } } } },
            models = new { currentModelId = "ignored", availableModels = new[] { new { modelId = "ignored", name = "Legacy" } } }
        });
        var choices = CopilotDisplay.Models(grouped);
        Check(choices.Options.Length == 2 && choices.CurrentId == "opaque/model" && choices.Options.All(o => o.ConfigId == "model-choice")
            && choices.Options[1].Id == "{\"model\":\"two\"}",
            "Copilot model selection reads grouped advertised configuration and retains opaque model values");
        var legacy = CopilotDisplay.Models(JsonSerializer.SerializeToElement(new { models = new { currentModelId = "legacy-two",
            availableModels = new[] { new { modelId = "legacy-one", name = "First" }, new { modelId = "legacy-two", name = "Second" } } } }));
        Check(legacy.Options.Length == 2 && legacy.CurrentId == "legacy-two" && legacy.Options.All(o => o.ConfigId is null),
            "Copilot model selection supports legacy advertised models without inventing availability");
        Check(CopilotDisplay.Models(JsonSerializer.SerializeToElement(new { })).Options.Length == 0,
            "A missing Copilot model list remains unavailable rather than using hard-coded models");
        string detail = CopilotDisplay.ToolDetails(JsonSerializer.SerializeToElement(new
        {
            title = "Update reader", status = "pending", rawInput = new { command = "dotnet test", cwd = root },
            content = new[] { new { type = "diff", path = "reader.cs", oldText = "old code", newText = "new code" } }
        }));
        Check(detail.Contains("dotnet test") && detail.Contains("reader.cs") && detail.Contains("old code") && detail.Contains("new code"),
            "Copilot permission and tool details include commands, file paths and complete before/after diffs");
        Check(CopilotDisplay.Usage(JsonSerializer.SerializeToElement(new { used = 1234, size = 10000 })) is { } usage
            && usage.StartsWith("Context ") && usage.EndsWith(" tokens") && !usage.Contains('%')
            && CopilotDisplay.Usage(JsonSerializer.SerializeToElement(new { used = -1, size = 10000 })) is null,
            "Copilot context-token readings stay distinct from subscription quota and reject invalid counts");
        var earlier = new[] { new CopilotChatMessage("assistant", "Partial local answer", DateTimeOffset.UtcNow) };
        var replay = new CopilotReplayBuffer();
        replay.Accept(JsonSerializer.SerializeToElement(new { sessionUpdate = "user_message_chunk", content = new { type = "text", text = "Review this" } }));
        replay.Accept(JsonSerializer.SerializeToElement(new { sessionUpdate = "agent_message_chunk", content = new { type = "text", text = "Complete " } }));
        replay.Accept(JsonSerializer.SerializeToElement(new { sessionUpdate = "agent_message_chunk", content = new { type = "text", text = "answer" } }));
        Check(earlier.Single().Text == "Partial local answer" && replay.Complete(earlier) is { Length: 2 } replayed
            && replayed[0].Role == "user" && replayed[1].Text == "Complete answer",
            "Successful Copilot resume replaces partial local output with one complete streamed transcript");
        Check(new CopilotReplayBuffer().Complete(earlier).SequenceEqual(earlier),
            "A resume that provides no replay retains the existing local Copilot transcript");
        var tooLarge = new CopilotReplayBuffer();
        tooLarge.Accept(JsonSerializer.SerializeToElement(new { sessionUpdate = "agent_message_chunk", content = new { type = "text", text = new string('x', CopilotChatStore.MaxMessageChars + 1) } }));
        Check(Rejects(() => tooLarge.Complete(earlier)) && earlier.Single().Text == "Partial local answer",
            "A failed replay preserves the previous local conversation instead of committing partial replacements");
        var knownTool = CopilotDisplay.MergeTool(null, JsonSerializer.SerializeToElement(new { toolCallId = "edit", title = "Apply patch",
            rawInput = new { command = "dotnet test", cwd = root }, content = new[] { new { type = "diff", path = "reader.cs", oldText = "old", newText = "new" } } }));
        string mergedDetail = CopilotDisplay.ToolDetails(JsonSerializer.SerializeToElement(CopilotDisplay.MergeTool(knownTool,
            JsonSerializer.SerializeToElement(new { toolCallId = "edit", status = "pending", rawInput = (object?)null }))));
        Check(mergedDetail.Contains("dotnet test") && mergedDetail.Contains("reader.cs") && mergedDetail.Contains("Before:\nold")
            && mergedDetail.Contains("Status: pending"),
            "A partial permission request retains the earlier command, working folder and full diff for review");
        foreach (float scale in new[] { 1f, 1.5f })
        {
            using var host = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                Location = new Point(-30000, -30000), ClientSize = new Size((int)(900 * scale), (int)(550 * scale)) };
            string layoutRoot = Path.Combine(root, "layout-" + scale);
            new CopilotChatStore(Path.Combine(layoutRoot, "copilot-chats.json")).Save(new(Guid.NewGuid().ToString("N"), "layout", "Project overview", root,
                "fixture-session", "legacy-two", DateTimeOffset.UtcNow,
                [new("user", "Help me understand the project in this folder.", DateTimeOffset.UtcNow),
                 new("assistant", "I can inspect the selected folder and explain its entry points. I will ask before running commands or making changes.", DateTimeOffset.UtcNow)]));
            using var page = new CopilotPage(layoutRoot, isolated: true);
            host.Controls.Add(page); host.Show();
            if (scale != 1) page.Scale(new SizeF(scale, scale));
            Finish(page.SetWorkspace("layout", "Layout validation", root));
            page.PerformLayout(); host.PerformLayout();
            typeof(CopilotPage).GetMethod("RenderTranscript", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(page, null);
            using (var preview = new Bitmap(page.Width, page.Height))
            { page.DrawToBitmap(preview, page.ClientRectangle); preview.Save(Path.Combine(root, "copilot-before-check-" + scale + ".png")); }
            foreach (string name in new[] { "CopilotInput", "CopilotSend", "CopilotStop", "CopilotModels" })
            {
                var control = page.Controls.Find(name, true).Single();
                var bounds = page.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
                Check(bounds.Width > 0 && bounds.Height > 0 && page.ClientRectangle.Contains(bounds),
                    $"Copilot {name} remains inside its page at {scale:0.0}x layout scale (control {bounds}, page {page.ClientRectangle})");
            }
            Finish(page.ShutdownAsync());
            Finish(page.SetWorkspace("layout", "Layout validation", root));
            Check(!page.IsWorking && page.Controls.Find("CopilotInput", true).Single() is TextBox { ReadOnly: false },
                "Reopening the Copilot page after shutdown restores its usable controls");
            using var bitmap = new Bitmap(page.Width, page.Height);
            page.DrawToBitmap(bitmap, page.ClientRectangle);
            bitmap.Save(Path.Combine(root, "copilot-layout-" + scale.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ".png"));
            // A disconnected local agent identity lets this isolated UI fixture exercise
            // the real permission view without starting a CLI or making network calls.
            var localAgent = new CopilotAgent();
            typeof(CopilotPage).GetField("agent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(page, localAgent);
            var request = JsonSerializer.SerializeToElement(new
            {
                toolCall = new { title = "Run project tests", rawInput = new { command = "dotnet test", cwd = root }, status = "pending" },
                options = new[] {
                    new { optionId = "yes-once", name = "Allow once", kind = "allow_once" },
                    new { optionId = "yes-always", name = "Always allow", kind = "allow_always" },
                    new { optionId = "no-once", name = "Reject once", kind = "reject_once" },
                    new { optionId = "no-always", name = "Always reject", kind = "reject_always" } }
            });
            var permission = (Task<string?>)typeof(CopilotPage).GetMethod("RequestPermissionAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(page, [localAgent, request, CancellationToken.None])!;
            Application.DoEvents(); page.PerformLayout();
            var review = page.Controls.Find("CopilotApprovalDetails", true).Single();
            page.DrawToBitmap(bitmap, page.ClientRectangle);
            bitmap.Save(Path.Combine(root, "copilot-approval-before-check-" + scale.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ".png"));
            Check(review.Visible && review.Width >= 250 && review.Height >= 60,
                $"Copilot permission action details remain readable at {scale:0.0}x scale ({review.Bounds}, visible={review.Visible})");
            var approvalPanel = page.Controls.Find("CopilotApproval", true).Single();
            var buttons = approvalPanel.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().ToArray();
            Check(buttons.Length == 5 && buttons.All(button => button.Height >= button.Font.Height + button.Padding.Vertical + 8
                && button.Parent!.ClientRectangle.Contains(button.Bounds)),
                $"Copilot permission choices have readable full-height labels and visible button bounds at {scale:0.0}x scale");
            foreach (string name in new[] { "CopilotInput", "CopilotSend", "CopilotStop", "CopilotModels" })
            {
                var control = page.Controls.Find(name, true).Single();
                Check(page.ClientRectangle.Contains(page.RectangleToClient(control.RectangleToScreen(control.ClientRectangle))),
                    $"Copilot permission review keeps {name} within the page at {scale:0.0}x scale");
            }
            page.DrawToBitmap(bitmap, page.ClientRectangle);
            bitmap.Save(Path.Combine(root, "copilot-approval-" + scale.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ".png"));
            page.Visible = false;
            Check(permission.Wait(TimeSpan.FromSeconds(2)) && permission.Result is null,
                "Hiding a Copilot permission card cancels its pending request without granting access");
            Finish(page.ShutdownAsync());
            var draft = (TextBox)page.Controls.Find("CopilotInput", true).Single(); draft.Text = "Keep this draft through Connect";
            typeof(CopilotPage).GetMethod("CreateChat", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(page, [false]);
            Check(draft.Text == "Keep this draft through Connect", "Connecting a new Copilot chat preserves an unsent draft");
            host.Close();
        }
    }
}
