using System.Diagnostics;
using System.Text.Json;

namespace AIHub;

internal static class SplitViewChecks
{
    // Read-only diagnostics for comparing native window state with a screenshot.
    // No windows are shown, activated, resized or otherwise changed here.
    internal static void Diagnose(string statePath, string outputPath)
    {
        var saved = Recovery.Read(statePath);
        var hubs = Native.Windows().Where(window => window.Title == AppInfo.DisplayName).ToArray();
        nint hub = hubs.Length == 1 ? hubs[0].Handle : 0;
        var windows = saved.Select(window =>
        {
            var handle = (nint)window.Handle;
            bool matches = window.Matches();
            bool hasBounds = Native.GetWindowRect(handle, out var bounds);
            return new
            {
                window.Handle,
                window.Pid,
                Matches = matches,
                Visible = Native.IsWindowVisible(handle),
                Iconic = Native.IsIconic(handle),
                Bounds = hasBounds ? new { bounds.Left, bounds.Top, bounds.Right, bounds.Bottom } : null,
                AboveHub = hub == 0 ? (bool?)null : Native.IsAbove(handle, hub),
                Parent = (long)Native.GetParent(handle),
                Style = $"{(long)Native.GetWindowLong(handle, Native.GWL_STYLE):X}",
                ExStyle = $"{(long)Native.GetWindowLong(handle, Native.GWL_EXSTYLE):X}",
                Dpi = Native.DpiName(handle)
            };
        }).ToArray();
        var pairs = saved.SelectMany((first, index) => saved.Skip(index + 1).Select(second => new
        {
            First = first.Handle,
            Second = second.Handle,
            FirstAboveSecond = Native.IsAbove((nint)first.Handle, (nint)second.Handle),
            SecondAboveFirst = Native.IsAbove((nint)second.Handle, (nint)first.Handle)
        })).ToArray();
        var report = new
        {
            CapturedUtc = DateTimeOffset.UtcNow,
            Foreground = (long)Native.GetForegroundWindow(),
            Hub = hub == 0 ? (long?)null : (long)hub,
            HubCandidates = hubs.Select(window => new { Handle = (long)window.Handle, window.Pid }).ToArray(),
            Windows = windows,
            Pairs = pairs
        };
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static async Task Run(string folder, List<string> lines)
    {
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            lines.Add("PASS " + label);
        }
        static bool Rejects(Action action)
        {
            try { action(); return false; }
            catch (InvalidOperationException) { return true; }
        }
        static bool Fits(nint window, nint pane) => Native.GetWindowRect(window, out var actual)
            && Native.GetWindowRect(pane, out var expected) && actual.Left == expected.Left && actual.Top == expected.Top
            && actual.Right == expected.Right && actual.Bottom == expected.Bottom;
        static bool OriginalStyles(WindowInfo window, SavedWindow original) => Native.GetParent(window.Handle) == (nint)original.Parent
            && ((long)Native.GetWindowLong(window.Handle, Native.GWL_STYLE) & ~0x10000000L) == (original.Style & ~0x10000000L)
            && (long)Native.GetWindowLong(window.Handle, Native.GWL_EXSTYLE) == original.ExStyle;

        string statePath = Path.Combine(folder, "split-windows-" + Guid.NewGuid().ToString("N") + ".json");
        using var host = new Form { Text = "AI Hub split validation", ShowInTaskbar = false,
            Size = new Size(1120, 680), StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -2000) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new(SizeType.Percent, 50)); layout.ColumnStyles.Add(new(SizeType.Percent, 50));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        var left = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) };
        var right = new Panel { Dock = DockStyle.Fill, Margin = new Padding(8, 0, 0, 0) };
        layout.Controls.Add(left, 0, 0); layout.Controls.Add(right, 1, 0); host.Controls.Add(layout); host.Show();
        Process? first = null, second = null;
        using var manager = new DockManager(statePath);
        try
        {
            first = Program.StartSelf("--sample", "--minimum-size");
            second = Program.StartSelf("--sample", "--dpi-v1");
            var firstWindow = await WaitForWindow(first);
            var secondWindow = await WaitForWindow(second);
            var firstOriginal = SavedWindow.Capture(firstWindow.Handle);
            var secondOriginal = SavedWindow.Capture(secondWindow.Handle);
            Check(Native.DpiName(secondWindow.Handle) == "PerMonitor", "Split fixture includes a legacy per-monitor DPI app");
            await manager.Attach("left", firstWindow, left.Handle, () => false);
            await manager.Attach("right", secondWindow, right.Handle, () => false);
            Check(!Native.IsWindowVisible(firstWindow.Handle) && !Native.IsWindowVisible(secondWindow.Handle),
                "Both split attachments can be prepared while the workspace is hidden");
            DockPlacement[] panes = [new("left", left.Handle), new("right", right.Handle)];
            nint foreground = Native.GetForegroundWindow();
            manager.Present(panes, "left", raise: false);
            Check(Native.GetForegroundWindow() == foreground, "Presenting a split without activation leaves foreground focus unchanged");
            Check(Native.IsWindowVisible(firstWindow.Handle) && Native.IsWindowVisible(secondWindow.Handle),
                "Presenting a split keeps both requested AI windows visible");
            Check(Fits(firstWindow.Handle, left.Handle) && Fits(secondWindow.Handle, right.Handle),
                "Split windows fit their individual panes even when an app enforces a taller minimum size");
            Native.GetWindowRect(firstWindow.Handle, out var firstBounds); Native.GetWindowRect(secondWindow.Handle, out var secondBounds);
            Check(firstBounds.Right <= secondBounds.Left, "Split AI windows do not overlap each other");
            Check(Native.GetParent(firstWindow.Handle) == 0 && Native.GetParent(secondWindow.Handle) == 0
                && Native.DpiName(secondWindow.Handle) == "PerMonitor", "Split hosting preserves independent windows and each app's DPI awareness");
            Check(Rejects(() => manager.Present([new("left", left.Handle), new("left", right.Handle)], "left"))
                && Rejects(() => manager.Present([new("left", left.Handle), new("right", left.Handle)], "left"))
                && Rejects(() => manager.Present([new("left", left.Handle), new("right", 0)], "left"))
                && Native.IsWindowVisible(firstWindow.Handle) && Native.IsWindowVisible(secondWindow.Handle),
                "Invalid split identities and pane handles are rejected before the current layout changes");
            manager.Select("left", left.Handle);
            Check(Native.IsWindowVisible(firstWindow.Handle) && !Native.IsWindowVisible(secondWindow.Handle),
                "Switching from split to a single AI hides only the other hosted app");
            manager.Present(panes, "right");
            host.Width -= 130; host.Height -= 90;
            await Task.Delay(50);
            foreground = Native.GetForegroundWindow();
            manager.Present(panes, "right", raise: false);
            Check(Fits(firstWindow.Handle, left.Handle) && Fits(secondWindow.Handle, right.Handle)
                && Native.GetForegroundWindow() == foreground, "Resizing both split panes preserves bounds without requesting focus");
            host.WindowState = FormWindowState.Minimized; manager.HideAll();
            Check(Native.IsIconic(host.Handle) && !Native.IsWindowVisible(firstWindow.Handle) && !Native.IsWindowVisible(secondWindow.Handle)
                && !first.HasExited && !second.HasExited, "Minimizing a split hides both AI windows without closing their processes");
            host.WindowState = FormWindowState.Normal;
            manager.Present(panes, "right");
            Check(Native.IsWindowVisible(firstWindow.Handle) && Native.IsWindowVisible(secondWindow.Handle)
                && firstOriginal.Matches() && secondOriginal.Matches(), "Restoring a split reuses both original live AI identities");
            manager.Detach("left");
            Check(OriginalStyles(firstWindow, firstOriginal) && manager.Has("right") && Native.IsWindowVisible(secondWindow.Handle),
                "Popping out one split AI restores its styles while the other pane stays connected");
            await manager.Attach("left", firstWindow, left.Handle, () => false);
            manager.Present(panes, "right");
            var recorded = Recovery.Read(statePath);
            Check(recorded.Count == 2 && recorded.All(window => window.Matches()) && recorded.Select(window => window.Handle).Distinct().Count() == 2,
                "Split recovery persists two distinct live process identities");
            manager.HideAll(); Recovery.RestoreFile(statePath);
            Check(OriginalStyles(firstWindow, firstOriginal) && OriginalStyles(secondWindow, secondOriginal)
                && Native.IsWindowVisible(firstWindow.Handle) && Native.IsWindowVisible(secondWindow.Handle)
                && Recovery.Read(statePath).Count == 0, "Recovery restores both split apps and clears their records without terminating either app");
            manager.RestoreAll();
            await manager.Attach("left", firstWindow, left.Handle, () => false);
            await manager.Attach("right", secondWindow, right.Handle, () => false);
            manager.Present(panes, "left");
            second.CloseMainWindow(); await second.WaitForExitAsync();
            manager.Present(panes, "left");
            Check(Native.IsWindowVisible(firstWindow.Handle) && Fits(firstWindow.Handle, left.Handle) && !manager.Has("right"),
                "Closing one provider does not hide or detach the remaining split AI");
            manager.RestoreAll();
            Check(OriginalStyles(firstWindow, firstOriginal) && Recovery.Read(statePath).Count == 0,
                "Split close cleanup restores the surviving AI and removes stale records");
        }
        finally
        {
            try { manager.RestoreAll(); }
            finally
            {
                foreach (var process in new[] { first, second })
                {
                    if (process is { HasExited: false })
                    {
                        process.CloseMainWindow();
                        if (!process.WaitForExit(1500)) process.Kill(); // Only these disposable fixtures.
                    }
                    process?.Dispose();
                }
                host.Close();
            }
        }
    }

    private static async Task<WindowInfo> WaitForWindow(Process process)
    {
        for (int attempt = 0; attempt < 80; attempt++)
        {
            var window = Native.Windows().FirstOrDefault(candidate => candidate.Pid == process.Id);
            if (window is not null) return window;
            await Task.Delay(50);
        }
        throw new InvalidOperationException("A disposable split-view app did not start.");
    }
}
