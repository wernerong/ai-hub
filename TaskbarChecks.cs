namespace AIHub;

internal static class TaskbarChecks
{
    internal static async Task Run(nint hub, List<string> lines)
    {
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); lines.Add("PASS " + message); }
        var click = new TaskbarClick();
        click.Begin(123, true, false, 100); click.Activate(true);
        Check(!click.Take(123, true, true, 120), "Taskbar toggle waits for the mouse button to be released");
        click.Release(true);
        Check(click.Take(123, true, false, 140) && !click.Take(123, true, false, 150), "A taskbar click minimizes the active AI workspace exactly once");
        click.Begin(123, true, false, 200); click.Release(true); click.Activate(true);
        Check(click.Take(123, true, false, 240), "Taskbar release before hub activation is also supported");
        click.Begin(0, true, false, 300); click.Release(true); click.Activate(true);
        Check(!click.Take(123, true, false, 350), "A click while the hub is minimized leaves normal restore behavior intact");
        click.Begin(123, false, false, 400); click.Release(true); click.Activate(true);
        Check(!click.Take(123, true, false, 450), "Sidebar and title-bar clicks never trigger taskbar minimization");
        click.Begin(123, true, true, 500); click.Release(true); click.Activate(true);
        Check(!click.Take(123, true, false, 550), "Modified taskbar clicks retain Windows behavior");
        click.Begin(123, true, false, 600); click.Release(true); click.Activate(false);
        Check(!click.Take(123, true, false, 650), "Returning from an unrelated app cannot consume an earlier taskbar click");
        click.Begin(123, true, false, 700); click.Release(false); click.Activate(true);
        Check(!click.Take(123, true, false, 750), "Dragging or releasing outside the taskbar cancels minimization");
        click.Begin(123, true, false, 800); click.Release(true); click.Activate(true);
        Check(!click.Take(456, true, false, 850) && !click.Take(123, true, false, 900), "Changing the selected AI discards a pending taskbar toggle");
        click.Begin(123, true, false, 900); click.Release(true); click.Activate(true);
        Check(!click.Take(123, true, false, 1901), "Expired taskbar clicks cannot minimize a later activation");
        click.Begin(123, true, false, 1000); click.Release(true); click.Activate(true); click.Cancel();
        Check(!click.Take(123, true, false, 1050), "Opening a page or minimizing explicitly cancels a pending taskbar click");
        click.Begin(123, true, false, 1100); click.Release(true);
        Check(!click.Take(123, false, false, 1150) && !click.Take(123, true, false, 1200), "Another taskbar button does not minimize AI Hub without hub activation");
        Check(TaskbarToggle.IsTaskbarClass("Shell_TrayWnd") && TaskbarToggle.IsTaskbarClass("Shell_SecondaryTrayWnd")
            && !TaskbarToggle.IsTaskbarClass("ApplicationFrameWindow"), "Primary and secondary Windows taskbars are recognized without treating app windows as taskbars");
        using var monitor = new TaskbarToggle(hub);
        Check(await monitor.Ready.WaitAsync(TimeSpan.FromSeconds(5)), "Taskbar click listener starts on its own message-loop thread");
    }
}
