using System.Diagnostics;

namespace AIHub;

internal static class ShutdownChecks
{
    private sealed class CloseProbe : Form
    {
        internal int CloseRequests;
        internal bool CancelClose, HideOnClose;
        internal Action? ObserveClose;
        protected override bool ShowWithoutActivation => true;

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            CloseRequests++;
            if (CancelClose || HideOnClose) e.Cancel = true;
            if (HideOnClose) Hide();
            ObserveClose?.Invoke();
            base.OnFormClosing(e);
        }
    }

    internal static async Task Run(string folder, List<string> lines)
    {
        using var host = Probe("AI Hub shutdown validation");
        var viewport = new Panel { Dock = DockStyle.Fill };
        host.Controls.Add(viewport); host.Show();
        string statePath = Path.Combine(folder, "shutdown-windows-" + Guid.NewGuid().ToString("N") + ".json");
        using var manager = new DockManager(statePath);
        try
        {
            using var visible = Probe("Shutdown visible AI");
            using var hidden = Probe("Shutdown hidden AI");
            using var unrelated = Probe("Unrelated window in the same process");
            using var detached = Probe("Explicitly popped-out AI");
            using var restored = Probe("Explicitly restored AI");
            visible.Show(); hidden.Show(); unrelated.Show(); detached.Show(); restored.Show();
            nint visibleHandle = visible.Handle, hiddenHandle = hidden.Handle;
            bool visibleRecordRemoved = false, hiddenRecordRemoved = false;
            visible.ObserveClose = () => visibleRecordRemoved = !Recovery.Read(statePath).Any(w => w.Handle == (long)visibleHandle);
            hidden.ObserveClose = () => hiddenRecordRemoved = !Recovery.Read(statePath).Any(w => w.Handle == (long)hiddenHandle);

            await manager.Attach("restored", Identity(restored), viewport.Handle, () => false);
            manager.RestoreAll();
            await manager.Attach("detached", Identity(detached), viewport.Handle, () => false);
            manager.Detach("detached");
            await manager.Attach("visible", Identity(visible), viewport.Handle, () => false);
            await manager.Attach("hidden", Identity(hidden), viewport.Handle, () => false);
            manager.Select("visible", viewport.Handle);
            Check(Native.IsWindowVisible(visibleHandle) && !Native.IsWindowVisible(hiddenHandle),
                "Shutdown fixture includes both a visible AI and an inactive hidden AI", lines);
            manager.CloseAll();
            await PumpUntil(() => visible.IsDisposed && hidden.IsDisposed);
            Check(visible.CloseRequests == 1 && hidden.CloseRequests == 1
                && !Native.IsWindow(visibleHandle) && !Native.IsWindow(hiddenHandle),
                "Hub shutdown gracefully closes every attached AI including hidden tabs", lines);
            Check(visibleRecordRemoved && hiddenRecordRemoved && manager.Windows.Count == 0 && Recovery.Read(statePath).Count == 0,
                "Each shutdown recovery record is removed before its window handles the close request", lines);
            Check(unrelated.CloseRequests == 0 && detached.CloseRequests == 0 && restored.CloseRequests == 0
                && Native.IsWindowVisible(unrelated.Handle) && Native.IsWindowVisible(detached.Handle) && Native.IsWindowVisible(restored.Handle),
                "Shutdown leaves unrelated same-process windows and explicitly detached or restored AIs open", lines);

            using var cancelled = Probe("AI with an unsaved-work close cancellation");
            cancelled.CancelClose = true; cancelled.Show();
            var cancelledOriginal = SavedWindow.Capture(cancelled.Handle);
            bool cancellationWasRestored = false;
            cancelled.ObserveClose = () => cancellationWasRestored = OriginalStyles(cancelled.Handle, cancelledOriginal)
                && Native.IsWindowVisible(cancelled.Handle);
            await manager.Attach("cancelled", Identity(cancelled), viewport.Handle, () => false);
            manager.CloseAll();
            await PumpUntil(() => cancelled.CloseRequests != 0);
            Check(cancelled.CloseRequests == 1 && !cancelled.IsDisposed && cancellationWasRestored
                && OriginalStyles(cancelled.Handle, cancelledOriginal) && Native.IsWindowVisible(cancelled.Handle)
                && manager.Windows.Count == 0 && Recovery.Read(statePath).Count == 0,
                "An AI can cancel shutdown with its original usable window and normal close handling preserved", lines);

            using var tray = Probe("AI that hides to its tray when closed");
            tray.HideOnClose = true; tray.Show();
            await manager.Attach("tray", Identity(tray), viewport.Handle, () => false);
            manager.CloseAll();
            await PumpUntil(() => tray.CloseRequests != 0);
            Check(tray.CloseRequests == 1 && !tray.IsDisposed && !Native.IsWindowVisible(tray.Handle),
                "Shutdown honors an AI's own close-to-tray behavior", lines);
            Recovery.RestoreFile(statePath);
            manager.RestoreAll();
            Check(!Native.IsWindowVisible(tray.Handle) && Recovery.Read(statePath).Count == 0,
                "Recovery and later cleanup do not reopen an AI that accepted shutdown by hiding to its tray", lines);

            using var stalePid = Probe("Stale shutdown PID");
            using var staleStart = Probe("Stale shutdown process start time");
            stalePid.Show(); staleStart.Show();
            var pidOriginal = SavedWindow.Capture(stalePid.Handle);
            var startOriginal = SavedWindow.Capture(staleStart.Handle);
            await manager.Attach("stale-pid", Identity(stalePid), viewport.Handle, () => false);
            await manager.Attach("stale-start", Identity(staleStart), viewport.Handle, () => false);
            manager.Windows["stale-pid"].Pid = -1;
            manager.Windows["stale-start"].StartTicks++;
            manager.CloseAll();
            await Task.Delay(50);
            Check(stalePid.CloseRequests == 0 && staleStart.CloseRequests == 0
                && Native.IsWindow(stalePid.Handle) && Native.IsWindow(staleStart.Handle)
                && manager.Windows.Count == 0 && Recovery.Read(statePath).Count == 0,
                "Shutdown rejects stale PID and process-start identities without closing their current windows", lines);
            // These are owned test fixtures: repair the intentionally invalidated identities' framing.
            pidOriginal.Restore(); startOriginal.Restore();

            using var blocked = Probe("AI retained after recovery persistence failure");
            blocked.Show();
            var blockedOriginal = SavedWindow.Capture(blocked.Handle);
            await manager.Attach("blocked", Identity(blocked), viewport.Handle, () => false);
            bool rejected = false;
            using (var held = new FileStream(statePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { manager.CloseAll(); }
                catch (InvalidOperationException) { rejected = true; }
                await Task.Delay(50);
                var durable = Recovery.Read(statePath);
                Check(rejected && blocked.CloseRequests == 0 && manager.Has("blocked")
                    && durable.Count == 1 && durable[0].Handle == (long)blocked.Handle && durable[0].Matches(),
                    "A failed durable shutdown update sends no close and retains the tracked recovery identity", lines);
            }
            Recovery.RestoreFile(statePath);
            Check(blocked.CloseRequests == 0 && OriginalStyles(blocked.Handle, blockedOriginal)
                && Native.IsWindowVisible(blocked.Handle) && Recovery.Read(statePath).Count == 0,
                "An AI retained after shutdown persistence failure remains recoverable without losing its window", lines);
            manager.CloseAll();
            await PumpUntil(() => blocked.IsDisposed);
            Check(blocked.CloseRequests == 1 && manager.Windows.Count == 0,
                "Shutdown can retry and close the retained AI after its recovery file becomes writable", lines);
        }
        finally { manager.RestoreAll(); }
    }

    private static CloseProbe Probe(string title) => new()
    {
        Text = title, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
        Location = new Point(-30000, -30000), Size = new Size(640, 480)
    };

    private static WindowInfo Identity(Form form)
    {
        using var process = Process.GetCurrentProcess();
        return new(form.Handle, process.Id, "AIHub", form.Text, Environment.ProcessPath,
            process.StartTime.ToUniversalTime().Ticks);
    }

    private static bool OriginalStyles(nint hwnd, SavedWindow original) => Native.GetParent(hwnd) == (nint)original.Parent
        && ((long)Native.GetWindowLong(hwnd, Native.GWL_STYLE) & ~0x10000000L) == (original.Style & ~0x10000000L)
        && (long)Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE) == original.ExStyle;

    private static async Task PumpUntil(Func<bool> completed)
    {
        for (int i = 0; i < 100 && !completed(); i++) await Task.Delay(10);
    }

    private static void Check(bool condition, string label, List<string> lines)
    {
        if (!condition) throw new InvalidOperationException(label);
        lines.Add("PASS " + label);
    }
}
