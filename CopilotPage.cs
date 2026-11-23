using System.Text;
using System.Text.Json;

namespace AIHub;

internal sealed class CopilotPage : UserControl
{
    private readonly CopilotRuntime runtime;
    private readonly CopilotChatStore store;
    private readonly bool isolated;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim workspaceGate = new(1, 1);
    private readonly Label heading = new() { Text = "Copilot", AutoSize = true, ForeColor = UsageStyle.Ink, Font = new Font("Segoe UI Semibold", 14) };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(1000, 80), ForeColor = UsageStyle.Muted, Text = "Use Copilot directly in AI Hub. Choose a working folder, then connect." };
    private readonly TextBox folder = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly ComboBox sessions = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 270 };
    private readonly ComboBox models = new() { Name = "CopilotModels", DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly RichTextBox transcript = new() { Name = "CopilotTranscript", Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false, BorderStyle = BorderStyle.None, WordWrap = true };
    private readonly TextBox input = new() { Name = "CopilotInput", Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, MaxLength = 20000, AcceptsReturn = true };
    private readonly Button install, signIn, connect, chooseFolder, newChat, send, stop;
    private readonly TableLayoutPanel layout = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(12) };
    private readonly FlowLayoutPanel setupRow;
    private readonly TableLayoutPanel folderRow;
    private readonly Panel approval = new() { Name = "CopilotApproval", Dock = DockStyle.Fill, Padding = new Padding(6), BackColor = UsageStyle.Surface, Visible = false };
    private readonly RichTextBox approvalDetails = new() { Name = "CopilotApprovalDetails", Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false, BorderStyle = BorderStyle.None };
    private readonly FlowLayoutPanel approvalButtons = new() { Dock = DockStyle.Bottom, Height = 72, AutoScroll = true, WrapContents = true };
    private readonly System.Windows.Forms.Timer renderTimer = new() { Interval = 100 };
    private readonly List<CopilotChatMessage> messages = [];
    private readonly Dictionary<string, (int Index, Dictionary<string, JsonElement> Fields)> toolMessages = [];
    private CopilotInstallation? installation;
    private CopilotAgent? agent;
    private Action<JsonElement>? agentUpdated;
    private CopilotChat? chat;
    private string workspaceKey = "personal", workspaceName = "Personal workspace";
    private CancellationTokenSource? operation;
    private Task? runningTask;
    private TaskCompletionSource<string?>? pendingPermission;
    private bool busy, switching, refreshing, dirty, initialized, shuttingDown, resourcesDisposed;
    private volatile bool loadingSession;
    private string? operationNotice;
    private string usageSummary = "Session usage unavailable";

    internal bool IsWorking => busy || switching;
    internal string UsageSummary => pendingPermission is not null ? "Permission needed"
        : agent is null ? (busy ? "Setting up Copilot…" : "Not connected")
        : busy ? "Working · " + usageSummary : usageSummary;
    internal event Action? StateChanged;

    internal CopilotPage(string stateDirectory, bool isolated = false)
    {
        this.isolated = isolated;
        runtime = new CopilotRuntime(stateDirectory);
        store = new CopilotChatStore(Path.Combine(stateDirectory, "copilot-chats.json"));
        Dock = DockStyle.Fill; BackColor = UsageStyle.Background; ForeColor = UsageStyle.Ink;
        Font = new Font("Segoe UI", 9.5f);
        foreach (var control in new Control[] { folder, sessions, models, transcript, input, approvalDetails })
        { control.BackColor = UsageStyle.Surface; control.ForeColor = UsageStyle.Ink; }
        install = Button("Install runtime", () => RunOperation(async token =>
        {
            installation = await runtime.InstallAsync(Progress(), token); ShowStatus("Copilot " + installation.Version + " installed. Sign in to your GitHub account.");
        }, "Installing the Copilot runtime…"));
        signIn = Button("Sign in", () => RunOperation(async token =>
        {
            await DisconnectAsync();
            installation ??= await runtime.FindAsync(token);
            if (installation is null) throw new InvalidOperationException("Install the Copilot runtime first.");
            await runtime.SignInAsync(installation, Progress(), token);
            ShowStatus("Sign-in finished. Connect to check your Copilot access.");
        }, "Complete Copilot sign-in in your browser…"));
        connect = Button("Connect", () => RunOperation(ConnectAsync, "Connecting to Copilot…"));
        chooseFolder = Button("Choose folder", ChooseFolderAsync);
        newChat = Button("New chat", () => RunOperation(async _ =>
        {
            await DisconnectAsync(); SaveCurrent(); CreateChat(); ShowStatus("New chat ready. Connect to start.");
        }, "Creating a chat…"));
        send = Button("Send", SendAsync);
        stop = Button("Stop", StopAsync);
        send.Name = "CopilotSend"; stop.Name = "CopilotStop";
        setupRow = Flow(install, signIn, connect);
        folderRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3 };
        folderRow.ColumnStyles.Add(new(SizeType.AutoSize)); folderRow.ColumnStyles.Add(new(SizeType.Percent, 100)); folderRow.ColumnStyles.Add(new(SizeType.AutoSize));
        folderRow.Controls.Add(new Label { Text = "Working folder", AutoSize = true, Margin = new Padding(0, 8, 10, 0) }, 0, 0);
        folderRow.Controls.Add(folder, 1, 0); folderRow.Controls.Add(chooseFolder, 2, 0);
        var choices = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 5, Margin = new Padding(0, 4, 0, 4) };
        choices.ColumnStyles.Add(new(SizeType.AutoSize)); choices.ColumnStyles.Add(new(SizeType.Percent, 45));
        choices.ColumnStyles.Add(new(SizeType.AutoSize)); choices.ColumnStyles.Add(new(SizeType.AutoSize)); choices.ColumnStyles.Add(new(SizeType.Percent, 55));
        choices.Controls.Add(new Label { Text = "Chat", AutoSize = true, Margin = new Padding(0, 8, 6, 0) }, 0, 0);
        sessions.Dock = models.Dock = DockStyle.Fill; newChat.Width = 90;
        choices.Controls.Add(sessions, 1, 0); choices.Controls.Add(newChat, 2, 0);
        choices.Controls.Add(new Label { Text = "Model", AutoSize = true, Margin = new Padding(8, 8, 6, 0) }, 3, 0); choices.Controls.Add(models, 4, 0);
        var approvalTitle = new Label { Text = "Copilot requests permission — review the action before choosing", Dock = DockStyle.Top, Height = 28, ForeColor = UsageStyle.Warning };
        approval.Controls.Add(approvalDetails); approval.Controls.Add(approvalTitle); approval.Controls.Add(approvalButtons);
        var composer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 6, 0, 0) };
        composer.RowStyles.Add(new(SizeType.Percent, 100));
        composer.ColumnStyles.Add(new(SizeType.Percent, 100)); composer.ColumnStyles.Add(new(SizeType.Absolute, 112));
        send.Margin = stop.Margin = new Padding(3, 0, 3, 2);
        composer.Controls.Add(input, 0, 0); composer.Controls.Add(new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Controls = { send, stop } }, 1, 0);
        send.Width = stop.Width = 100;
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int row = 0; row < 5; row++) layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 0)); layout.RowStyles.Add(new(SizeType.Absolute, 80));
        layout.Controls.Add(heading, 0, 0); layout.Controls.Add(status, 0, 1); layout.Controls.Add(setupRow, 0, 2);
        var messageArea = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        messageArea.Controls.Add(transcript); messageArea.Controls.Add(approval);
        layout.Controls.Add(folderRow, 0, 3); layout.Controls.Add(choices, 0, 4); layout.Controls.Add(messageArea, 0, 5);
        layout.Controls.Add(composer, 0, 7); Controls.Add(layout);
        sessions.SelectionChangeCommitted += async (_, _) => await RunOperation(async _ =>
        {
            var next = (sessions.SelectedItem as ChatChoice)?.Chat;
            if (next is null || next.Id == chat?.Id) return;
            await DisconnectAsync(); SaveCurrent(); LoadChat(next); ShowStatus("Saved conversation loaded. Connect to resume it.");
        }, "Opening the saved chat…");
        models.SelectionChangeCommitted += async (_, _) => await RunOperation(async token =>
        {
            if (agent is null || models.SelectedItem is not CopilotModelOption option) return;
            string? previous = chat?.ModelId;
            try
            {
                if (option.ConfigId is { } config) ApplyModels(await agent.SetConfigAsync(config, option.Id, token));
                else await agent.SetModelAsync(option.Id, token);
            }
            catch { models.SelectedItem = models.Items.Cast<CopilotModelOption>().FirstOrDefault(item => item.Id == previous); throw; }
            if (chat is not null) { chat = chat with { ModelId = (models.SelectedItem as CopilotModelOption)?.Id ?? option.Id }; SaveCurrent(); }
            ShowStatus("Model changed to " + option.Name + ".");
        }, "Changing the Copilot model…");
        input.TextChanged += (_, _) => UpdateState();
        input.KeyDown += async (_, e) => { if (e.Control && e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await SendAsync(); } };
        VisibleChanged += (_, _) => { if (!Visible) CancelPermission(); };
        renderTimer.Tick += (_, _) => { if (dirty) RenderTranscript(); }; renderTimer.Start();
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        if (store.Warning is not null) ShowStatus(store.Warning, error: true);
        RefreshChats(); UpdateState();
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (initialized || isolated) return; initialized = true;
        await RunOperation(async token =>
        {
            installation = await runtime.FindAsync(token);
            ShowStatus(installation is null ? "Install the Copilot runtime to use this panel independently of VS Code."
                : "Copilot " + installation.Version + " is ready. Choose a folder and connect.");
        }, "Checking the Copilot runtime…");
    }

    internal async Task SetWorkspace(string key, string name, string? suggestedFolder)
    {
        CopilotChatStore.ValidateWorkspace(key);
        await workspaceGate.WaitAsync(lifetime.Token);
        try
        {
            shuttingDown = false;
            if (key == workspaceKey)
            {
                workspaceName = name; heading.Text = "Copilot · " + name;
                if (chat is null && store.ForWorkspace(key).FirstOrDefault() is { } latestChat) LoadChat(latestChat);
                if (chat is null && string.IsNullOrEmpty(folder.Text) && Directory.Exists(suggestedFolder)) folder.Text = Path.GetFullPath(suggestedFolder!);
                UpdateState();
                return;
            }
            switching = true; UpdateState();
            await StopAsync(); SaveCurrent();
            workspaceKey = key; workspaceName = name; heading.Text = "Copilot · " + name;
            chat = null; messages.Clear(); toolMessages.Clear(); input.Clear(); folder.Clear(); models.Items.Clear();
            var latest = store.ForWorkspace(key).FirstOrDefault();
            if (latest is not null) LoadChat(latest);
            else if (Directory.Exists(suggestedFolder)) folder.Text = Path.GetFullPath(suggestedFolder!);
            RefreshChats(); dirty = true; ShowStatus("Choose a chat and working folder for " + name + ".");
        }
        finally { switching = false; workspaceGate.Release(); UpdateState(); }
    }

    internal async Task StopAsync()
    {
        CancelPermission(); operation?.Cancel();
        var pending = runningTask;
        await DisconnectAsync();
        if (pending is not null) await pending.WaitAsync(TimeSpan.FromSeconds(15));
        SaveCurrent(); UpdateState();
    }

    internal async Task ShutdownAsync()
    {
        shuttingDown = true;
        try { await StopAsync(); }
        catch { shuttingDown = false; UpdateState(); throw; }
    }

    internal void ArchiveWorkspace(string key, string name)
    {
        store.ArchiveWorkspace(key, name); RefreshArchivedChats();
    }
    internal void ArchiveMissingWorkspaces(IEnumerable<string> knownKeys)
    {
        store.ArchiveMissingWorkspaces(knownKeys); RefreshArchivedChats();
    }
    private void RefreshArchivedChats()
    {
        if (chat is not null && store.ForWorkspace(workspaceKey).FirstOrDefault(item => item.Id == chat.Id) is { } fresh)
            chat = fresh with { Messages = [.. messages] };
        RefreshChats();
    }

    private async Task ChooseFolderAsync()
    {
        if (IsWorking) return;
        using var dialog = new FolderBrowserDialog { Description = "Choose the folder Copilot can work in", UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(folder.Text) ? folder.Text : "" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await RunOperation(async _ =>
        {
            await DisconnectAsync(); SaveCurrent(); folder.Text = dialog.SelectedPath;
            chat = null; messages.Clear(); toolMessages.Clear(); input.Clear(); models.Items.Clear(); RefreshChats(); dirty = true;
            ShowStatus("Working folder selected. Start a new chat or connect.");
        }, "Changing working folder…");
    }

    private void CreateChat(bool clearDraft = true)
    {
        if (!Directory.Exists(folder.Text)) throw new InvalidOperationException("Choose an existing working folder first.");
        chat = store.Save(new(Guid.NewGuid().ToString("N"), workspaceKey, "New chat", Path.GetFullPath(folder.Text), null, null, DateTimeOffset.UtcNow, []));
        messages.Clear(); toolMessages.Clear(); if (clearDraft) input.Clear(); models.Items.Clear(); RefreshChats(); dirty = true;
    }

    private async Task ConnectAsync(CancellationToken token)
    {
        await DisconnectAsync();
        installation ??= await runtime.FindAsync(token);
        if (installation is null) throw new InvalidOperationException("Install the Copilot runtime first.");
        if (!Directory.Exists(folder.Text)) throw new InvalidOperationException("Choose an existing working folder before connecting.");
        if (chat is null) CreateChat(clearDraft: false);
        var currentChat = chat!; usageSummary = "Session usage unavailable";
        var originalMessages = messages.ToArray();
        var created = new CopilotAgent(); agent = created; loadingSession = currentChat.SessionId is not null;
        var candidateReplay = loadingSession ? new CopilotReplayBuffer() : null;
        agentUpdated = update =>
        {
            var copy = update.Clone(); bool replay = loadingSession;
            if (replay) candidateReplay?.Accept(CopilotDisplay.Property(copy, "update"));
            PostUi(() => { if (ReferenceEquals(agent, created)) ApplyUpdate(copy, replay); });
        };
        created.Updated += agentUpdated;
        created.PermissionRequested = (request, cancellation) => RequestPermissionAsync(created, request.Clone(), cancellation);
        try
        {
            var setup = await created.StartAsync(installation.Executable, currentChat.Folder, currentChat.SessionId, token);
            token.ThrowIfCancellationRequested();
            ApplyModels(setup);
            var restored = candidateReplay?.Complete(originalMessages) ?? originalMessages;
            var committed = store.Save(currentChat with { SessionId = created.SessionId,
                ModelId = (models.SelectedItem as CopilotModelOption)?.Id ?? currentChat.ModelId, Messages = restored });
            chat = committed; messages.Clear(); messages.AddRange(restored);
            if (candidateReplay?.HasMessages == true)
            { toolMessages.Clear(); foreach (var pair in candidateReplay.ToolSnapshot()) toolMessages[pair.Key] = pair.Value; }
            RefreshChats(); dirty = true;
            ShowStatus(currentChat.SessionId is null ? "Connected. Send a message to Copilot." : "Conversation resumed. Continue where you left off.");
        }
        catch
        {
            await DisconnectAsync();
            // Keep the prior session and transcript. A failed resume never silently creates a replacement conversation.
            chat = currentChat; messages.Clear(); messages.AddRange(originalMessages); dirty = true; throw;
        }
        finally { loadingSession = false; }
    }

    private Task SendAsync() => RunOperation(async token =>
    {
        string prompt = input.Text.Trim(); if (prompt.Length == 0) return;
        if (agent is null) await ConnectAsync(token);
        if (messages.Count >= CopilotChatStore.MaxMessages - 10 || messages.Sum(m => m.Text.Length) > CopilotChatStore.MaxChatChars - 50000)
            throw new InvalidOperationException("This chat is nearly full. Start a new chat to continue.");
        int previousCount = messages.Count; var previousChat = chat;
        AddMessage("user", prompt);
        if (chat is not null && chat.Name == "New chat") chat = chat with { Name = new string(prompt.Take(80).Select(c => char.IsControl(c) ? ' ' : c).ToArray()) };
        try { SaveCurrent(); }
        catch { messages.RemoveRange(previousCount, messages.Count - previousCount); chat = previousChat; dirty = true; throw; }
        input.Clear();
        JsonElement result;
        try { result = await agent!.PromptAsync(prompt, token); }
        catch { await DisconnectAsync(); throw; }
        ShowStatus(operationNotice ?? (CopilotDisplay.String(result, "stopReason") == "cancelled" ? "Stopped." : "Ready. Ctrl+Enter sends your next message."));
    }, "Copilot is working…");

    private async Task RunOperation(Func<CancellationToken, Task> action, string message)
    {
        if (busy || switching || refreshing || shuttingDown || IsDisposed) return;
        busy = true; operationNotice = null;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runningTask = completion.Task;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); operation = cancellation;
        UpdateState(); ShowStatus(message);
        try { await action(cancellation.Token); }
        catch (OperationCanceledException) { ShowStatus(operationNotice ?? "Stopped. Connect again to continue this conversation."); }
        catch (Exception ex) { ShowStatus(ex.Message + (chat?.SessionId is not null && agent is null ? " The saved chat is retained; retry Connect or start a new chat." : ""), true); }
        finally
        {
            CancelPermission();
            try { SaveCurrent(); } catch (Exception ex) { ShowStatus("Could not save Copilot history: " + ex.Message, true); }
            operation = null; busy = false; runningTask = null; completion.TrySetResult(); UpdateState();
        }
    }

    private async Task DisconnectAsync()
    {
        var old = agent; if (old is null) return; agent = null;
        if (agentUpdated is not null) old.Updated -= agentUpdated;
        agentUpdated = null; old.PermissionRequested = null; CancelPermission();
        try { await old.CancelAsync().WaitAsync(TimeSpan.FromSeconds(3)); } catch (Exception) { }
        await old.DisposeAsync();
    }

    private void ApplyUpdate(JsonElement notification, bool replay)
    {
        if (IsDisposed || shuttingDown || chat is null) return;
        string? session = CopilotDisplay.String(notification, "sessionId");
        if (agent?.SessionId is { } active && session is not null && session != active) return;
        var update = CopilotDisplay.Property(notification, "update");
        string? kind = CopilotDisplay.String(update, "sessionUpdate");
        if (kind == "config_option_update") { ApplyModels(update); return; }
        if (kind == "usage_update")
        { if (CopilotDisplay.Usage(update) is { } value) { usageSummary = value; StateChanged?.Invoke(); } return; }
        if (replay) return;
        if (kind == "agent_message_chunk")
        {
            string? text = CopilotDisplay.String(CopilotDisplay.Property(update, "content"), "text");
            if (text is null) return;
            if (messages.Count > 0 && messages[^1].Role == "assistant")
            {
                if (!Fits(text.Length, messages[^1].Text.Length + text.Length)) return;
                messages[^1] = messages[^1] with { Text = messages[^1].Text + text }; dirty = true;
            }
            else AddMessage("assistant", text);
        }
        else if (kind is "tool_call" or "tool_call_update")
        {
            string id = CopilotDisplay.String(update, "toolCallId") ?? Guid.NewGuid().ToString("N");
            bool exists = toolMessages.TryGetValue(id, out var previous);
            var fields = CopilotDisplay.MergeTool(exists ? previous.Fields : null, update);
            string detail = CopilotDisplay.ToolDetails(JsonSerializer.SerializeToElement(fields));
            if (exists)
            {
                if (!Fits(detail.Length - messages[previous.Index].Text.Length, detail.Length)) return;
                messages[previous.Index] = messages[previous.Index] with { Text = detail }; toolMessages[id] = (previous.Index, fields); dirty = true;
            }
            else if (AddMessage("tool", detail)) toolMessages[id] = (messages.Count - 1, fields);
        }
    }

    private bool Fits(int additional, int messageLength)
    {
        if (messageLength <= CopilotChatStore.MaxMessageChars && messages.Sum(m => m.Text.Length) + additional <= CopilotChatStore.MaxChatChars) return true;
        operationNotice = "Stopped because this chat reached its local history limit. Start a new chat.";
        operation?.Cancel(); ShowStatus(operationNotice, true); return false;
    }
    private bool AddMessage(string role, string text)
    {
        if (messages.Count >= CopilotChatStore.MaxMessages || !Fits(text.Length, text.Length))
        { operationNotice ??= "Stopped because this chat is full. Start a new chat."; operation?.Cancel(); return false; }
        messages.Add(new(role, text, DateTimeOffset.UtcNow)); dirty = true; return true;
    }

    private async Task<string?> RequestPermissionAsync(CopilotAgent owner, JsonElement request, CancellationToken cancellation)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellation.Register(() => completion.TrySetResult(null));
        if (!PostUi(() =>
        {
            if (!Visible || shuttingDown || !ReferenceEquals(agent, owner) || pendingPermission is not null || completion.Task.IsCompleted)
            { completion.TrySetResult(null); return; }
            var tool = CopilotDisplay.Property(request, "toolCall");
            string? toolId = CopilotDisplay.String(tool, "toolCallId");
            var known = toolId is not null && toolMessages.TryGetValue(toolId, out var previous) ? previous.Fields : null;
            string details = CopilotDisplay.ToolDetails(JsonSerializer.SerializeToElement(CopilotDisplay.MergeTool(known, tool)));
            if (details.Length > CopilotChatStore.MaxMessageChars)
            { ShowStatus("Copilot requested an action too large to review. Permission was cancelled.", true); completion.TrySetResult(null); return; }
            pendingPermission = completion; approvalDetails.Text = details;
            foreach (Control control in approvalButtons.Controls.Cast<Control>().ToArray()) control.Dispose();
            approvalButtons.Controls.Clear();
            var options = CopilotDisplay.Property(request, "options");
            if (options.ValueKind == JsonValueKind.Array)
                foreach (var option in options.EnumerateArray().Take(20))
                {
                    string? id = CopilotDisplay.String(option, "optionId");
                    if (string.IsNullOrEmpty(id)) continue;
                    var button = UsageStyle.Button(CopilotDisplay.String(option, "name") ?? id, () => completion.TrySetResult(id));
                    button.Size = new Size(LogicalToDeviceUnits(142), LogicalToDeviceUnits(36));
                    button.AutoEllipsis = true; button.AccessibleName = button.Text; approvalButtons.Controls.Add(button);
                }
            var cancel = UsageStyle.Button("Cancel request", () => completion.TrySetResult(null));
            cancel.Size = new Size(LogicalToDeviceUnits(142), LogicalToDeviceUnits(36)); approvalButtons.Controls.Add(cancel);
            approvalButtons.Height = LogicalToDeviceUnits(36) * 2 + cancel.Margin.Vertical * 2 + 2;
            transcript.Visible = false; approval.Visible = true; approval.BringToFront();
            ShowStatus("Copilot is waiting for your permission."); UpdateState();
        })) completion.TrySetResult(null);
        try { return await completion.Task.ConfigureAwait(false); }
        finally { PostUi(() => { if (ReferenceEquals(pendingPermission, completion)) { pendingPermission = null; HidePermission(); } }); }
    }

    private void CancelPermission()
    {
        pendingPermission?.TrySetResult(null); pendingPermission = null; HidePermission();
    }
    private void HidePermission()
    {
        if (IsDisposed) return;
        approval.Visible = false; transcript.Visible = true;
        foreach (Control control in approvalButtons.Controls.Cast<Control>().ToArray()) control.Dispose();
        approvalButtons.Controls.Clear(); UpdateState();
    }

    private void ApplyModels(JsonElement setup)
    {
        var choices = CopilotDisplay.Models(setup);
        if (choices.Options.Length == 0) return;
        refreshing = true;
        try
        {
            models.Items.Clear(); models.Items.AddRange(choices.Options);
            models.SelectedItem = choices.Options.FirstOrDefault(o => o.Id == choices.CurrentId);
        }
        finally { refreshing = false; }
        UpdateState();
    }

    private void LoadChat(CopilotChat next)
    {
        chat = next; folder.Text = next.Folder; messages.Clear(); messages.AddRange(next.Messages);
        toolMessages.Clear(); input.Clear(); models.Items.Clear(); RefreshChats(); dirty = true;
    }
    private void SaveCurrent()
    {
        if (chat is null || store.Warning is not null) return;
        var updated = chat with { Messages = [.. messages] };
        if (!chat.Messages.SequenceEqual(updated.Messages)) updated = updated with { Updated = DateTimeOffset.UtcNow };
        chat = store.Save(updated); RefreshChats();
    }
    private void RefreshChats()
    {
        if (IsDisposed) return;
        refreshing = true;
        try
        {
            sessions.Items.Clear();
            var choices = store.ForWorkspace(workspaceKey).Select(c => new ChatChoice(c)).ToArray();
            sessions.Items.AddRange(choices);
            sessions.SelectedItem = choices.FirstOrDefault(c => c.Chat.Id == chat?.Id);
        }
        finally { refreshing = false; }
    }
    private void RenderTranscript()
    {
        dirty = false;
        var text = new StringBuilder();
        foreach (var item in messages) text.AppendLine(item.Role switch { "user" => "You", "assistant" => "Copilot", "tool" => "Tool activity", _ => "AI Hub" }).AppendLine(item.Text).AppendLine();
        transcript.Text = text.ToString(); transcript.SelectionStart = transcript.TextLength; transcript.ScrollToCaret();
    }
    private void UpdateState()
    {
        if (IsDisposed) return;
        bool available = !IsWorking && !shuttingDown && store.Warning is null;
        setupRow.Visible = agent is null;
        folderRow.Visible = pendingPermission is null;
        install.Enabled = available && agent is null; signIn.Enabled = available && installation is not null;
        connect.Enabled = available && installation is not null && Directory.Exists(folder.Text);
        connect.Text = agent is null ? "Connect" : "Reconnect";
        chooseFolder.Enabled = newChat.Enabled = available; sessions.Enabled = available && sessions.Items.Count > 0;
        models.Enabled = available && agent is not null && models.Items.Count > 0;
        send.Enabled = available && Directory.Exists(folder.Text) && input.Text.Trim().Length > 0;
        stop.Enabled = IsWorking; input.ReadOnly = IsWorking || shuttingDown;
        StateChanged?.Invoke();
    }
    private void ShowStatus(string text, bool error = false)
    {
        if (IsDisposed) return;
        status.Text = store.Warning ?? (text.Length > 1800 ? text[..1800] + "…" : text);
        status.ForeColor = error || store.Warning is not null ? UsageStyle.Warning : UsageStyle.Muted;
        StateChanged?.Invoke();
    }
    private IProgress<string> Progress() => new Progress<string>(text => { if (!IsDisposed) ShowStatus(text); });
    private bool PostUi(Action action)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return false;
        try { BeginInvoke(() => { if (!IsDisposed && !Disposing) action(); }); return true; }
        catch (InvalidOperationException) { return false; }
    }
    private Button Button(string text, Func<Task> action)
    {
        var button = UsageStyle.Button(text, () => { }); button.Width = 135; button.Height = 30;
        button.Click += async (_, _) => { try { await action(); } catch (Exception ex) { ShowStatus(ex.Message, true); } };
        return button;
    }
    private static FlowLayoutPanel Flow(params Control[] controls)
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 5, 0, 5) };
        flow.Controls.AddRange(controls); return flow;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed)
        {
            resourcesDisposed = true; shuttingDown = true; lifetime.Cancel(); CancelPermission(); renderTimer.Dispose();
            var old = agent; agent = null;
            if (old is not null)
            {
                if (agentUpdated is not null) old.Updated -= agentUpdated;
                old.PermissionRequested = null;
                _ = old.DisposeAsync().AsTask().ContinueWith(task => { _ = task.Exception; }, TaskScheduler.Default);
            }
            lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
    private sealed record ChatChoice(CopilotChat Chat) { public override string ToString() => Chat.Name; }
}
