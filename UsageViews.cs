using AIHub.Usage;
using System.Drawing.Drawing2D;

namespace AIHub;

internal static class UsageStyle
{
    internal static readonly Color Background = Color.FromArgb(7, 13, 23), Surface = Color.FromArgb(12, 23, 37),
        Ink = Color.FromArgb(228, 244, 255), Muted = Color.FromArgb(139, 165, 185), Line = Color.FromArgb(34, 63, 83),
        Good = Color.FromArgb(76, 222, 237), Warning = Color.FromArgb(255, 193, 104), Low = Color.FromArgb(255, 117, 144);
    internal static Color MeterColor(double? used) => used >= 90 ? Low : used >= 75 ? Warning : Good;
    internal static Bitmap Icon(string key, string? executable = null)
    {
        using var stream = typeof(AppInfo).Assembly.GetManifestResourceStream($"AIHub.Assets.Providers.{key}.png");
        if (stream is not null) { using var source = Image.FromStream(stream); return new Bitmap(source); }
        try { if (executable is not null && File.Exists(executable)) { using var icon = System.Drawing.Icon.ExtractAssociatedIcon(executable); if (icon is not null) return icon.ToBitmap(); } }
        catch (Exception ex) when (ex is IOException or ArgumentException or System.ComponentModel.Win32Exception) { }
        var bitmap = new Bitmap(64, 64);
        using var g = Graphics.FromImage(bitmap); g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Good, 2); g.DrawPolygon(pen, [new(32, 4), new(56, 18), new(56, 46), new(32, 60), new(8, 46), new(8, 18)]);
        Text(g, key.StartsWith("custom-") ? "+" : key[..1].ToUpperInvariant(), 23, Ink, new(0, 10, 64, 44), true);
        return bitmap;
    }
    internal static Button Button(string text, Action action)
    {
        var button = new Button { Text = text, UseMnemonic = false, AutoSize = false, Size = new Size(180, 36),
            FlatStyle = FlatStyle.Flat, BackColor = Surface, ForeColor = Ink, Cursor = Cursors.Hand, Padding = new Padding(10, 3, 10, 3) };
        button.FlatAppearance.BorderColor = Line; button.FlatAppearance.MouseOverBackColor = Color.FromArgb(20, 44, 61);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(27, 63, 80);
        button.Click += (_, _) => action(); return button;
    }
    internal static void Text(Graphics g, string text, float size, Color color, RectangleF bounds, bool center = false, bool bold = false, bool wrap = false)
    {
        using var font = new Font(bold ? "Segoe UI Semibold" : "Segoe UI", size, FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat { Alignment = center ? StringAlignment.Center : StringAlignment.Near,
            LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = wrap ? 0 : StringFormatFlags.NoWrap };
        g.DrawString(text, font, brush, bounds, format);
    }
    internal static void Ring(Graphics g, RectangleF bounds, double? used, Color color, bool ticks = true)
    {
        using var track = new Pen(Line, 5); g.DrawArc(track, bounds, 135, 270);
        if (used is { } value)
        {
            float sweep = (float)(270 * Math.Clamp(100 - value, 0, 100) / 100);
            if (sweep > 0)
            {
                using var glow = new Pen(Color.FromArgb(22, color), 14); g.DrawArc(glow, bounds, 135, sweep);
                using var light = new Pen(color, 5) { StartCap = LineCap.Round, EndCap = LineCap.Round }; g.DrawArc(light, bounds, 135, sweep);
            }
        }
        if (!ticks) return;
        float cx = bounds.X + bounds.Width / 2, cy = bounds.Y + bounds.Height / 2, radius = bounds.Width / 2 + 10;
        using var tick = new Pen(Line, 1);
        for (int i = 0; i <= 36; i++)
        {
            double a = (135 + i * 7.5) * Math.PI / 180;
            float length = i % 3 == 0 ? 6 : 3;
            g.DrawLine(tick, cx + (float)Math.Cos(a) * radius, cy + (float)Math.Sin(a) * radius,
                cx + (float)Math.Cos(a) * (radius + length), cy + (float)Math.Sin(a) * (radius + length));
        }
        using var orbit = new Pen(Color.FromArgb(40, color), 1); var inner = bounds; inner.Inflate(-9, -9); g.DrawArc(orbit, inner, 145, 250);
    }
    internal static string Health(UsageState state) => !state.Enabled ? "OFFLINE" : state.Reading is null
        ? state.Refreshing ? "CONNECTING" : "UNAVAILABLE" : state.IsStale(DateTimeOffset.Now) ? "STALE DATA" : state.Refreshing ? "REFRESHING" : "CONNECTED";
}

