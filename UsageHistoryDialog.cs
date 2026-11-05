using AIHub.Usage;
using System.Drawing.Drawing2D;

namespace AIHub;

internal sealed class UsageHistoryDialog : Form
{
    private sealed record Series(string ProviderId, string Label, string Name, string Scope)
    { public override string ToString() => Name + " / " + Label; }
    private readonly UsageHistoryStore store;
    private readonly UsageProvider[] providers;
    private readonly Func<UsageState[]> currentStates;
    private readonly ComboBox series;
    private readonly Label summary, scope, retention;
    private readonly HistoryChart chart;
    private readonly ListView timeline;
    private readonly CheckBox record;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 5000 };
    private bool updating;
    private bool fittingTimeline;
    private long displayedRevision = -1;
    private UsageHistorySample[] cachedSamples = [], displayedPoints = [];
    private (string Id, string Label)[] historyKeys = [], displayedKeys = [];
    private (string Id, string Label)? displayedSeries;

    internal static void ShowPreview(string folder)
    {
        // A new isolated directory prevents previews from reading or replacing
        // either real history or an earlier preview. Readers are never called.
        var previewFolder = Path.Combine(Path.GetFullPath(folder), "history-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(previewFolder);
        var store = new UsageHistoryStore(Path.Combine(previewFolder, "usage-history.json"));
        var now = DateTimeOffset.Now;
        UsageProvider[] providers = [
            new("codex", "Codex", "openai", "Sample Codex allowance · separate from ChatGPT and Dot", "Synthetic preview",
                _ => Task.FromException<Reading>(new InvalidOperationException("Preview reader must never run."))),
            new("grok", "Grok CLI", "grok", "Sample CLI / Build allowance · not Grok web chat", "Synthetic preview",
                _ => Task.FromException<Reading>(new InvalidOperationException("Preview reader must never run."))),
            new("grokbot", "Grok Bot", "grokbot", "Sample Grok Bot desktop allowance", "Synthetic preview",
                _ => Task.FromException<Reading>(new InvalidOperationException("Preview reader must never run.")))
        ];
        UsageState[] states = [];
        for (int i = 0; i <= 8; i++)
        {
            var fetched = now.AddMinutes(-20 + i * 2.5);
            states = [
                new("codex", true, new("Codex", [new("5 hours", 30 + i * 4.5, now.AddHours(3)),
                    new("Weekly", 45 + i * .125, now.AddDays(3))], "", fetched)),
                new("grok", true, new("Grok CLI", [new("Weekly", 22 + i * .25, now.AddHours(6))], "", fetched)),
                new("grokbot", true, new("Grok Bot", [new("Weekly", 70 + i * .25, now.AddDays(2))], "", fetched))
            ];
            store.Record(states, fetched);
        }
        using var dialog = new UsageHistoryDialog(store, providers, () => states)
            { Text = "Usage history — PREVIEW / SYNTHETIC DATA", ShowInTaskbar = true, StartPosition = FormStartPosition.CenterScreen };
        Application.Run(dialog);
    }

    internal UsageHistoryDialog(UsageHistoryStore store, UsageProvider[] providers, Func<UsageState[]> currentStates)
    {
        this.store = store; this.providers = providers; this.currentStates = currentStates;
        Text = "Usage history"; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        ClientSize = new Size(940, 740); MinimumSize = new Size(760, 660); MinimizeBox = false;
        BackColor = UsageStyle.Background; ForeColor = UsageStyle.Ink; Font = new Font("Segoe UI", 10); Padding = new Padding(22);
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        shell.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (float height in new[] { 38f, 49f, 100f }) shell.RowStyles.Add(new(SizeType.Absolute, height));
        shell.RowStyles.Add(new(SizeType.Percent, 100)); shell.RowStyles.Add(new(SizeType.Absolute, 128)); shell.RowStyles.Add(new(SizeType.Absolute, 105));
        shell.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "USAGE HISTORY  /  LOCAL OBSERVATIONS", ForeColor = UsageStyle.Good,
            Font = new Font("Segoe UI Semibold", 13), AutoEllipsis = true }, 0, 0);
        var selector = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        selector.ColumnStyles.Add(new(SizeType.Absolute, 260)); selector.ColumnStyles.Add(new(SizeType.Percent, 100));
        series = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = UsageStyle.Surface,
            ForeColor = UsageStyle.Ink, AccessibleName = "Usage history provider and quota" };
        scope = new Label { Dock = DockStyle.Fill, ForeColor = UsageStyle.Muted, AutoEllipsis = true, Padding = new Padding(12, 0, 0, 0) };
        selector.Controls.Add(series, 0, 0); selector.Controls.Add(scope, 1, 0); shell.Controls.Add(selector, 0, 1);
        summary = new Label { Dock = DockStyle.Fill, ForeColor = UsageStyle.Ink, AccessibleName = "Latest quota and estimate", AutoEllipsis = true };
        shell.Controls.Add(summary, 0, 2);
        chart = new HistoryChart { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12) }; shell.Controls.Add(chart, 0, 3);
        timeline = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BackColor = UsageStyle.Surface, ForeColor = UsageStyle.Ink, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Reset timeline", OwnerDraw = true };
        timeline.Columns.Add("Product / quota", 230); timeline.Columns.Add("Reported reset (local time)", 235); timeline.Columns.Add("Reading status", 280);
        timeline.DrawColumnHeader += (_, e) =>
        {
            using var background = new SolidBrush(UsageStyle.Background);
            using var line = new Pen(UsageStyle.Line);
            e.Graphics.FillRectangle(background, e.Bounds);
            e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            e.Graphics.DrawLine(line, e.Bounds.Right - 1, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Bottom);
            int padding = Math.Max(6, (int)Math.Round(8 * timeline.DeviceDpi / 96f));
            var textBounds = Rectangle.Inflate(e.Bounds, -padding, 0);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", timeline.Font, textBounds, UsageStyle.Good,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        };
        // Native row drawing retains keyboard selection and focus indication.
        timeline.DrawItem += (_, e) => e.DrawDefault = true;
        timeline.DrawSubItem += (_, e) => e.DrawDefault = true;
        timeline.SizeChanged += (_, _) => FitTimelineColumns();
        timeline.DpiChangedAfterParent += (_, _) => FitTimelineColumns();
        timeline.HandleCreated += (_, _) => FitTimelineColumns();
        shell.Controls.Add(timeline, 0, 4);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(0, 9, 0, 0) };
        footer.RowStyles.Add(new(SizeType.Absolute, 43)); footer.RowStyles.Add(new(SizeType.Percent, 100));
        retention = new Label { Dock = DockStyle.Fill, ForeColor = UsageStyle.Muted, Font = new Font("Segoe UI", 9), AutoEllipsis = true };
        footer.Controls.Add(retention, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        record = new CheckBox { Text = "Record usage locally", AutoSize = true, ForeColor = UsageStyle.Ink, Margin = new Padding(0, 9, 20, 0),
            AccessibleName = "Record local usage history" };
        var clear = UsageStyle.Button("Clear history", ClearHistory); clear.Width = 145;
        var close = UsageStyle.Button("Close", Close); close.Width = 100; close.DialogResult = DialogResult.Cancel;
        actions.Controls.Add(record); actions.Controls.Add(clear); actions.Controls.Add(close); footer.Controls.Add(actions, 0, 1);
        shell.Controls.Add(footer, 0, 5); Controls.Add(shell); CancelButton = close;
        record.CheckedChanged += (_, _) =>
        {
            if (updating) return;
            try { store.SetEnabled(record.Checked); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            { MessageBox.Show(this, ex.Message, "Usage history", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            RefreshHistory();
        };
        series.SelectedIndexChanged += (_, _) => { if (!updating) RefreshHistory(); };
        timer.Tick += (_, _) => RefreshHistory();
        Shown += (_, _) =>
        {
            int dark = 1; Native.DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
            var area = Screen.FromControl(this).WorkingArea;
            MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
            Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            RefreshHistory(); timer.Start();
        };
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96); PerformAutoScale();
    }

    private void FitTimelineColumns()
    {
        if (fittingTimeline || timeline.Columns.Count != 3 || timeline.ClientSize.Width <= 0) return;
        fittingTimeline = true;
        try
        {
            int padding = (int)Math.Ceiling(20 * timeline.DeviceDpi / 96f);
            int available = Math.Max(1, timeline.ClientSize.Width - 1);
            int Minimum(int column) => TextRenderer.MeasureText(timeline.Columns[column].Text, timeline.Font).Width + padding;
            int first = Math.Max(Minimum(0), available * 31 / 100);
            int second = Math.Max(Minimum(1), available * 33 / 100);
            timeline.Columns[0].Width = first; timeline.Columns[1].Width = second;
            // Fill the entire header width; narrow screens retain horizontal
            // scrolling instead of clipping the header labels.
            timeline.Columns[2].Width = Math.Max(Minimum(2), available - first - second);
        }
        finally { fittingTimeline = false; }
    }

    private void ClearHistory()
    {
        if (MessageBox.Show(this, "Clear locally saved usage samples? Current allowances and provider accounts stay unchanged.",
            "Clear usage history", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try { store.Clear(DateTimeOffset.Now); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { MessageBox.Show(this, ex.Message, "Usage history", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        RefreshHistory();
    }

    private void RefreshHistory()
    {
        var now = DateTimeOffset.Now; var states = currentStates();
        long revision = store.Revision;
        bool samplesChanged = revision != displayedRevision;
        if (samplesChanged)
        {
            cachedSamples = store.Samples; displayedRevision = revision;
            historyKeys = cachedSamples.Select(s => (s.ProviderId, s.QuotaLabel)).Distinct().ToArray();
        }
        var selected = series.SelectedItem as Series;
        var keys = historyKeys.Concat(states.Where(s => s.Reading is not null)
            .SelectMany(s => s.Reading!.Quotas.Select(q => (s.Id, q.Label)))).Distinct()
            .OrderBy(k => k.Item1).ThenBy(k => k.Item2).ToArray();
        updating = true;
        try
        {
            record.Checked = store.Enabled;
            if (!keys.SequenceEqual(displayedKeys))
            {
                series.BeginUpdate(); series.Items.Clear();
                foreach (var (id, label) in keys)
                {
                    var provider = providers.FirstOrDefault(p => p.Id == id);
                    series.Items.Add(new Series(id, label, provider?.Name ?? id, provider?.Scope ?? "Previously recorded provider"));
                }
                if (series.Items.Count > 0) series.SelectedIndex = Math.Max(0, series.Items.Cast<Series>().ToList().FindIndex(s => s.ProviderId == selected?.ProviderId && s.Label == selected?.Label));
                series.EndUpdate(); displayedKeys = keys;
            }
        }
        finally { updating = false; }
        retention.Text = store.Warning ?? $"Up to 30 days / {UsageHistoryStore.MaxSamples:N0} samples on this Windows account; oldest samples are removed.\nStores product/quota IDs, percentages and times only. Clear history after changing provider accounts.";
        retention.ForeColor = store.Warning is null ? UsageStyle.Muted : UsageStyle.Warning;
        if (series.SelectedItem is Series chosen)
        {
            scope.Text = chosen.Scope;
            var key = (chosen.ProviderId, chosen.Label);
            if (samplesChanged || displayedSeries != key)
            {
                // The store already orders observations; redraw only after a
                // sample or series change, not on each countdown timer tick.
                displayedPoints = cachedSamples.Where(s => s.ProviderId == chosen.ProviderId && s.QuotaLabel == chosen.Label).ToArray();
                displayedSeries = key; chart.UpdateSamples(displayedPoints);
            }
            var points = displayedPoints;
            var current = states.FirstOrDefault(s => s.Id == chosen.ProviderId);
            var latest = points.LastOrDefault();
            var estimate = UsageHistoryAnalysis.Estimate(points, chosen.ProviderId, chosen.Label, current, now);
            string first = latest is null ? "No saved samples yet. A successful quota reading starts the history."
                : $"Last saved: {latest.Used:0.##}% used / {100 - latest.Used:0.##}% remaining  ·  {latest.Fetched.LocalDateTime:g}";
            string forecast = !store.Enabled ? "Estimates paused while history recording is off." : !estimate.Available ? estimate.Reason
                : estimate.ExhaustedAt <= now ? "Estimated exhaustion has been reached; refresh the provider reading."
                : estimate.LastsUntilReset == true ? $"Estimate: allowance lasts until the reported reset at this rate ({estimate.PercentPerHour:0.##} percentage points/hour)."
                : $"Estimate: allowance may run out {estimate.ExhaustedAt!.Value.LocalDateTime:g}, before reset ({estimate.PercentPerHour:0.##} percentage points/hour).";
            summary.Text = first + "\n" + forecast + "\n" + (estimate.Available && store.Enabled ? estimate.Reason : "Estimates require fresh, comparable readings in the same quota window.");
            summary.AccessibleDescription = chosen.Name + ", " + chosen.Label + ". " + summary.Text;
        }
        else
        {
            scope.Text = "Only available product-specific readings are recorded.";
            if (displayedSeries is not null || samplesChanged) chart.UpdateSamples([]);
            displayedPoints = []; displayedSeries = null;
            summary.Text = "No usage history yet.\nSuccessful numeric quota readings appear here while local recording is enabled.";
        }
        timeline.BeginUpdate(); timeline.Items.Clear();
        foreach (var (state, quota) in states.Where(s => s.Reading is not null)
            .SelectMany(s => s.Reading!.Quotas.Select(q => (State: s, Quota: q))).OrderBy(item => item.Quota.Reset))
            {
                var provider = providers.FirstOrDefault(p => p.Id == state.Id);
                var row = new ListViewItem((provider?.Name ?? state.Id) + " / " + quota.Label);
                row.SubItems.Add(quota.Reset?.LocalDateTime.ToString("g") ?? "Unavailable");
                row.SubItems.Add(!state.Enabled ? "Monitor disabled" : state.IsStale(now) ? "Stale reading — reset unconfirmed" : UsageDisplay.ResetText(quota.Reset, now));
                timeline.Items.Add(row);
            }
        timeline.EndUpdate();
    }
    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }

    private sealed class HistoryChart : Control
    {
        private UsageHistorySample[] samples = [];
        internal HistoryChart() { DoubleBuffered = true; BackColor = UsageStyle.Surface; AccessibleName = "Recorded usage percentage chart"; }
        internal void UpdateSamples(UsageHistorySample[] samples)
        {
            this.samples = samples;
            AccessibleDescription = samples.Length == 0 ? "No samples." : $"{samples.Length} recorded samples. Latest {samples[^1].Used:0.##}% used, {samples[^1].Fetched.LocalDateTime:g}. Gaps and quota resets are not connected.";
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = DeviceDpi / 96f; g.ScaleTransform(scale, scale); float width = Width / scale, height = Height / scale;
            var plot = new RectangleF(43, 24, Math.Max(1, width - 65), Math.Max(1, height - 58));
            using var grid = new Pen(UsageStyle.Line); using var trace = new Pen(UsageStyle.Good, 2); using var point = new SolidBrush(UsageStyle.Good);
            foreach (int percent in new[] { 0, 25, 50, 75, 100 })
            {
                float y = plot.Bottom - plot.Height * percent / 100;
                g.DrawLine(grid, plot.Left, y, plot.Right, y);
                UsageStyle.Text(g, percent.ToString(), 9, UsageStyle.Muted, new(3, y - 8, 33, 16));
            }
            UsageStyle.Text(g, "% USED  ·  OBSERVED SAMPLES", 9, UsageStyle.Muted, new(plot.Left, 2, plot.Width, 18));
            if (samples.Length == 0) { UsageStyle.Text(g, "Waiting for recorded readings", 12, UsageStyle.Muted, plot, true); return; }
            var first = samples[0].Fetched; var last = samples[^1].Fetched;
            double span = Math.Max(1, (last - first).TotalSeconds);
            PointF Position(UsageHistorySample sample) => new(plot.Left + (float)((sample.Fetched - first).TotalSeconds / span) * plot.Width,
                plot.Bottom - (float)sample.Used / 100 * plot.Height);
            PointF anchor = Position(samples[0]);
            for (int i = 0; i < samples.Length; i++)
            {
                var p = Position(samples[i]);
                bool continuous = i > 0 && samples[i].Reset == samples[i - 1].Reset
                    && samples[i].Fetched - samples[i - 1].Fetched <= UsageHistoryAnalysis.MaximumGap
                    && samples[i].Used + .001 >= samples[i - 1].Used;
                if (!continuous) anchor = p;
                else if (Math.Abs(p.X - anchor.X) >= .5f || Math.Abs(p.Y - anchor.Y) >= .5f || i == samples.Length - 1)
                { g.DrawLine(trace, anchor, p); anchor = p; }
                if (samples.Length < 500 || !continuous || i == samples.Length - 1) g.FillEllipse(point, p.X - 2, p.Y - 2, 4, 4);
            }
            UsageStyle.Text(g, first.LocalDateTime.ToString("MMM d HH:mm"), 9, UsageStyle.Muted, new(plot.Left, plot.Bottom + 7, plot.Width / 2, 18));
            UsageStyle.Text(g, last.LocalDateTime.ToString("MMM d HH:mm"), 9, UsageStyle.Muted, new(plot.Left + plot.Width / 2, plot.Bottom + 7, plot.Width / 2, 18));
        }
    }
}
