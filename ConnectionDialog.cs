namespace AIHub;

internal sealed class ConnectionDialog : Form
{
    private readonly ListBox windows;
    private readonly TextBox name;
    internal WindowInfo? ChosenWindow => windows.SelectedItem as WindowInfo;
    internal string ConnectionName => name.Text;
    internal string? RemoveKey { get; private set; }
    internal string? RestoreKey { get; private set; }
    internal ConnectionDialog(WindowInfo[] candidates, AppEntry[] added, AppEntry[] removed)
    {
        Text = "Connect an AI"; Size = new Size(760, 650); MinimumSize = new Size(620, 560);
        StartPosition = FormStartPosition.CenterParent; BackColor = UsageStyle.Background; ForeColor = UsageStyle.Ink;
        Font = new Font("Segoe UI", 10); MinimizeBox = false; MaximizeBox = false; Padding = new Padding(24);
        var intro = new Label { Dock = DockStyle.Top, Height = 76, Text = "CHOOSE AN OPEN WINDOW\n\nOpen your AI app first. For a website, use a dedicated browser window. Your existing login stays in that app.", ForeColor = UsageStyle.Muted };
        windows = new ListBox { Dock = DockStyle.Fill, BackColor = UsageStyle.Surface, ForeColor = UsageStyle.Ink, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false, AccessibleName = "Open app windows" };
        windows.Items.AddRange(candidates.OrderBy(w => w.ProcessName).Cast<object>().ToArray());
        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 130 + (added.Length > 0 ? 48 : 0) + (removed.Length > 0 ? 48 : 0), ColumnCount = 2, Padding = new Padding(0, 10, 0, 0) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        name = new TextBox { Dock = DockStyle.Top, MaxLength = 40, PlaceholderText = "Name for this AI", AccessibleName = "AI connection name", BackColor = UsageStyle.Surface, ForeColor = UsageStyle.Ink };
        bottom.Controls.Add(name, 0, 0);
        var connect = UsageStyle.Button("Connect window", () => { if (ChosenWindow is not null && !string.IsNullOrWhiteSpace(name.Text)) DialogResult = DialogResult.OK; });
        connect.Dock = DockStyle.Top; connect.Enabled = false; bottom.Controls.Add(connect, 1, 0);
        windows.SelectedIndexChanged += (_, _) => { if (ChosenWindow is { } w) name.Text = AppEntry.All.FirstOrDefault(a => a.Matches(w))?.Name ?? w.ProcessName; connect.Enabled = ChosenWindow is not null; };
        bottom.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(500, 0), Text = "Desktop connections are remembered. Browser connections match the selected title; reconnect if it changes after closing the window.", ForeColor = UsageStyle.Muted, Font = new Font("Segoe UI", 9) }, 0, 1);
        int row = 2;
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
            restore.Dock = DockStyle.Top; bottom.Controls.Add(restore, 1, row);
        }
        Controls.Add(windows); Controls.Add(bottom); Controls.Add(intro); AcceptButton = connect;
        Shown += (_, _) => { int dark = 1; Native.DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); };
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96); PerformAutoScale();
    }
}