internal sealed class UsageChip : Button
{
    private readonly UsageProvider provider;
    private UsageState state;
    private readonly ToolTip hint = new() { InitialDelay = 300, AutoPopDelay = 12000 };
    internal UsageChip(UsageProvider provider, Action open)
    {
        this.provider = provider; state = new(provider.Id, true);
        Dock = DockStyle.Fill; Margin = Padding.Empty; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        BackColor = UsageStyle.Surface; ForeColor = UsageStyle.Ink; Cursor = Cursors.Hand;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Click += (_, _) => open();
    }
    internal void Update(UsageState value)
    {
        state = value;
        AccessibleName = provider.Name + ": " + UsageDisplay.Remaining(UsageDisplay.Primary(state.Reading)) + ". " + UsageStyle.Health(state) + ". Open usage overview.";
        AccessibleDescription = provider.Scope;
        hint.SetToolTip(this, provider.Scope + "\n" + (state.Error ?? UsageDisplay.ResetText(UsageDisplay.Primary(state.Reading)?.Reset, DateTimeOffset.Now)));
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96f; g.ScaleTransform(scale, scale); float w = Width / scale, h = Height / scale;
        var quota = UsageDisplay.Primary(state.Reading); bool stale = state.IsStale(DateTimeOffset.Now);
        Color color = stale ? UsageStyle.Warning : UsageStyle.MeterColor(quota?.Used);
        using var line = new Pen(UsageStyle.Line); g.DrawLine(line, 0, 0, w, 0); g.DrawLine(line, w - 1, 12, w - 1, h - 12);
        UsageStyle.Ring(g, new(17, (h - 24) / 2, 24, 24), quota?.Used, color, false);
        UsageStyle.Text(g, provider.Name.ToUpperInvariant(), 10, UsageStyle.Muted, new(54, 7, w - 64, 17), bold: true);
        string value = quota?.Used is not null ? UsageDisplay.Remaining(quota) + "  /  " + UsageDisplay.WindowLabel(quota) : state.Refreshing ? "Connecting…" : "Unavailable";
        UsageStyle.Text(g, value + (stale ? " · stale" : ""), 13, quota?.Used is null ? UsageStyle.Muted : color, new(54, 24, w - 64, 22));
        if (Focused) { using var focus = new Pen(UsageStyle.Good); g.DrawRectangle(focus, 2, 2, w - 5, h - 5); }
    }
    protected override void Dispose(bool disposing) { if (disposing) hint.Dispose(); base.Dispose(disposing); }
}

internal sealed class UsageStrip : TableLayoutPanel
{
    private readonly Dictionary<string, UsageChip> chips = [];
    private readonly Action open;
    internal UsageStrip(UsageProvider[] providers, Action open)
    {
        this.open = open; Dock = DockStyle.Bottom; Height = 56; BackColor = UsageStyle.Surface; Margin = Padding.Empty;
        RowCount = 1; RowStyles.Add(new RowStyle(SizeType.Percent, 100)); Configure(providers);
    }
    internal void Configure(UsageProvider[] providers)
    {
        if (chips.Keys.SequenceEqual(providers.Select(p => p.Id))) { Visible = providers.Length > 0; return; }
        SuspendLayout(); foreach (var chip in chips.Values) chip.Dispose(); chips.Clear(); ColumnStyles.Clear();
        ColumnCount = Math.Max(1, providers.Length);
        foreach (var provider in providers)
        {
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / providers.Length));
            var chip = new UsageChip(provider, open); chips.Add(provider.Id, chip); Controls.Add(chip, chips.Count - 1, 0);
        }
        Visible = providers.Length > 0; ResumeLayout();
    }
    internal void Update(UsageState[] states) { foreach (var state in states) if (chips.TryGetValue(state.Id, out var chip)) chip.Update(state); }
}

