using AIHub.Usage;

namespace AIHub;

internal sealed class HubForm : Form
{
    private static readonly Color Background = Color.FromArgb(19, 22, 27), Surface = Color.FromArgb(26, 30, 36), Muted = Color.FromArgb(151, 160, 172), Ink = Color.FromArgb(236, 240, 244);
    private readonly DockManager dock;
    private readonly ZoomSettings zoom;
    private readonly Dictionary<string, (SavedWindow Window, int Percent)> appliedZoom = [];
    private readonly Panel viewport = new() { Dock = DockStyle.Fill, BackColor = Background };
    private readonly Panel empty = new() { Dock = DockStyle.Fill, BackColor = Background, Padding = new Padding(50) };
    private readonly Label heading = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Ink };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 36, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(22, 0, 0, 0), ForeColor = Muted };
    private readonly Dictionary<string, Button> navigation = [];
    private readonly ToolTip navigationHints = new() { InitialDelay = 350, ReshowDelay = 100, AutoPopDelay = 8000 };
    private readonly Button detach, restore, settingsButton, usageButton;
    private readonly UsageReaders usageReaders = new();
    private readonly UsageMonitor usage;
    private UsagePreferences usagePreferences;
    private readonly string usagePreferencesPath;
    private readonly UsageAlerts usageAlerts;
    private readonly UsageStrip usageStrip;
    private readonly System.Windows.Forms.Timer usageTimer = new() { Interval = 1000 };
    private readonly NotifyIcon usageNotifications;
    private readonly Queue<UsageAlert> pendingAlerts = new();
    private DateTimeOffset nextUsageRefresh, nextAlert;
    private UsagePage? usagePage;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 150 };
    private string? selected;
    private bool busy;
    private bool closing;
    private bool minimized;
    private bool presentationQueued;
    private bool focusPresentation;
    private enum HubPage { Workspace, Settings, Usage }
    private HubPage page;
    private bool pageOpen => page != HubPage.Workspace;

    internal HubForm(string statePath)
    {
        dock = new DockManager(statePath);
        zoom = new ZoomSettings(Path.Combine(Path.GetDirectoryName(statePath)!, "zoom-settings.json"));
        usagePreferencesPath = Path.Combine(Path.GetDirectoryName(statePath)!, "usage-settings.json");
        usagePreferences = UsagePreferences.Load(usagePreferencesPath);
        usage = new UsageMonitor(usageReaders.Providers, usagePreferences.IsEnabled);
        usageAlerts = new UsageAlerts(usagePreferences.SentAlerts);
        usageStrip = new UsageStrip(usageReaders.Providers, ShowUsage);
        usageNotifications = new NotifyIcon { Icon = AppInfo.LoadIcon(), Text = "AI Hub usage alerts", Visible = usagePreferences.LowAlerts || usagePreferences.ResetAlerts };
        usageNotifications.BalloonTipClicked += (_, _) => { WindowState = FormWindowState.Normal; Activate(); ShowUsage(); };
        usageNotifications.DoubleClick += (_, _) => { WindowState = FormWindowState.Normal; Activate(); ShowUsage(); };
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = AppInfo.DisplayName;
        Icon = AppInfo.LoadIcon();
        BackColor = Background;
        ForeColor = Ink;
        Font = new Font("Segoe UI", 10);
        MinimumSize = new Size(1000, 700);
        Size = new Size(1440, 940);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;

        var sidebar = new Panel { Dock = DockStyle.Left, Width = 88, BackColor = Surface, Padding = new Padding(8, 12, 8, 10) };
        var brand = new Panel { Height = 64, Dock = DockStyle.Top };
        var emblem = new PictureBox { Image = Icon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(40, 40), Location = new Point(16, 4), AccessibleName = "AI Hub icon" };
        brand.Controls.Add(emblem);
        navigationHints.SetToolTip(emblem, AppInfo.DisplayName);
        var choices = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        foreach (var entry in AppEntry.All)
        {
            var button = new ProviderButton(entry, async () => await Select(entry));
            navigationHints.SetToolTip(button, entry.Name + "\n" + entry.Description);
            navigation[entry.Key] = button;
            choices.Controls.Add(button);
        }
        var version = new Label { Dock = DockStyle.Bottom, Height = 24, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Muted, Text = $"v{AppInfo.Version}", Font = new Font("Segoe UI", 8), AccessibleName = "AI Hub version " + AppInfo.Version };
        sidebar.Controls.Add(choices);
        sidebar.Controls.Add(brand);
        settingsButton = MakeButton("Settings", ShowSettings);
        settingsButton.Font = new Font("Segoe UI", 8.5f);
        settingsButton.AccessibleName = "Settings";
        settingsButton.Dock = DockStyle.Bottom;
        settingsButton.Height = 42;
        navigationHints.SetToolTip(settingsButton, "Settings · App content zoom");
        sidebar.Controls.Add(settingsButton);
        sidebar.Controls.Add(version);
        usageButton = MakeButton("Usage", ShowUsage);
        usageButton.Font = new Font("Segoe UI", 8.5f);
        usageButton.AccessibleName = "Usage overview";
        usageButton.Dock = DockStyle.Bottom;
        usageButton.Height = 42;
        sidebar.Controls.Add(usageButton);
        sidebar.Controls.SetChildIndex(usageButton, sidebar.Controls.IndexOf(settingsButton));

        var top = new Panel { Dock = DockStyle.Top, Height = 66, Padding = new Padding(22, 12, 16, 12), BackColor = Surface };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 285, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        detach = MakeButton("Pop out", PopOut);
        detach.Size = new Size(108, 38);
        detach.Enabled = false;
        restore = MakeButton("Restore all", RestoreAll);
        restore.Size = new Size(148, 38);
        actions.Controls.Add(detach);
        actions.Controls.Add(restore);
        top.Controls.Add(heading);
        top.Controls.Add(actions);

        var main = new Panel { Dock = DockStyle.Fill };
        viewport.Controls.Add(empty);
        main.Controls.Add(viewport);
        main.Controls.Add(status);
        main.Controls.Add(usageStrip);
        main.Controls.Add(top);
        Controls.Add(main);
        Controls.Add(sidebar);
        heading.Text = "Welcome to your workspace";
        ShowWelcome();
        status.Height = 28;
        status.Visible = false;
        status.TextChanged += (_, _) => UpdateStatusVisibility();
        usage.Changed += UsageChanged;
        usageTimer.Tick += (_, _) =>
        {
            if (closing) return;
            UpdateUsage();
            if (DateTimeOffset.Now >= nextUsageRefresh)
            {
                nextUsageRefresh = DateTimeOffset.Now.AddSeconds(5);
                _ = usage.RefreshAsync();
            }
            if (pendingAlerts.Count > 0 && DateTimeOffset.Now >= nextAlert)
            {
                var alert = pendingAlerts.Dequeue();
                usageNotifications.ShowBalloonTip(7000, alert.Title, alert.Message, ToolTipIcon.Info);
                nextAlert = DateTimeOffset.Now.AddSeconds(10);
            }
        };
        viewport.Resize += (_, _) => ResizeSelected();
        LocationChanged += (_, _) => ResizeSelected();
        Activated += (_, _) => QueueSelectedPresentation();
        Shown += (_, _) =>
        {
            var area = Screen.FromControl(this).WorkingArea;
            Size = new Size(Math.Min(Width, area.Width - 40), Math.Min(Height, area.Height - 40));
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            int dark = 1;
            Native.DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
            timer.Start();
            usageTimer.Start();
            nextUsageRefresh = DateTimeOffset.Now.AddSeconds(5);
            _ = usage.RefreshAsync();
        };
        timer.Tick += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
            {
                if (!minimized) dock.HideAll();
                minimized = true;
                return;
            }
            if (minimized)
            {
                minimized = false;
                if (selected is not null && !pageOpen) dock.Select(selected, viewport.Handle);
            }
            if (selected is not null && !busy && !pageOpen && !dock.Has(selected))
            {
                selected = null;
                detach.Enabled = false;
                ShowWelcome("The app window closed. Select its tab to open it again.");
            }
            if (selected is not null && !busy) ResizeSelected();
        };
        FormClosing += (_, e) =>
        {
            if (busy) { e.Cancel = true; status.Text = "Wait for the app to finish connecting, then close the hub."; return; }
            closing = true;
            timer.Stop();
            try { dock.RestoreAll(); }
            catch (Exception ex) { closing = false; timer.Start(); e.Cancel = true; MessageBox.Show("An app could not be restored yet. Keep the hub open and try Restore all again.\n\n" + ex.Message, "AI Hub"); }
        };
        FormClosed += (_, _) =>
        {
            usageTimer.Stop(); usage.Changed -= UsageChanged; usage.Dispose(); usageTimer.Dispose();
            usageReaders.Dispose(); usageNotifications.Visible = false;
            usageNotifications.Icon?.Dispose(); usageNotifications.Dispose();
            dock.Dispose(); timer.Dispose(); navigationHints.Dispose(); emblem.Image?.Dispose(); Icon?.Dispose();
        };
        // These controls are created in code, so the design DPI is supplied after
        // all children exist, not before Font changes trigger automatic layout.
        AutoScaleDimensions = new SizeF(96, 96);
        PerformAutoScale();
    }

    private static Button MakeButton(string text, Action click)
    {
        var button = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = Surface, ForeColor = Ink, Cursor = Cursors.Hand, Font = new Font("Segoe UI", 10) };
        button.FlatAppearance.BorderColor = Color.FromArgb(57, 64, 73);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(47, 55, 65);
        button.Click += (_, _) => click();
        return button;
    }

    private void ShowWelcome(string? message = null)
    {
        page = HubPage.Workspace;
        heading.Text = "Welcome to your workspace";
        ClearContent();
        empty.Padding = new Padding(IsHandleCreated ? (int)Math.Round(50f * DeviceDpi / 96f) : 50);
        var content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        content.Controls.Add(new Label { AutoSize = true, Text = "A home for every AI.", Font = new Font("Segoe UI Semibold", 30), ForeColor = Ink, Margin = new Padding(0, 35, 0, 18) });
        content.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(720, 0), Text = message ?? "Bring your existing apps into one window. Switch from a conversation to a coding task or an agent without juggling your desktop.", Font = new Font("Segoe UI", 13), ForeColor = Muted, Margin = new Padding(0, 0, 0, 38) });
        foreach (var entry in AppEntry.All)
        {
            var button = MakeButton(entry.Name + "     /     " + entry.Description + "     →", async () => await Select(entry));
            button.Size = new Size(660, 68);
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(18, 0, 0, 0);
            button.ForeColor = entry.Accent;
            button.Margin = new Padding(0, 0, 0, 14);
            content.Controls.Add(button);
        }
        content.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(680, 0), Text = "OpenAI includes ChatGPT, Codex and your Dot. Use their existing controls inside the OpenAI panel.\n\nPop out returns an app to its own window. Closing AI Hub restores every connected window.", Font = new Font("Segoe UI", 10), ForeColor = Muted, Margin = new Padding(0, 24, 0, 0) });
        empty.Controls.Add(content);
        empty.Visible = true;
        status.Text = zoom.LoadWarning ?? $"{AppInfo.DisplayName}  ·  Uses your installed apps";
        UpdateNavigation();
    }

    private void ClearContent()
    {
        usagePage = null;
        foreach (Control control in empty.Controls.Cast<Control>().ToArray()) control.Dispose();
    }

    private void ShowSettings()
    {
        if (busy || closing) return;
        page = HubPage.Settings;
        focusPresentation = false;
        dock.HideAll();
        ClearContent();
        empty.Padding = Padding.Empty;
        empty.Controls.Add(new SettingsPage(zoom, (key, percent) =>
        {
            zoom.Set(key, percent);
            appliedZoom.Remove(key);
            status.Text = "Zoom choice saved. Open the panel to apply it.";
        }, async entry =>
        {
            appliedZoom.Remove(entry.Key);
            await Select(entry);
        }, usagePreferences, usageReaders.Providers, SaveUsagePreferences));
        empty.Visible = true;
        heading.Text = "Settings   /   Zoom, usage and alerts";
        detach.Enabled = false;
        status.Text = "Choose a zoom level for each AI panel.";
        UpdateNavigation();
    }

    private async Task Select(AppEntry entry)
    {
        if (busy || closing) return;
        busy = true;
        try
        {
            status.Text = $"Connecting {entry.Name}…";
            if (!dock.Has(entry.Key))
            {
                var candidates = Native.Windows().Where(entry.Matches).ToList();
                if (candidates.Count == 0)
                {
                    entry.Launch();
                    for (int attempt = 0; attempt < 80; attempt++)
                    {
                        await Task.Delay(250);
                        candidates = Native.Windows().Where(entry.Matches).ToList();
                        if (candidates.Count > 0) break;
                    }
                }
                if (candidates.Count == 0) throw new InvalidOperationException($"{entry.Name} did not expose a matching window. Finish opening it, then select its tab again.");
                var chosen = candidates.Count == 1 ? candidates[0] : ChooseWindow(candidates);
                if (chosen is null) { status.Text = "No window selected."; return; }
                await dock.Attach(entry.Key, chosen, viewport.Handle);
            }
            selected = entry.Key;
            page = HubPage.Workspace;
            empty.Visible = false;
            dock.Select(entry.Key, viewport.Handle);
            heading.Text = entry.Name + "   /   " + entry.Description;
            detach.Enabled = true;
            status.Text = entry.Key == "openai" ? "Use the OpenAI app's own ChatGPT, Codex and Dot controls. Your chats and account stay in that app." : $"{entry.Name} is connected. Click inside the app to use it.";
            UpdateNavigation();
            QueueSelectedPresentation(true);
        }
        catch (Exception ex)
        {
            if (!pageOpen && selected is not null && dock.Has(selected)) dock.Select(selected, viewport.Handle);
            status.Text = $"Could not connect {entry.Name}.";
            MessageBox.Show(this, ex.Message, "AI Hub", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally { busy = false; UpdateStatusVisibility(); }
    }

    private WindowInfo? ChooseWindow(List<WindowInfo> windows)
    {
        using var dialog = new Form { Text = "Choose the window to bring into AI Hub", Size = new Size(700, 340), StartPosition = FormStartPosition.CenterParent, BackColor = Background, ForeColor = Ink, MinimizeBox = false, MaximizeBox = false };
        var list = new ListBox { Dock = DockStyle.Fill, Font = Font, BackColor = Surface, ForeColor = Ink };
        list.Items.AddRange(windows.Cast<object>().ToArray());
        list.SelectedIndex = 0;
        var use = MakeButton("Use selected window", () => dialog.DialogResult = DialogResult.OK);
        use.Dock = DockStyle.Bottom;
        use.Height = 44;
        dialog.Controls.Add(list);
        dialog.Controls.Add(use);
        dialog.AcceptButton = use;
        return dialog.ShowDialog(this) == DialogResult.OK ? list.SelectedItem as WindowInfo : null;
    }

    private void ResizeSelected()
    {
        if (selected is null || pageOpen || closing || WindowState == FormWindowState.Minimized) return;
        try { dock.Resize(selected, viewport.Handle, Native.GetForegroundWindow() == Handle || dock.IsForeground(selected)); }
        catch (Exception ex) { status.Text = "Resize failed: " + ex.Message; }
    }
    private void QueueSelectedPresentation(bool focus = false)
    {
        if (closing || !IsHandleCreated || IsDisposed) return;
        focusPresentation |= focus;
        if (presentationQueued) return;
        presentationQueued = true;
        BeginInvoke((Action)(async () =>
        {
            presentationQueued = false;
            bool activate = focusPresentation;
            focusPresentation = false;
            if (closing || busy || pageOpen || selected is null || WindowState == FormWindowState.Minimized) return;
            // Windows finishes activating/raising the hub before its app is
            // placed above the content area. Do not steal focus from other apps.
            if (Native.GetForegroundWindow() != Handle && !dock.IsForeground(selected)) return;
            // Activation itself must not take focus from a mouse-down in the
            // sidebar. Only a completed panel selection transfers focus.
            try
            {
                dock.Select(selected, viewport.Handle, activate);
                if (activate) await ApplySelectedZoom();
            }
            catch (Exception ex) { status.Text = "Could not show the selected app: " + ex.Message; }
        }));
    }
    private async Task ApplySelectedZoom()
    {
        var key = selected;
        if (key is null || zoom.Get(key) is not int percent || !dock.Windows.TryGetValue(key, out var window)) return;
        if (appliedZoom.TryGetValue(key, out var applied) && ReferenceEquals(applied.Window, window) && applied.Percent == percent) return;
        busy = true;
        try
        {
            await Task.Delay(100); // Let the app receive focus before its shortcuts.
            await ZoomController.Apply(key, percent,
                () => !closing && !pageOpen && selected == key && window.Matches()
                    && Native.IsWindowVisible((nint)window.Handle) && dock.IsForeground(key),
                command => Native.SendZoomChord((nint)window.Handle, command));
            appliedZoom[key] = (window, percent);
            status.Text = $"{AppEntry.All.First(a => a.Key == key).Name}  ·  Zoom {percent}%";
        }
        catch (Exception ex) { status.Text = "Zoom not applied: " + ex.Message; }
        finally { busy = false; UpdateStatusVisibility(); }
    }
    private void PopOut()
    {
        if (selected is null || busy) return;
        try
        {
            dock.Detach(selected);
            appliedZoom.Remove(selected);
            selected = null;
            detach.Enabled = false;
            heading.Text = "Your workspace";
            ShowWelcome("The app is back in its own window. Select a tab to continue.");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "AI Hub"); }
    }
    private void RestoreAll()
    {
        if (busy) return;
        try
        {
            dock.RestoreAll();
            appliedZoom.Clear();
            selected = null;
            detach.Enabled = false;
            heading.Text = "Your workspace";
            ShowWelcome("All apps are back in their own windows.");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "AI Hub"); }
    }
    private void UpdateNavigation()
    {
        foreach (var (key, button) in navigation)
        {
            button.BackColor = !pageOpen && selected == key ? Color.FromArgb(48, 64, 62) : Surface;
            button.FlatAppearance.BorderSize = !pageOpen && selected == key ? 1 : 0;
            button.FlatAppearance.BorderColor = !pageOpen && selected == key ? Color.FromArgb(121, 201, 179) : Color.FromArgb(57, 64, 73);
        }
        foreach (var (button, active) in new[] { (settingsButton, page == HubPage.Settings), (usageButton, page == HubPage.Usage) })
        {
            button.BackColor = active ? Color.FromArgb(48, 64, 62) : Surface;
            button.FlatAppearance.BorderSize = active ? 1 : 0;
            button.FlatAppearance.BorderColor = active ? Color.FromArgb(121, 201, 179) : Color.FromArgb(57, 64, 73);
        }
    }

    private void ShowUsage()
    {
        if (busy || closing) return;
        page = HubPage.Usage;
        focusPresentation = false;
        dock.HideAll();
        ClearContent();
        empty.Padding = Padding.Empty;
        usagePage = new UsagePage(usageReaders.Providers, () => _ = usage.RefreshAsync(true), async () =>
        {
            if (selected is { } key) await Select(AppEntry.All.Single(a => a.Key == key));
            else ShowWelcome();
        }, ShowSettings);
        empty.Controls.Add(usagePage);
        empty.Visible = true;
        heading.Text = "Usage   /   Allowances and connections";
        detach.Enabled = false;
        status.Text = "Usage readings cover the named product only.";
        UpdateNavigation(); UpdateUsage();
    }

    private void UsageChanged()
    {
        if (closing || IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke((Action)(() => { if (!closing && !IsDisposed) UpdateUsage(); })); }
        catch (InvalidOperationException) { /* The hub is closing. */ }
    }

    private void UpdateUsage()
    {
        var states = usage.States;
        usageStrip.Update(states);
        usagePage?.Update(states, usage.Busy);
        int previousCount = usageAlerts.Sent.Count;
        foreach (var state in states)
        {
            var provider = usageReaders.Providers.Single(p => p.Id == state.Id);
            foreach (var alert in usageAlerts.Evaluate(provider, state, DateTimeOffset.Now,
                usagePreferences.LowAlerts, usagePreferences.ResetAlerts, usagePreferences.UsedThreshold)) pendingAlerts.Enqueue(alert);
        }
        if (usageAlerts.Sent.Count != previousCount)
        {
            usagePreferences.SentAlerts = usageAlerts.Sent;
            try { usagePreferences.Save(usagePreferencesPath); }
            catch { status.Text = "Could not save alert history. Alerts may repeat after restart."; }
        }
    }

    private void SaveUsagePreferences(UsagePreferences proposed)
    {
        proposed.SentAlerts = usageAlerts.Sent;
        proposed.Save(usagePreferencesPath);
        usagePreferences = proposed;
        foreach (var provider in usageReaders.Providers) usage.SetEnabled(provider.Id, proposed.IsEnabled(provider.Id));
        pendingAlerts.Clear();
        usageNotifications.Visible = proposed.LowAlerts || proposed.ResetAlerts;
        UpdateUsage();
        _ = usage.RefreshAsync(true);
    }

    private void UpdateStatusVisibility()
    {
        navigationHints.SetToolTip(heading, status.Text);
        status.Visible = busy || status.Text.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || status.Text.Contains("could not", StringComparison.OrdinalIgnoreCase) || status.Text.Contains("not applied", StringComparison.OrdinalIgnoreCase);
    }
}
