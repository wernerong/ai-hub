using System.Diagnostics;

namespace AIHub;

internal static class SelfTest
{
    internal static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var lines = new List<string>();
        var form = new Form { Text = "AI Hub validation", Size = new Size(900, 650), ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000) };
        var panel = new Panel { Dock = DockStyle.Fill };
        form.Controls.Add(panel);
        form.Shown += async (_, _) =>
        {
            Process? sample = null;
            using var manager = new DockManager(Path.Combine(folder, "test-windows.json"));
            try
            {
                await CheckZoomSettings(folder, lines);
                await UsageChecks.Run(folder, lines);
                ActivationChecks.CheckPolicy(lines);
                await UpdateChecks.Run(folder, lines);
                sample = Program.StartSelf("--sample");
                WindowInfo? window = null;
                for (int i = 0; i < 80; i++)
                {
                    await Task.Delay(100);
                    window = Native.Windows().FirstOrDefault(w => w.Pid == sample.Id);
                    if (window is not null) break;
                }
                Check(window is not null, "Disposable external app starts", lines);
                var original = SavedWindow.Capture(window!.Handle);
                await manager.Attach("sample", window, panel.Handle);
                Check(Native.GetParent(window.Handle) == 0, "Grouped app keeps its independent top-level window", lines);
                Check(Recovery.Read(Path.Combine(folder, "test-windows.json")).Single().Matches(), "Recovery record retains process identity", lines);
                panel.Size = new Size(760, 480);
                manager.Resize("sample", panel.Handle);
                Native.GetClientRect(panel.Handle, out var parent);
                Native.GetWindowRect(window.Handle, out var child);
                Native.GetWindowRect(panel.Handle, out var viewportRect);
                Check(child.Right - child.Left == parent.Right && child.Bottom - child.Top == parent.Bottom && child.Left == viewportRect.Left && child.Top == viewportRect.Top, "App follows viewport position and size", lines);
                manager.Select("other", panel.Handle);
                Check(!Native.IsWindowVisible(window.Handle), "Inactive app is hidden", lines);
                manager.Select("sample", panel.Handle);
                Check(Native.IsWindowVisible(window.Handle), "Selected app becomes visible", lines);
                Native.ShowWindow(window.Handle, 6); // Simulate an app minimizing its own window.
                await Task.Delay(50);
                manager.Select("sample", panel.Handle);
                Check(!Native.IsIconic(window.Handle) && Native.IsWindowVisible(window.Handle), "Reselecting restores a minimized app without switching panels", lines);
                for (int repeat = 0; repeat < 20; repeat++)
                {
                    Native.SetWindowPos(form.Handle, 0, 0, 0, 0, 0, 0x1 | 0x2 | Native.SWP_NOACTIVATE);
                    manager.Select("sample", panel.Handle);
                    Check(Native.IsWindowVisible(window.Handle) && Native.IsAbove(window.Handle, form.Handle), $"Repeat panel selection {repeat + 1} keeps the app above the hub", lines);
                }
                manager.Detach("sample");
                Check(Native.GetParent(window.Handle) == (nint)original.Parent, "Pop out restores the original parent", lines);
                Check(((long)Native.GetWindowLong(window.Handle, Native.GWL_STYLE) & ~0x10000000L) == (original.Style & ~0x10000000L), "Pop out restores window styles", lines);
                var restored = SavedWindow.Capture(window.Handle);
                Check(restored.Placement.NormalPosition.Left == original.Placement.NormalPosition.Left && restored.Placement.NormalPosition.Top == original.Placement.NormalPosition.Top && restored.Placement.NormalPosition.Right == original.Placement.NormalPosition.Right && restored.Placement.NormalPosition.Bottom == original.Placement.NormalPosition.Bottom, "Pop out restores original position and size", lines);
                Check(!sample.HasExited, "Original app remains running", lines);
                original.Pid = -1;
                Check(!original.Matches(), "Recovery rejects stale or reused window identity", lines);
                await manager.Attach("sample", window, panel.Handle);
                manager.RestoreAll();
                Check(Native.GetParent(window.Handle) == 0 && Recovery.Read(Path.Combine(folder, "test-windows.json")).Count == 0, "Close cleanup restores apps and clears recovery records", lines);
                var crashState = Path.Combine(folder, "crash-windows.json");
                using (var crashHost = Program.StartSelf("--crash-host", ((long)window.Handle).ToString(), window.Pid.ToString(), crashState))
                {
                    for (int i = 0; i < 60 && !File.Exists(crashState + ".attached"); i++) await Task.Delay(100);
                    Check(File.Exists(crashState + ".attached"), "Crash-recovery test host attaches the disposable app", lines);
                    crashHost.Kill(); // Only this test host; never a user application.
                    await crashHost.WaitForExitAsync();
                    for (int i = 0; i < 60 && Recovery.Read(crashState).Count != 0 && !sample.HasExited; i++) await Task.Delay(100);
                    Check(!sample.HasExited && Native.IsWindow(window.Handle) && Native.GetParent(window.Handle) == 0 && Recovery.Read(crashState).Count == 0 && ((long)Native.GetWindowLong(window.Handle, Native.GWL_STYLE) & ~0x10000000L) == (original.Style & ~0x10000000L), "Recovery helper restores the running app after abrupt hub termination", lines);
                    File.Delete(crashState + ".attached");
                }
                sample.CloseMainWindow();
                await sample.WaitForExitAsync();
                sample.Dispose();
                sample = Program.StartSelf("--sample", "--dpi-v1");
                window = null;
                for (int i = 0; i < 60; i++)
                {
                    await Task.Delay(100);
                    window = Native.Windows().FirstOrDefault(w => w.Pid == sample.Id);
                    if (window is not null) break;
                }
                Check(window is not null && Native.DpiName(window.Handle) == "PerMonitor", "Legacy per-monitor app starts", lines);
                await manager.Attach("v1", window!, panel.Handle);
                Check(Native.DpiName(window!.Handle) == "PerMonitor", "Grouping preserves the legacy app's DPI mode", lines);
                manager.RestoreAll();
                Check(Native.GetParent(window.Handle) == 0 && Native.DpiName(window.Handle) == "PerMonitor", "Legacy app restores with its original DPI mode", lines);
                sample.CloseMainWindow();
                await sample.WaitForExitAsync();
                sample.Dispose();
                sample = Program.StartSelf("--sample", "--minimum-size");
                window = null;
                for (int i = 0; i < 60; i++)
                {
                    await Task.Delay(100);
                    window = Native.Windows().FirstOrDefault(w => w.Pid == sample.Id);
                    if (window is not null) break;
                }
                Check(window is not null, "App with a minimum window size starts", lines);
                Native.GetWindowRect(window!.Handle, out var minimumRect);
                Native.GetWindowRect(panel.Handle, out var availableRect);
                Check(minimumRect.Bottom - minimumRect.Top >= 1000 && availableRect.Bottom - availableRect.Top < 1000,
                    "Minimum-size fixture is taller than the available workspace", lines);
                await manager.Attach("minimum", window, panel.Handle);
                CheckBounds(window.Handle, panel.Handle, "Minimum-size app fits without overlapping the usage strip", lines);
                // Exercise the same ongoing alignment and reselection used by the hub.
                form.Height -= 80;
                await Task.Delay(50);
                manager.Resize("minimum", panel.Handle);
                CheckBounds(window.Handle, panel.Handle, "Minimum-size app follows a smaller workspace", lines);
                manager.Select("other", panel.Handle);
                manager.Select("minimum", panel.Handle);
                CheckBounds(window.Handle, panel.Handle, "Reselecting the minimum-size app preserves workspace bounds", lines);
                manager.Detach("minimum");
                var poppedOut = SavedWindow.Capture(window.Handle);
                Native.SetWindowPos(window.Handle, 0, 0, 0, 700, 500, 0x2 | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                Native.GetWindowRect(window.Handle, out var popOutRect);
                Check(poppedOut.Placement.NormalPosition.Bottom - poppedOut.Placement.NormalPosition.Top >= 1000
                    && popOutRect.Bottom - popOutRect.Top >= 1000,
                    "Pop out restores the original size and retains the app's own minimum-size behavior", lines);
            }
            catch (Exception ex) { lines.Add("FAIL " + ex); Environment.ExitCode = 1; }
            finally
            {
                try { manager.RestoreAll(); } catch (Exception ex) { lines.Add("FAIL cleanup: " + ex.Message); }
                if (sample is { HasExited: false })
                {
                    sample.CloseMainWindow();
                    if (!sample.WaitForExit(1500)) sample.Kill(); // Only our disposable sample process.
                }
                sample?.Dispose();
                File.WriteAllLines(Path.Combine(folder, "validation.txt"), lines);
                form.Close();
            }
        };
        Application.Run(form);
    }
    internal static void CrashHost(nint hwnd, int pid, string path)
    {
        var form = new Form { ShowInTaskbar = false, Size = new Size(900, 650), StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000) };
        var panel = new Panel { Dock = DockStyle.Fill };
        form.Controls.Add(panel);
        var manager = new DockManager(path);
        form.Shown += async (_, _) =>
        {
            try { await manager.Attach("sample", new WindowInfo(hwnd, pid, "AIHub", "test"), panel.Handle); File.WriteAllText(path + ".attached", "attached"); }
            catch (Exception ex) { File.WriteAllText(path + ".error", ex.ToString()); form.Close(); }
        };
        Application.Run(form);
    }
    private static void Check(bool condition, string label, List<string> lines)
    {
        if (!condition) throw new InvalidOperationException(label);
        lines.Add("PASS " + label);
    }
    private static void CheckBounds(nint window, nint viewport, string label, List<string> lines)
    {
        Check(Native.GetWindowRect(window, out var actual) && Native.GetWindowRect(viewport, out var expected)
            && actual.Left == expected.Left && actual.Top == expected.Top
            && actual.Right == expected.Right && actual.Bottom == expected.Bottom, label, lines);
    }
    private static async Task CheckZoomSettings(string folder, List<string> lines)
    {
        var path = Path.Combine(folder, $"zoom-test-{Guid.NewGuid():N}.json");
        var settings = new ZoomSettings(path);
        Check(AppEntry.All.All(a => settings.Get(a.Key) is null), "New zoom settings preserve every app's existing zoom", lines);
        settings.Set("openai", 120);
        settings.Set("grok", 125);
        settings.Set("grokbot", 80);
        var loaded = new ZoomSettings(path);
        Check(loaded.Get("openai") == 120 && loaded.Get("grok") == 125 && loaded.Get("grokbot") == 80, "Distinct per-app zoom choices survive reload", lines);
        loaded.Set("grok", null);
        Check(new ZoomSettings(path).Get("grok") is null && new ZoomSettings(path).Get("openai") == 120, "Keep current zoom stops managing only the chosen app", lines);
        bool rejected = false;
        try { loaded.Set("openai", 125); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected && new ZoomSettings(path).Get("openai") == 120, "Unsupported zoom percentage cannot overwrite saved settings", lines);
        Check(ZoomLevels.Commands("openai", 120).SequenceEqual(new[] { '0', '+', '+' })
            && ZoomLevels.Commands("grokbot", 80).SequenceEqual(new[] { '0', '-', '-' })
            && ZoomLevels.Commands("grok", 125).SequenceEqual(new[] { '0', '+', '+' }), "Zoom commands use each app's actual percentage steps", lines);
        Check(ZoomLevels.Commands("grok", 100).SequenceEqual(new[] { '0' }), "100 percent resets directly without extra zoom steps", lines);
        File.WriteAllText(path, "{broken");
        var corrupt = new ZoomSettings(path);
        Check(corrupt.Get("openai") is null && corrupt.LoadWarning is not null, "Corrupt settings preserve current zoom and report a warning", lines);
        File.WriteAllText(path, "{\"grok\":135,\"openai\":80,\"unknown\":100}");
        var filtered = new ZoomSettings(path);
        Check(filtered.Get("grok") is null && filtered.Get("openai") == 80, "Invalid stored percentages are ignored without losing valid settings", lines);
        var sent = new List<char>();
        bool interrupted = false;
        try { await ZoomController.Apply("grokbot", 80, () => false, sent.Add); }
        catch (InvalidOperationException) { interrupted = true; }
        Check(interrupted && sent.Count == 0, "Zoom sends no keys when the intended app is not focused", lines);
        interrupted = false;
        try { await ZoomController.Apply("grokbot", 80, () => sent.Count == 0, sent.Add); }
        catch (InvalidOperationException) { interrupted = true; }
        Check(interrupted && sent.SequenceEqual(new[] { '0' }), "Focus loss cancels remaining zoom shortcuts", lines);
        File.Delete(path);
    }
}