internal sealed class UsageCard : Control
{
    private readonly UsageProvider provider;
    private readonly Bitmap icon;
    private readonly ToolTip hint = new() { InitialDelay = 500, AutoPopDelay = 15000 };
    private UsageState state;
    internal UsageCard(UsageProvider provider)
    {
        this.provider = provider; state = new(provider.Id, true); icon = UsageStyle.Icon(provider.AppKey);
        Size = new Size(300, 490); BackColor = UsageStyle.Surface; Margin = new Padding(0, 0, 16, 16);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AccessibleRole = AccessibleRole.Grouping;
    }
    internal void Update(UsageState value)
    {
        state = value;
        Height = LogicalToDeviceUnits(330 + Math.Max(1, state.Reading?.Quotas.Length ?? 0) * 52);
        AccessibleName = provider.Name + " usage. " + UsageStyle.Health(state) + ". " + UsageDisplay.Remaining(UsageDisplay.Primary(state.Reading));
        AccessibleDescription = provider.Scope + ". " + (state.Error ?? string.Join(". ", state.Reading?.Quotas.Select(q => q.Label + ": " + UsageDisplay.Remaining(q) + ", " + UsageDisplay.ResetText(q.Reset, DateTimeOffset.Now)) ?? []));
        hint.SetToolTip(this, AccessibleDescription);
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; float scale = DeviceDpi / 96f; g.ScaleTransform(scale, scale);
        float w = Width / scale, h = Height / scale;
        using var bg = new LinearGradientBrush(new RectangleF(0, 0, w, h), Color.FromArgb(15, 30, 46), UsageStyle.Surface, 90f);
        g.FillRectangle(bg, 0, 0, w, h);
        using var border = new Pen(UsageStyle.Line); g.DrawRectangle(border, .5f, .5f, w - 1, h - 1);
        bool stale = state.IsStale(DateTimeOffset.Now); var quota = UsageDisplay.Primary(state.Reading);
        var color = stale ? UsageStyle.Warning : UsageStyle.MeterColor(quota?.Used);
        using var accent = new Pen(color, 2); g.DrawLine(accent, 0, 18, 0, 0); g.DrawLine(accent, 0, 0, 32, 0);
        g.DrawLine(accent, w - 32, h - 1, w - 1, h - 1); g.DrawLine(accent, w - 1, h - 1, w - 1, h - 18);
        g.DrawImage(icon, new RectangleF(20, 21, 27, 27));
        UsageStyle.Text(g, provider.Name, 19, UsageStyle.Ink, new(58, 15, w - 78, 29), bold: true);
        UsageStyle.Text(g, UsageStyle.Health(state), 9, state.Reading is null || stale ? UsageStyle.Warning : color, new(59, 44, w - 79, 17), bold: true);
        UsageStyle.Text(g, provider.Scope, 11, UsageStyle.Muted, new(20, 68, w - 40, 36), wrap: true);
        const float diameter = 124; var ring = new RectangleF((w - diameter) / 2, 112, diameter, diameter);
        UsageStyle.Ring(g, ring, quota?.Used, color);
        UsageStyle.Text(g, quota?.Used is { } used ? $"{Math.Clamp(100 - used, 0, 100):0}%" : "—", 36, quota?.Used is null ? UsageStyle.Muted : UsageStyle.Ink, new(20, 139, w - 40, 52), true, true);
        UsageStyle.Text(g, quota?.Used is null ? "NO READING" : "REMAINING", 9, color, new(20, 190, w - 40, 18), true, true);
        UsageStyle.Text(g, quota?.Label ?? "Awaiting provider", 11, UsageStyle.Muted, new(20, 226, w - 40, 25), true);
        float y = 265;
        foreach (var q in state.Reading?.Quotas ?? [new Quota("Allowance", null, null)])
        {
            UsageStyle.Text(g, q.Label, 12, UsageStyle.Ink, new(20, y, w - 144, 20), bold: true);
            UsageStyle.Text(g, UsageDisplay.Remaining(q), 12, q.Used is null ? UsageStyle.Muted : color, new(w - 130, y, 110, 20), center: true);
            using var track = new SolidBrush(UsageStyle.Line); g.FillRectangle(track, 20, y + 23, w - 40, 3);
            if (q.Used is { } amount) { using var fill = new SolidBrush(stale ? UsageStyle.Warning : UsageStyle.MeterColor(amount)); g.FillRectangle(fill, 20, y + 23, (w - 40) * (float)Math.Clamp(100 - amount, 0, 100) / 100, 3); }
            UsageStyle.Text(g, q.Reset is null && state.Reading is null ? "Waiting for a successful connection" : UsageDisplay.ResetText(q.Reset, DateTimeOffset.Now), 10, UsageStyle.Muted, new(20, y + 29, w - 40, 20));
            y += 52;
        }
        g.DrawLine(border, 20, y + 1, w - 20, y + 1);
        string detail = state.Error ?? (state.Reading is null ? provider.ConnectionHelp : string.Join(" · ", new[] { state.Reading.Balance,
            state.Reading.FreeResets is { } free ? $"{free} free resets" : null }.Concat(state.Reading.Quotas.Select(q => q.Note)).Where(t => !string.IsNullOrWhiteSpace(t))));
        UsageStyle.Text(g, detail, 11, state.Error is null ? UsageStyle.Muted : UsageStyle.Warning, new(20, y + 6, w - 40, 35), wrap: true);
        UsageStyle.Text(g, state.Reading is { } reading ? "LAST SYNC  " + reading.Fetched.LocalDateTime.ToString("HH:mm:ss") : "AWAITING SIGNAL", 9, UsageStyle.Muted, new(20, h - 22, w - 40, 18), bold: true);
    }
    protected override void Dispose(bool disposing) { if (disposing) { icon.Dispose(); hint.Dispose(); } base.Dispose(disposing); }
}

