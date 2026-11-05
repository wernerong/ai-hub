namespace AIHub;

internal sealed class ConnectionDialog : Form
{
    private readonly ListBox windows;
    private readonly TextBox name;
    internal WindowInfo? ChosenWindow => windows.SelectedItem as WindowInfo;
    internal string ConnectionName => name.Text;
    internal string? RemoveKey { get; private set; }
    internal string? RestoreKey { get; private set; }
    internal string? ReconnectKey { get; private set; }
    internal ConnectionDialog(WindowInfo[] candidates, AppEntry[] added, AppEntry[] removed, AppEntry? reconnect = null)
    {
        var savedBrowsers = added.Concat(removed).Where(a => a.Custom && a.WindowTitle is not null)
            .DistinctBy(a => a.Key).OrderBy(a => a.Name).ToArray();
        if (reconnect is not null) { candidates = candidates.Where(w => AppEntry.IsBrowser(w.ProcessName)).ToArray(); added = []; removed = []; savedBrowsers = []; }
        Text = reconnect is null ? "Connect an AI" : "Reconnect " + reconnect.Name;
        Size = new Size(800, 720); MinimumSize = new Size(700, 620);
        StartPosition = FormStartPosition.CenterParent; BackColor = UsageStyle.Background; ForeColor = UsageStyle.Ink;
        Font = new Font("Segoe UI", 10); MinimizeBox = false; MaximizeBox = false; Padding = new Padding(24);
        var intro = new Label { Dock = DockStyle.Top, Height = 76,
            Text = reconnect is null ? "CHOOSE AN OPEN WINDOW\n\nOpen your AI app first. For a website, use a dedicated browser window. Your existing login stays in that app."
                : "RECONNECT " + reconnect.Name.ToUpperInvariant() + "\n\nOpen the website in its own browser window, then choose it below. Its saved name and project membership stay in place.", ForeColor = UsageStyle.Muted };
        windows = new ListBox { Dock = DockStyle.Fill, BackColor = UsageStyle.Surface, ForeColor = UsageStyle.Ink, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false, AccessibleName = "Open app windows" };
        windows.Items.AddRange(candidates.OrderBy(w => w.ProcessName).Cast<object>().ToArray());
        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 172 + (added.Length > 0 ? 48 : 0) + (removed.Length > 0 ? 48 : 0) + (savedBrowsers.Length > 0 ? 48 : 0), ColumnCount = 2, Padding = new Padding(0, 10, 0, 0) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        name = new TextBox { Dock = DockStyle.Top, MaxLength = 40, PlaceholderText = "Name for this AI", AccessibleName = "AI connection name", BackColor = UsageStyle.Surface, ForeColor = UsageStyle.Ink,
            Text = reconnect?.Name ?? "", ReadOnly = reconnect is not null };
        bottom.Controls.Add(name, 0, 0);
        var connect = UsageStyle.Button(reconnect is null ? "Connect window" : "Reconnect window", () =>
        {
            if (ChosenWindow is null || string.IsNullOrWhiteSpace(name.Text)) return;
            ReconnectKey = reconnect?.Key; DialogResult = DialogResult.OK;
        });
        connect.Dock = DockStyle.Top; connect.Enabled = false; bottom.Controls.Add(connect, 1, 0);
        windows.SelectedIndexChanged += (_, _) =>
        {
            if (reconnect is null && ChosenWindow is { } w) name.Text = AppEntry.All.FirstOrDefault(a => a.Matches(w))?.Name ?? w.ProcessName;
            connect.Enabled = ChosenWindow is not null && !string.IsNullOrWhiteSpace(name.Text);
        };
        name.TextChanged += (_, _) => connect.Enabled = ChosenWindow is not null && !string.IsNullOrWhiteSpace(name.Text);
        var explanation = new Label { AutoSize = true, MaximumSize = new Size(680, 0),
            Text = "Saved connections stay in your sidebar while closed. Browser titles can change; explicitly reconnect to the right window when needed. AI Hub hosts the whole browser window.",
            ForeColor = UsageStyle.Muted, Font = new Font("Segoe UI", 9), Margin = new Padding(3, 8, 3, 12) };
        bottom.Controls.Add(explanation, 0, 1); bottom.SetColumnSpan(explanation, 2);
        int row = 2;
        if (savedBrowsers.Length > 0)
        {
            var saved = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(AppEntry.Name), AccessibleName = "Saved browser connections" };
            saved.Items.AddRange(savedBrowsers.Cast<object>().ToArray()); saved.SelectedIndex = 0; bottom.Controls.Add(saved, 0, row);
            var replace = UsageStyle.Button("Reconnect saved browser", () =>
            {
                if (ChosenWindow is not { } chosen || !AppEntry.IsBrowser(chosen.ProcessName)) return;
                ReconnectKey = ((AppEntry)saved.SelectedItem!).Key; DialogResult = DialogResult.OK;
            });
            replace.Dock = DockStyle.Top; replace.Enabled = false; bottom.Controls.Add(replace, 1, row++);
            windows.SelectedIndexChanged += (_, _) => replace.Enabled = ChosenWindow is { } chosen && AppEntry.IsBrowser(chosen.ProcessName);
        }
        if (added.Length > 0)
        {
            var current = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(AppEntry.Name), AccessibleName = "Apps in AI Hub" };
            current.Items.AddRange(added.Cast<object>().ToArray()); current.SelectedIndex = 0; bottom.Controls.Add(current, 0, row);
            var remove = UsageStyle.Button("Remove from AI Hub", () => { RemoveKey = ((AppEntry)current.SelectedItem!).Key; DialogResult = DialogResult.OK; });
            remove.Dock = DockStyle.Top; bottom.Controls.Add(remove, 1, row++);
        }
        if (removed.Length > 0)
        {
            var hidden = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(AppEntry.Name), AccessibleName = "Removed apps" };
            hidden.Items.AddRange(removed.Cast<object>().ToArray()); hidden.SelectedIndex = 0; bottom.Controls.Add(hidden, 0, row);
            var restore = UsageStyle.Button("Add back to AI Hub", () => { RestoreKey = ((AppEntry)hidden.SelectedItem!).Key; DialogResult = DialogResult.OK; });
            restore.Dock = DockStyle.Top; bottom.Controls.Add(restore, 1, row++);
        }
        var cancel = UsageStyle.Button("Cancel", () => DialogResult = DialogResult.Cancel); cancel.Dock = DockStyle.Top;
        bottom.Controls.Add(cancel, 1, row); CancelButton = cancel;
        Controls.Add(windows); Controls.Add(bottom); Controls.Add(intro); AcceptButton = connect;
        Shown += (_, _) => { int dark = 1; Native.DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); };
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96); PerformAutoScale();
    }
}
