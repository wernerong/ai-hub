using System.Diagnostics;

namespace AIHub;

// Foreground-window tests need an actual click: Windows correctly prevents a
// background self-test from acquiring foreground permission on its own.
internal static class ActivationChecks
{
    internal static void CheckPolicy(List<string> lines)
    {
        var request = new WorkspaceActivation();
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            lines.Add("PASS " + label);
        }
        request.Request();
        Check(!request.TakeRequest(true, true) && request.TakeRequest(true, false), "Returning app focus waits for a mouse press or connection to finish");
        Check(!request.TakeRequest(true, false), "Returning focus is restored once rather than on every timer tick");
        request.Request(); request.TakeRequest(true, true); request.Cancel();
        Check(!request.TakeRequest(true, false), "A provider or settings click cancels restoration of the previous app");
        request.Request();
        Check(!request.TakeRequest(false, false) && !request.TakeRequest(true, false), "Switching away or opening a page discards a pending focus restoration");
    }

    internal static void Show(string folder)
    {
        Directory.CreateDirectory(folder);
        using var form = new Form { Text = "AI Hub activation checks", Size = new Size(800, 550), StartPosition = FormStartPosition.CenterScreen };
        var viewport = new Panel { Dock = DockStyle.Fill };
        var run = new Button { Text = "Run activation checks", AccessibleName = "Run activation checks", Dock = DockStyle.Top, Height = 48 };
        var result = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Bottom, Height = 160, ScrollBars = ScrollBars.Vertical,
            Text = "Click Run to test switching away and returning using disposable windows only." };
        form.Controls.Add(viewport); form.Controls.Add(result); form.Controls.Add(run);
        run.Click += async (_, _) =>
        {
            run.Enabled = false;
            var lines = new List<string>();
            void Check(bool condition, string label)
            {
                if (!condition) throw new InvalidOperationException(label);
                lines.Add("PASS " + label);
            }
            Process? sample = null, unrelated = null;
            using var manager = new DockManager(Path.Combine(folder, "activation-windows.json"));
            try
            {
                sample = Program.StartSelf("--sample");
                WindowInfo? window = null;
                for (int i = 0; i < 80 && window is null; i++)
                {
                    await Task.Delay(50);
                    window = Native.Windows().FirstOrDefault(w => w.Pid == sample.Id);
                }
                if (window is null) throw new InvalidOperationException("Disposable selected app did not start.");
                await manager.Attach("sample", window, viewport.Handle);
                unrelated = Program.StartSelf("--sample");
                WindowInfo? otherWindow = null;
                for (int i = 0; i < 80 && otherWindow is null; i++)
                {
                    await Task.Delay(50);
                    otherWindow = Native.Windows().FirstOrDefault(w => w.Pid == unrelated.Id);
                }
                if (otherWindow is null) throw new InvalidOperationException("Disposable unrelated app did not start.");
                var activation = new WorkspaceActivation();
                for (int repeat = 1; repeat <= 5; repeat++)
                {
                    Native.SetForegroundWindow(otherWindow.Handle); await Task.Delay(60);
                    Check(Native.GetForegroundWindow() == otherWindow.Handle, "Unrelated fixture receives focus");
                    manager.Resize("sample", viewport.Handle);
                    Check(Native.GetForegroundWindow() == otherWindow.Handle && Native.IsAbove(otherWindow.Handle, window.Handle), "Background alignment leaves the other app in front");
                    Native.SetForegroundWindow(form.Handle); await Task.Delay(60);
                    Check(Native.GetForegroundWindow() == form.Handle, "Hub fixture receives returning activation");
                    activation.Request();
                    Check(!activation.TakeRequest(true, true) && Native.GetForegroundWindow() == form.Handle,
                        "A mouse press keeps focus on the hub until it finishes");
                    if (activation.TakeRequest(true, false)) manager.Select("sample", viewport.Handle, true);
                    await Task.Delay(60);
                    if (!Native.IsAbove(window.Handle, form.Handle) || !Native.IsAbove(form.Handle, otherWindow.Handle))
                        lines.Add($"DETAIL appVisible={Native.IsWindowVisible(window.Handle)}, appAboveHub={Native.IsAbove(window.Handle, form.Handle)}, hubAboveOther={Native.IsAbove(form.Handle, otherWindow.Handle)}, hubFocused={Native.GetForegroundWindow() == form.Handle}");
                    Check(Native.IsWindowVisible(window.Handle) && Native.IsAbove(window.Handle, form.Handle)
                        && Native.IsAbove(form.Handle, otherWindow.Handle), $"Return {repeat} restores the selected app above the workspace");
                    Check(Native.GetForegroundWindow() == window.Handle, "Settled return activation focuses the selected app");
                    form.WindowState = FormWindowState.Minimized; manager.HideAll();
                    await Task.Delay(60);
                    Check(Native.IsIconic(form.Handle) && !Native.IsWindowVisible(window.Handle) && !sample.HasExited,
                        $"Minimize {repeat} hides the selected AI without closing it");
                    form.WindowState = FormWindowState.Normal; Native.SetForegroundWindow(form.Handle);
                    await Task.Delay(60);
                    manager.Select("sample", viewport.Handle, true);
                    await Task.Delay(60);
                    Check(!Native.IsIconic(form.Handle) && Native.IsWindowVisible(window.Handle)
                        && Native.IsAbove(window.Handle, form.Handle) && Native.GetForegroundWindow() == window.Handle,
                        $"Restore {repeat} returns the same AI above the workspace with focus");
                }
            }
            catch (Exception ex) { lines.Add("FAIL " + ex.Message); }
            finally
            {
                try { manager.RestoreAll(); } catch (Exception ex) { lines.Add("FAIL cleanup: " + ex.Message); }
                foreach (var process in new[] { sample, unrelated })
                {
                    if (process is { HasExited: false })
                    {
                        process.CloseMainWindow();
                        if (!process.WaitForExit(1500)) process.Kill(); // Only disposable test processes.
                    }
                    process?.Dispose();
                }
                File.WriteAllLines(Path.Combine(folder, "activation-validation.txt"), lines);
                result.Text = string.Join(Environment.NewLine, lines); run.Text = "Checks complete";
            }
        };
        Application.Run(form);
    }
}