internal sealed class UsagePage : UserControl
{
    private readonly FlowLayoutPanel cards = new() { AutoSize = true, Dock = DockStyle.Top, Margin = Padding.Empty, WrapContents = true };
    private readonly Dictionary<string, UsageCard> views = [];
    private readonly Button refresh;
    private readonly Label empty = new() { Text = "No usage monitors connected.\nAvailable monitors appear here when their apps or CLI logins are detected.\nConnect an AI from the sidebar to bring its window into your workspace.", AutoSize = true, ForeColor = UsageStyle.Muted, Margin = new Padding(0, 22, 0, 26) };
    private readonly Label summary = new() { AutoSize = true, ForeColor = UsageStyle.Good, Margin = new Padding(0, 4, 0, 12) };
    internal UsagePage(UsageProvider[] providers, Action refresh, Action back, Action settings)
    {
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
        Dock = DockStyle.Fill; BackColor = UsageStyle.Background; ForeColor = UsageStyle.Ink; Font = new Font("Segoe UI", 10); AutoScroll = true; Padding = new Padding(28);
        var stack = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = Padding.Empty };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stack.Controls.Add(new Label { Text = "A I  H U B    /    R E S O U R C E S", AutoSize = true, Font = new Font("Segoe UI Semibold", 8), ForeColor = UsageStyle.Good, Margin = new Padding(0, 0, 0, 7) });
        stack.Controls.Add(new Label { Text = "Usage command center", AutoSize = true, Font = new Font("Segoe UI Semibold", 26), Margin = new Padding(0, 0, 0, 5) });
        stack.Controls.Add(summary);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 0, 0, 16) };
        this.refresh = UsageStyle.Button("Refresh readings", refresh); toolbar.Controls.Add(this.refresh);
        toolbar.Controls.Add(UsageStyle.Button("Monitors & alerts", settings)); toolbar.Controls.Add(UsageStyle.Button("Back to workspace", back));
        stack.Controls.Add(toolbar); stack.Controls.Add(cards); stack.Controls.Add(empty);
        stack.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(900, 0), Text = "Each monitor covers the product named on its card. ChatGPT, Dot and Grok web limits are separate.\nUnknown readings stay unavailable; stale readings retain their last successful sync time.", ForeColor = UsageStyle.Muted, Font = new Font("Segoe UI", 9), Margin = new Padding(0, 6, 0, 18) });
        Controls.Add(stack); cards.SizeChanged += (_, _) => ArrangeCards();
        Configure(providers); AutoScaleDimensions = new SizeF(96, 96); PerformAutoScale();
    }
    internal void Configure(UsageProvider[] providers)
    {
        summary.Text = $"{providers.Length:00} MONITORS  /  REMAINING ALLOWANCE  /  60s REFRESH";
        if (views.Keys.SequenceEqual(providers.Select(p => p.Id))) { empty.Visible = providers.Length == 0; return; }
        cards.SuspendLayout(); foreach (var view in views.Values) view.Dispose(); views.Clear();
        foreach (var provider in providers) { var card = new UsageCard(provider); views.Add(provider.Id, card); cards.Controls.Add(card); }
        empty.Visible = providers.Length == 0;
        cards.ResumeLayout(); ArrangeCards();
    }
    private void ArrangeCards()
    {
        if (views.Count == 0) return;
        int gap = LogicalToDeviceUnits(16), width = Math.Max(1, cards.ClientSize.Width);
        int columns = Math.Clamp((width + gap) / LogicalToDeviceUnits(290), 1, Math.Min(3, views.Count));
        int cardWidth = Math.Max(LogicalToDeviceUnits(250), (width - gap * (columns - 1)) / columns - 1);
        int index = 0;
        foreach (var card in views.Values)
        {
            card.Margin = new Padding(0, 0, ++index % columns == 0 ? 0 : gap, gap);
            if (card.Width != cardWidth) card.Width = cardWidth;
        }
    }
    internal void Update(UsageState[] states, bool busy)
    {
        refresh.Enabled = !busy; refresh.Text = busy ? "Refreshing…" : "Refresh readings";
        foreach (var state in states) if (views.TryGetValue(state.Id, out var view)) view.Update(state);
    }
}
