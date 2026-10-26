using AIHub.Usage;

namespace AIHub;

internal static class UsageStyle
{
    internal static readonly Color Background = Color.FromArgb(19, 22, 27), Surface = Color.FromArgb(26, 30, 36),
        Ink = Color.FromArgb(236, 240, 244), Muted = Color.FromArgb(151, 160, 172),
        Good = Color.FromArgb(121, 201, 179), Warning = Color.FromArgb(239, 188, 100), Low = Color.FromArgb(245, 130, 139);
    internal static Color MeterColor(double? used) => used >= 90 ? Low : used >= 75 ? Warning : Good;
    internal static Bitmap Icon(string key)
    {
        using var stream = typeof(AppInfo).Assembly.GetManifestResourceStream($"AIHub.Assets.Providers.{key}.png")!;
        using var source = Image.FromStream(stream);
        return new Bitmap(source);
    }
    internal static Button Button(string text, Action action)
    {
        var button = new Button { Text = text, UseMnemonic = false, AutoSize = true, MinimumSize = new Size(100, 34),
            FlatStyle = FlatStyle.Flat, BackColor = Surface, ForeColor = Ink, Cursor = Cursors.Hand, Padding = new Padding(8, 2, 8, 2) };
        button.FlatAppearance.BorderColor = Color.FromArgb(57, 64, 73);
        button.Click += (_, _) => action();
        return button;
    }
}

internal sealed class UsageChip : Button
{
    private readonly Bitmap icon;
    private readonly UsageProvider provider;
    private UsageState state;
    private readonly ToolTip hint;
    internal UsageChip(UsageProvider provider, Action open)
    {
        this.provider = provider;
        state = new(provider.Id, true);
        icon = UsageStyle.Icon(provider.AppKey);
        Dock = DockStyle.Fill; Margin = Padding.Empty;
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseOverBackColor = Color.FromArgb(47, 55, 65);
        BackColor = UsageStyle.Surface; ForeColor = UsageStyle.Ink;
        Font = new Font("Segoe UI", 9); Cursor = Cursors.Hand;
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        hint = new ToolTip { InitialDelay = 300, AutoPopDelay = 12000 };
        Click += (_, _) => open();
    }
    internal void Update(UsageState value)
    {
        state = value;
        var quota = UsageDisplay.Primary(state.Reading);
        string valueText = !state.Enabled ? "Off" : state.Reading is null ? state.Refreshing ? "Connecting…" : "Unavailable"
            : UsageDisplay.Remaining(quota) + " · " + UsageDisplay.WindowLabel(quota) + (state.IsStale(DateTimeOffset.Now) ? " · stale" : "");
        AccessibleName = provider.Name + ": " + valueText + ". Open usage overview.";
        AccessibleDescription = provider.Scope;
        hint.SetToolTip(this, provider.Scope + "\n" + (state.Error ?? UsageDisplay.ResetText(quota?.Reset, DateTimeOffset.Now)) +
            (state.Reading is { } reading ? $"\nLast successful update: {reading.Fetched.LocalDateTime:g}" : "\n" + provider.ConnectionHelp));
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        int pad = LogicalToDeviceUnits(10), size = LogicalToDeviceUnits(17), gap = LogicalToDeviceUnits(7);
        e.Graphics.DrawImage(icon, new Rectangle(pad, (Height - size) / 2, size, size));
        var quota = UsageDisplay.Primary(state.Reading);
        string detail = !state.Enabled ? "Off" : state.Reading is null ? state.Refreshing ? "Connecting…" : "Unavailable"
            : UsageDisplay.Remaining(quota) + " · " + UsageDisplay.WindowLabel(quota) + (state.IsStale(DateTimeOffset.Now) ? " · stale" : "");
        int meterWidth = Width >= LogicalToDeviceUnits(270) && state.Enabled && quota?.Used is not null ? LogicalToDeviceUnits(30) : 0;
        var textBounds = new Rectangle(pad + size + gap, 0, Math.Max(0, Width - pad * 2 - size - gap - meterWidth - (meterWidth > 0 ? gap : 0)), Height);
        TextRenderer.DrawText(e.Graphics, provider.Name + "  " + detail, Font, textBounds,
            !state.Enabled ? UsageStyle.Muted : state.IsStale(DateTimeOffset.Now) ? UsageStyle.Warning : ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        if (meterWidth > 0)
        {
            var bar = new Rectangle(Width - pad - meterWidth, (Height - LogicalToDeviceUnits(4)) / 2, meterWidth, LogicalToDeviceUnits(4));
            using var track = new SolidBrush(Color.FromArgb(57, 64, 73)); e.Graphics.FillRectangle(track, bar);
            bar.Width = (int)Math.Round(bar.Width * Math.Clamp(100 - quota!.Used!.Value, 0, 100) / 100);
            using var fill = new SolidBrush(state.IsStale(DateTimeOffset.Now) ? UsageStyle.Muted : UsageStyle.MeterColor(quota.Used));
            e.Graphics.FillRectangle(fill, bar);
        }
    }
    protected override void Dispose(bool disposing) { if (disposing) { icon.Dispose(); hint.Dispose(); } base.Dispose(disposing); }
}

internal sealed class UsageStrip : TableLayoutPanel
{
    private readonly Dictionary<string, UsageChip> chips = new();
    internal UsageStrip(UsageProvider[] providers, Action open)
    {
        Dock = DockStyle.Bottom; Height = 36; BackColor = UsageStyle.Surface; Margin = Padding.Empty;
        ColumnCount = providers.Length; RowCount = 1; RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        foreach (var provider in providers)
        {
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / providers.Length));
            var chip = new UsageChip(provider, open); chips.Add(provider.Id, chip);
            Controls.Add(chip, chips.Count - 1, 0);
        }
    }
    internal void Update(UsageState[] states) { foreach (var state in states) chips[state.Id].Update(state); }
}

