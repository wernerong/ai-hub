using System.Runtime.InteropServices;

namespace AIHub;

internal sealed record HubCommand(string Id, string Title, string Detail, string Keywords)
{
    public override string ToString() => string.IsNullOrWhiteSpace(Detail) ? Title : Title + " — " + Detail;
}

internal sealed class CommandPaletteDialog : Form
{
    private readonly HubCommand[] commands;
    private readonly TextBox search;
    private readonly ListBox results;
    private readonly Label count;
    private readonly Button run;
    internal string? SelectedCommandId { get; private set; }

    internal CommandPaletteDialog(IEnumerable<HubCommand> commands)
    {
        this.commands = commands.ToArray();
        Text = "AI Hub commands";
        AccessibleName = "AI Hub command palette";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(720, 460);
        MinimumSize = new Size(560, 360);
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        BackColor = UsageStyle.Background; ForeColor = UsageStyle.Ink;
        Font = new Font("Segoe UI", 10); Padding = new Padding(22);

        var header = new Panel { Dock = DockStyle.Top, Height = 96 };
        header.Controls.Add(new Label { Text = "COMMAND CENTER", Dock = DockStyle.Top, Height = 32,
            ForeColor = UsageStyle.Good, Font = new Font("Segoe UI Semibold", 10), AutoEllipsis = true });
        search = new TextBox { Dock = DockStyle.Bottom, PlaceholderText = "Search projects, apps and commands…",
            BackColor = UsageStyle.Surface, ForeColor = UsageStyle.Ink, Font = new Font("Segoe UI", 14),
            BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Search AI Hub commands", MaxLength = 200 };
        header.Padding = new Padding(0, 0, 0, 14); header.Controls.Add(search);

        results = new ListBox { Dock = DockStyle.Fill, BackColor = UsageStyle.Surface, ForeColor = UsageStyle.Ink,
            BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 58, AccessibleName = "Available commands", AccessibleDescription = "Use arrow keys to choose a command, then press Enter." };
        results.DrawItem += DrawCommand;
        results.MouseDoubleClick += (_, e) => { if (results.IndexFromPoint(e.Location) >= 0) Choose(); };

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 62, Padding = new Padding(0, 12, 0, 0) };
        run = UsageStyle.Button("Run command", Choose); run.Dock = DockStyle.Right; run.Width = 150;
        run.AccessibleName = "Run selected command";
        results.SelectedIndexChanged += (_, _) => run.Enabled = results.SelectedItem is HubCommand;
        var cancel = UsageStyle.Button("Cancel", () => DialogResult = DialogResult.Cancel);
        cancel.Dock = DockStyle.Right; cancel.Width = 95; cancel.DialogResult = DialogResult.Cancel;
        count = new Label { Dock = DockStyle.Fill, ForeColor = UsageStyle.Muted, TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true, AccessibleName = "Command search results" };
        footer.Controls.Add(count); footer.Controls.Add(cancel); footer.Controls.Add(run);
        Controls.Add(results); Controls.Add(footer); Controls.Add(header);
        AcceptButton = run; CancelButton = cancel;
        search.TextChanged += (_, _) => UpdateResults();
        search.KeyDown += (_, e) =>
        {
            if (e.KeyCode is not (Keys.Down or Keys.Up)) return;
            if (results.Items.Count > 0)
                results.SelectedIndex = Math.Clamp(results.SelectedIndex + (e.KeyCode == Keys.Down ? 1 : -1), 0, results.Items.Count - 1);
            e.Handled = true; e.SuppressKeyPress = true;
        };
        UpdateResults();
        Shown += (_, _) =>
        {
            int dark = 1; Native.DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
            results.ItemHeight = (int)Math.Round(58 * DeviceDpi / 96f);
            search.Focus();
        };
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96); PerformAutoScale();
        DpiChanged += (_, _) => results.ItemHeight = (int)Math.Round(58 * DeviceDpi / 96f);
    }

    // Callers supply only commands currently available to the user. Searching
    // never launches an app, reads provider conversations or changes state.
    internal static HubCommand[] Filter(IEnumerable<HubCommand> commands, string query)
    {
        string[] terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return commands.Where(command => terms.All(term =>
            command.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
            || command.Detail.Contains(term, StringComparison.OrdinalIgnoreCase)
            || command.Keywords.Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    private void UpdateResults()
    {
        string? previous = (results.SelectedItem as HubCommand)?.Id;
        var matches = Filter(commands, search.Text);
        results.BeginUpdate();
        results.Items.Clear(); results.Items.AddRange(matches.Cast<object>().ToArray());
        if (matches.Length > 0) results.SelectedIndex = Math.Max(0, Array.FindIndex(matches, c => c.Id == previous));
        results.EndUpdate();
        run.Enabled = matches.Length > 0;
        count.Text = matches.Length == 0 ? "No matching commands" : $"{matches.Length} commands  ·  ↑ ↓ Choose  ·  Enter Run";
    }

    private void Choose()
    {
        if (results.SelectedItem is not HubCommand command) return;
        SelectedCommandId = command.Id;
        DialogResult = DialogResult.OK;
    }

    private void DrawCommand(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || results.Items[e.Index] is not HubCommand command) return;
        bool chosen = (e.State & DrawItemState.Selected) != 0;
        using var background = new SolidBrush(chosen ? Color.FromArgb(20, 49, 65) : UsageStyle.Surface);
        e.Graphics.FillRectangle(background, e.Bounds);
        int inset = (int)Math.Round(12 * DeviceDpi / 96f);
        var bounds = new Rectangle(e.Bounds.Left + inset, e.Bounds.Top + 5, Math.Max(1, e.Bounds.Width - inset * 2), e.Bounds.Height / 2);
        TextRenderer.DrawText(e.Graphics, command.Title, Font, bounds, chosen ? UsageStyle.Good : UsageStyle.Ink,
            TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        bounds.Y += e.Bounds.Height / 2 - 4;
        TextRenderer.DrawText(e.Graphics, command.Detail, Font, bounds, UsageStyle.Muted,
            TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
    }
}

// RegisterHotKey reserves only the explicitly configured shortcut. A second
// ID lets a replacement fail without discarding the working shortcut.
internal sealed class CommandShortcut : IDisposable
{
    internal const int HotKeyMessage = 0x0312;
    private const int FirstId = 0xA160, SecondId = 0xA161;
    private readonly nint hub;
    private bool registered, disposed;
    private Keys key, modifiers;
    internal int MessageId { get; private set; } = FirstId;
    internal CommandShortcut(nint hub) => this.hub = hub;

    internal bool TryRegister(Keys key, Keys modifiers)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (key == Keys.None)
        {
            if (registered) UnregisterHotKey(hub, MessageId);
            registered = false; this.key = Keys.None; this.modifiers = Keys.None;
            return true;
        }
        if (!IsSupported(key, modifiers)) return false;
        if (registered && this.key == key && this.modifiers == modifiers) return true;
        int nextId = registered && MessageId == FirstId ? SecondId : FirstId;
        uint nativeModifiers = 0x4000; // MOD_NOREPEAT
        if ((modifiers & Keys.Alt) != 0) nativeModifiers |= 0x0001;
        if ((modifiers & Keys.Control) != 0) nativeModifiers |= 0x0002;
        if ((modifiers & Keys.Shift) != 0) nativeModifiers |= 0x0004;
        if (!RegisterHotKey(hub, nextId, nativeModifiers, (uint)key)) return false;
        if (registered) UnregisterHotKey(hub, MessageId);
        MessageId = nextId; registered = true; this.key = key; this.modifiers = modifiers;
        return true;
    }

    internal static bool IsSupported(Keys key, Keys modifiers) => key is Keys.Space or Keys.K or Keys.P
        && modifiers == (Keys.Control | Keys.Alt);

    public void Dispose()
    {
        if (disposed) return;
        if (registered) UnregisterHotKey(hub, MessageId);
        registered = false; disposed = true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint window, int id);
}

internal sealed class ShortcutDialog : Form
{
    private sealed record Choice(string Name, Keys Key) { public override string ToString() => Name; }
    private readonly ComboBox choice;
    internal Keys SelectedKey => (choice.SelectedItem as Choice)?.Key ?? Keys.None;
    internal ShortcutDialog(Keys current)
    {
        Text = "Command shortcut"; StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 235); FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
        BackColor = UsageStyle.Background; ForeColor = UsageStyle.Ink; Font = new Font("Segoe UI", 10); Padding = new Padding(22);
        var info = new Label { Dock = DockStyle.Top, Height = 75, ForeColor = UsageStyle.Muted,
            Text = "Open AI Hub commands while another app is focused.\nIf another app uses the shortcut, choose a different one.\nCtrl+K also opens commands when AI Hub has focus." };
        choice = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Global command shortcut", BackColor = UsageStyle.Surface, ForeColor = UsageStyle.Ink };
        Choice[] choices = [new("Ctrl+Alt+Space", Keys.Space), new("Ctrl+Alt+K", Keys.K), new("Ctrl+Alt+P", Keys.P), new("Disabled", Keys.None)];
        choice.Items.AddRange(choices); choice.SelectedIndex = Math.Max(0, Array.FindIndex(choices, c => c.Key == current));
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft };
        var save = UsageStyle.Button("Save shortcut", () => DialogResult = DialogResult.OK);
        var cancel = UsageStyle.Button("Cancel", () => DialogResult = DialogResult.Cancel); cancel.DialogResult = DialogResult.Cancel;
        footer.Controls.Add(save); footer.Controls.Add(cancel);
        Controls.Add(choice); Controls.Add(info); Controls.Add(footer); AcceptButton = save; CancelButton = cancel;
        Shown += (_, _) => { int dark = 1; Native.DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); };
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96); PerformAutoScale();
    }
}
