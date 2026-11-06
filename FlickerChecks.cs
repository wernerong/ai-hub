using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace AIHub;

internal static class FlickerChecks
{
    private sealed record Geometry(int Left, int Top, int Right, int Bottom)
    {
        internal int Width => Right - Left;
        internal int Height => Bottom - Top;
        internal static Geometry? Read(nint window) => Native.GetWindowRect(window, out var bounds)
            ? new(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom) : null;
    }

    private sealed class ResizeProbe : Form
    {
        internal readonly List<Geometry> SizeMessages = [];
        internal bool Recording;
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (Recording && message.Msg == 0x0005 && Geometry.Read(Handle) is { } bounds)
                SizeMessages.Add(bounds);
        }
        internal void Reset() { SizeMessages.Clear(); Recording = true; }
    }

    internal static async Task Run(string folder, List<string> lines)
    {
        int failures = 0;
        void Check(bool condition, string label)
        {
            lines.Add((condition ? "PASS " : "FAIL ") + label);
            if (!condition) failures++;
        }
        static bool Fits(Form probe, Control pane) => Geometry.Read(probe.Handle) == Geometry.Read(pane.Handle);
        static WindowInfo Identity(Form form) => new(form.Handle, Environment.ProcessId, "AIHub", form.Text,
            Environment.ProcessPath, Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks);
        static ResizeProbe Probe(string title, int x) => new()
        {
            Text = title, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
            Location = new Point(x, -3000), Size = new Size(700, 1050), MinimumSize = new Size(400, 1000)
        };
        using var host = new Form { Text = "AI Hub idle alignment validation", ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -3000), Size = new Size(1100, 650) };
        var viewport = new Panel { Dock = DockStyle.Fill };
        host.Controls.Add(viewport); host.Show();
        using var first = Probe("AI Hub resize probe one", -4000);
        using var second = Probe("AI Hub resize probe two", -5000);
        first.Show(); second.Show();
        using var manager = new DockManager(Path.Combine(folder, "flicker-windows-" + Guid.NewGuid().ToString("N") + ".json"));
        try
        {
            await manager.Attach("first", Identity(first), viewport.Handle, () => false);
            manager.Present([new("first", viewport.Handle)], "first");
            await Task.Delay(25);
            Check(Fits(first, viewport) && first.Height < first.MinimumSize.Height,
                "Flicker fixture starts fitted below its standalone minimum height");
            first.Reset();
            for (int repeat = 0; repeat < 12; repeat++)
            {
                manager.Present([new("first", viewport.Handle)], "first", raise: true);
                await Task.Delay(10);
            }
            lines.Add("DETAIL single idle WM_SIZE=" + first.SizeMessages.Count + "; heights="
                + string.Join(",", first.SizeMessages.Select(bounds => bounds.Height).Distinct()));
            Check(first.SizeMessages.Count == 0 && Fits(first, viewport),
                "Repeated foreground presentation of a fitted single AI produces no intermediate resize messages");

            var panes = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            panes.ColumnStyles.Add(new(SizeType.Percent, 50)); panes.ColumnStyles.Add(new(SizeType.Percent, 50));
            panes.RowStyles.Add(new(SizeType.Percent, 100));
            var left = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 5, 0) };
            var right = new Panel { Dock = DockStyle.Fill, Margin = new Padding(5, 0, 0, 0) };
            panes.Controls.Add(left, 0, 0); panes.Controls.Add(right, 1, 0); viewport.Controls.Add(panes);
            await manager.Attach("second", Identity(second), right.Handle, () => false);
            DockPlacement[] layout = [new("first", left.Handle), new("second", right.Handle)];
            manager.Present(layout, "first"); await Task.Delay(25);
            first.Reset(); second.Reset();
            for (int repeat = 0; repeat < 12; repeat++)
            {
                manager.Present(layout, repeat % 2 == 0 ? "first" : "second", raise: true);
                await Task.Delay(10);
            }
            lines.Add($"DETAIL split idle WM_SIZE first={first.SizeMessages.Count}; second={second.SizeMessages.Count}");
            Check(first.SizeMessages.Count == 0 && second.SizeMessages.Count == 0 && Fits(first, left) && Fits(second, right),
                "Repeated split presentation and focus-key changes produce no intermediate resize messages in either AI");

            host.Height -= 60;
            manager.Present(layout, "first"); await Task.Delay(25);
            Check(first.SizeMessages.Count > 0 && second.SizeMessages.Count > 0 && Fits(first, left) && Fits(second, right),
                "Real workspace resizing still fits both minimum-size AI panes");
            first.Reset(); second.Reset();
            manager.Select("first", left.Handle);
            manager.Present(layout, "second");
            Check(Native.IsWindowVisible(first.Handle) && Native.IsWindowVisible(second.Handle) && Fits(first, left) && Fits(second, right),
                "Reselecting a hidden split AI restores visibility and fitted bounds");
            manager.RestoreAll();
            Native.SetWindowPos(first.Handle, 0, 0, 0, 700, 500, 0x2 | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            Check(Geometry.Read(first.Handle) is { Height: >= 1000 },
                "Detaching a flicker fixture preserves its standalone minimum-size behavior");
        }
        finally
        {
            manager.RestoreAll();
            first.Close(); second.Close(); host.Close();
        }
        if (failures != 0) throw new InvalidOperationException($"Idle alignment regression checks failed: {failures}.");
    }

    private sealed class Observation(SavedWindow saved)
    {
        internal SavedWindow Saved => saved;
        public long Handle => saved.Handle;
        public int Pid => saved.Pid;
        public int Samples { get; private set; }
        public int BoundsChanges { get; private set; }
        public int LocationEvents { get; set; }
        public int VisibilityChanges { get; private set; }
        public int ForegroundSamples { get; private set; }
        public bool IdentityLost { get; private set; }
        public int? MinWidth { get; private set; }
        public int? MaxWidth { get; private set; }
        public int? MinHeight { get; private set; }
        public int? MaxHeight { get; private set; }
        public Geometry? InitialBounds { get; private set; }
        public Geometry? FinalBounds { get; private set; }
        public List<object> Transitions { get; } = [];
        private bool? visible;
        internal void Sample(long elapsed, nint foreground)
        {
            if (IdentityLost) return;
            if (!saved.Matches()) { IdentityLost = true; return; }
            Samples++;
            if (foreground == (nint)saved.Handle) ForegroundSamples++;
            bool nextVisible = Native.IsWindowVisible((nint)saved.Handle);
            if (visible is not null && visible != nextVisible) VisibilityChanges++;
            visible = nextVisible;
            var bounds = Geometry.Read((nint)saved.Handle);
            if (bounds is null) return;
            InitialBounds ??= bounds;
            if (FinalBounds is not null && FinalBounds != bounds)
            {
                BoundsChanges++;
                if (Transitions.Count < 256) Transitions.Add(new { ElapsedMs = elapsed, Bounds = bounds });
            }
            FinalBounds = bounds;
            MinWidth = Math.Min(MinWidth ?? bounds.Width, bounds.Width); MaxWidth = Math.Max(MaxWidth ?? bounds.Width, bounds.Width);
            MinHeight = Math.Min(MinHeight ?? bounds.Height, bounds.Height); MaxHeight = Math.Max(MaxHeight ?? bounds.Height, bounds.Height);
        }
    }

    // Read-only diagnostic: no titles, chat contents, input, geometry writes,
    // activation, app discovery or provider requests. Target only saved identities.
    internal static void Watch(string statePath, string outputPath)
    {
        var observations = Recovery.Read(statePath).Where(window => window.Matches()).ToDictionary(window => (nint)window.Handle, window => new Observation(window));
        var started = DateTimeOffset.UtcNow;
        var elapsed = Stopwatch.StartNew();
        nint foreground = Native.GetForegroundWindow();
        int foregroundChanges = 0;
        WinEvent callback = (_, _, window, objectId, childId, _, _) =>
        {
            if (objectId == 0 && childId == 0 && observations.TryGetValue(window, out var item) && item.Saved.Matches())
                item.LocationEvents++;
        };
        nint hook = SetWinEventHook(0x800B, 0x800B, 0, callback, 0, 0, 0);
        try
        {
            while (elapsed.ElapsedMilliseconds < 10000)
            {
                Application.DoEvents();
                nint current = Native.GetForegroundWindow();
                if (current != foreground) { foregroundChanges++; foreground = current; }
                foreach (var item in observations.Values) item.Sample(elapsed.ElapsedMilliseconds, current);
                Thread.Sleep(5);
            }
        }
        finally
        {
            if (hook != 0) UnhookWinEvent(hook);
            GC.KeepAlive(callback);
        }
        File.WriteAllText(outputPath, JsonSerializer.Serialize(new
        {
            StartedUtc = started, DurationMs = elapsed.ElapsedMilliseconds, PollIntervalMs = 5,
            LocationHookAvailable = hook != 0, ForegroundChanges = foregroundChanges, Windows = observations.Values
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private delegate void WinEvent(nint hook, uint eventType, nint window, int objectId, int childId, uint threadId, uint time);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint first, uint last, nint module, WinEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
}