internal sealed class UsageBar : Control
{
    internal double? Used;
    internal bool Stale;
    internal UsageBar() { Height = 6; SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        using var track = new SolidBrush(Color.FromArgb(57, 64, 73)); e.Graphics.FillRectangle(track, ClientRectangle);
        if (Used is not { } used) return;
        using var fill = new SolidBrush(Stale ? UsageStyle.Muted : UsageStyle.MeterColor(used));
        e.Graphics.FillRectangle(fill, 0, 0, (float)(Width * Math.Clamp(100 - used, 0, 100) / 100), Height);
    }
}

internal sealed class UsageCard : Panel
{
    private readonly UsageProvider provider;
    private readonly PictureBox picture;
    private readonly Label scope, health, balance, resets, error;
    private readonly (Label Value, UsageBar Bar, Label Reset)[] quotas;
    internal UsageCard(UsageProvider provider)
    {
        this.provider = provider;
        BackColor = UsageStyle.Surface; ForeColor = UsageStyle.Ink; Font = new Font("Segoe UI", 10);
        Size = new Size(280, 400); Margin = new Padding(0, 0, 14, 14);
        picture = new PictureBox { Image = UsageStyle.Icon(provider.AppKey), Location = new Point(16, 18), Size = new Size(28, 28), SizeMode = PictureBoxSizeMode.Zoom };
        Controls.Add(picture);
        Controls.Add(new Label { Text = provider.Name, Location = new Point(54, 16), Size = new Size(200, 24), Font = new Font("Segoe UI Semibold", 13) });
        scope = Label(provider.Scope, 16, 56, 248, 40, UsageStyle.Muted);
        health = Label("Connecting…", 16, 98, 248, 24, UsageStyle.Muted);
        quotas = Enumerable.Range(0, provider.Id == "codex" ? 2 : 1).Select(index =>
        {
            int y = 124 + index * 64;
            var value = Label("", 16, y, 248, 22, UsageStyle.Ink);
            var bar = new UsageBar { Location = new Point(16, y + 27), Size = new Size(248, 6), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            Controls.Add(bar);
            return (value, bar, Label("", 16, y + 39, 248, 24, UsageStyle.Muted));
        }).ToArray();
        int after = 124 + quotas.Length * 64;
        balance = Label("", 16, after, 248, 28, UsageStyle.Ink);
        resets = Label("", 16, after + 30, 248, 24, UsageStyle.Muted);
        error = Label("", 16, after + 60, 248, 64, UsageStyle.Muted);
        Height = after + 136;
    }
    private Label Label(string text, int x, int y, int width, int height, Color color)
    {
        var label = new Label { Text = text, UseMnemonic = false, Location = new Point(x, y), Size = new Size(width, height), ForeColor = color,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right };
        Controls.Add(label); return label;
    }
    internal void Update(UsageState state)
    {
        var now = DateTimeOffset.Now;
        bool stale = state.IsStale(now);
        health.Text = !state.Enabled ? "Monitoring off" : state.Reading is null ? state.Refreshing ? "Connecting…" : "Unavailable"
            : (stale ? "Stale · " : "Updated · ") + state.Reading.Fetched.LocalDateTime.ToString("HH:mm:ss") + (state.Refreshing ? " · refreshing" : "");
        health.ForeColor = !state.Enabled ? UsageStyle.Muted : stale || state.Error is not null ? UsageStyle.Warning : UsageStyle.Good;
        for (int i = 0; i < quotas.Length; i++)
        {
            var q = state.Enabled && state.Reading is { } r && i < r.Quotas.Length ? r.Quotas[i] : null;
            quotas[i].Value.Text = (q?.Label ?? (provider.Id == "codex" ? i == 0 ? "5 hours" : "Weekly" : "Weekly")) + "   ·   " + (state.Enabled ? UsageDisplay.Remaining(q) : "Off");
            quotas[i].Bar.Used = q?.Used; quotas[i].Bar.Stale = stale; quotas[i].Bar.Invalidate();
            quotas[i].Reset.Text = q is null ? "" : UsageDisplay.ResetText(q.Reset, now);
        }
        balance.Text = state.Enabled ? state.Reading?.Balance ?? "" : "";
        resets.Text = state.Enabled && state.Reading?.FreeResets is { } available ? $"Free resets available: {available}" : "";
        error.Text = !state.Enabled ? "Enable this provider in Settings." : state.Error ??
            (state.Reading is null ? provider.ConnectionHelp : string.Join("\n", state.Reading.Quotas.Where(q => q.Note is not null).Select(q => q.Note)));
        AccessibleName = provider.Name + " usage";
    }
    protected override void Dispose(bool disposing) { if (disposing) picture.Image?.Dispose(); base.Dispose(disposing); }
}

internal sealed class UsagePage : UserControl
{
    private readonly TableLayoutPanel cards = new() { AutoSize = true, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
    private readonly Dictionary<string, UsageCard> views = new();
    private readonly Button refresh;
    internal UsagePage(UsageProvider[] providers, Action refresh, Action back, Action settings)
    {
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
        Dock = DockStyle.Fill; BackColor = UsageStyle.Background; ForeColor = UsageStyle.Ink;
        Font = new Font("Segoe UI", 10); AutoScroll = true; Padding = new Padding(24);
        var stack = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 5 };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 0, 0, 14), WrapContents = true };
        toolbar.Controls.Add(new Label { Text = "Usage", AutoSize = true, Font = new Font("Segoe UI Semibold", 24), Margin = new Padding(0, 0, 20, 0) });
        this.refresh = UsageStyle.Button("Refresh", refresh); toolbar.Controls.Add(this.refresh);
        toolbar.Controls.Add(UsageStyle.Button("Connections & alerts", settings));
        toolbar.Controls.Add(UsageStyle.Button("Back to workspace", back));
        stack.Controls.Add(toolbar, 0, 0);
        stack.Controls.Add(new Label { AutoSize = true, Text = "Remaining allowance · refreshes every minute · stale readings keep their last timestamp",
            ForeColor = UsageStyle.Muted, Margin = new Padding(0, 0, 0, 18) }, 0, 1);
        cards.Dock = DockStyle.Top;
        foreach (var provider in providers)
        {
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / providers.Length));
            var card = new UsageCard(provider) { Dock = DockStyle.Fill };
            views.Add(provider.Id, card); cards.Controls.Add(card, views.Count - 1, 0);
        }
        stack.Controls.Add(cards, 0, 2);
        stack.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(900, 0),
            Text = "ChatGPT, Dot and Grok web limits are not covered by these readers.\nThe strip shows each provider's most constrained reported window; unavailable is never treated as zero usage.",
            ForeColor = UsageStyle.Muted, Margin = new Padding(0, 8, 0, 12) }, 0, 3);
        Controls.Add(stack);
        AutoScaleDimensions = new SizeF(96, 96); PerformAutoScale();
    }
    internal void Update(UsageState[] states, bool busy)
    {
        refresh.Enabled = !busy;
        refresh.Text = busy ? "Refreshing…" : "Refresh";
        foreach (var state in states) views[state.Id].Update(state);
    }
}
