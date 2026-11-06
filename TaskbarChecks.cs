namespace AIHub;

internal static class TaskbarChecks
{
    internal static async Task Run(nint hub, List<string> lines)
    {
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); lines.Add("PASS " + message); }
        var click = new TaskbarClick();
        click.Begin(123, true, false, 100);
        Check(click.Pending(110) && !click.Take(123, true, false, 110),
            "An armed taskbar press holds focus restoration before hub activation without minimizing early");
        click.Activate(true);
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

        const int hubWindow = 9001, firstPane = 123, secondPane = 456, shell = 7001, unrelated = 8001;
        var foreground = new TaskbarForeground(hubWindow);
        Check(foreground.SetTargets([firstPane, secondPane]) && foreground.IsWorkspace(hubWindow)
            && foreground.IsWorkspace(firstPane) && foreground.IsWorkspace(secondPane) && !foreground.IsWorkspace(unrelated),
            "Taskbar foreground eligibility includes the hub and either visible split pane only");

        foreground.Observe(firstPane, false);
        Check(foreground.Observe(0, false) == firstPane,
            "A temporary lack of a foreground window preserves the hosted origin through activation");
        nint origin = foreground.Observe(shell, true);
        click.Begin(origin, true, false, 2000);
        Check(origin == firstPane && click.Pending(2010),
            "A shell focus hop before the first taskbar press retains the active AI gesture");
        foreground.Observe(hubWindow, false); click.Activate(true); click.Release(true);
        Check(click.Take(origin, foreground.IsWorkspace(hubWindow), false, 2050)
            && !click.Take(origin, foreground.IsWorkspace(hubWindow), false, 2060),
            "AI to shell to hub activation minimizes on one released click exactly once");

        foreground.Observe(secondPane, false);
        origin = foreground.Observe(shell, true);
        click.Begin(origin, true, false, 2100); click.Release(true);
        foreground.Observe(hubWindow, false); click.Activate(true);
        Check(origin == secondPane && click.Take(origin, foreground.IsWorkspace(hubWindow), false, 2150),
            "The second split pane also minimizes on its first taskbar click through shell focus");

        foreground.Observe(firstPane, false);
        origin = foreground.Observe(shell, true);
        click.Begin(origin, true, false, 2200);
        foreground.Observe(hubWindow, false); click.Activate(true); click.Release(true);
        foreground.Observe(firstPane, false);
        Check(click.Take(origin, foreground.IsWorkspace(firstPane), false, 2250),
            "A valid taskbar click still minimizes after focus returns to its AI before consumption");

        foreground.Observe(firstPane, false);
        foreground.Observe(unrelated, false);
        origin = foreground.Observe(shell, true);
        click.Begin(origin, true, false, 2300); click.Release(true);
        foreground.Observe(hubWindow, false); click.Activate(true);
        Check(origin == 0 && !click.Pending(2310) && !click.Take(origin, true, false, 2350),
            "Returning through the taskbar from an unrelated app restores instead of minimizing the workspace");

        foreground.Observe(secondPane, false);
        origin = foreground.Observe(shell, true);
        click.Begin(origin, true, false, 2400); click.Release(true);
        Check(!click.Take(origin, foreground.IsWorkspace(secondPane), false, 2450),
            "Remembered AI focus cannot minimize without activation of the hub taskbar button");
        click.Cancel();

        foreground.Observe(firstPane, false); foreground.Observe(hubWindow, false);
        Check(foreground.Observe(shell, true) == 0,
            "A taskbar press from the foreground hub leaves Windows native minimize behavior intact");
        foreground.Observe(secondPane, false);
        Check(!foreground.SetTargets([secondPane, firstPane, firstPane]) && foreground.Observe(shell, true) == secondPane,
            "Refreshing unchanged visible panes preserves the remembered foreground AI");
        Check(foreground.SetTargets([firstPane]) && foreground.Observe(shell, true) == 0
            && !foreground.IsWorkspace(secondPane),
            "Changing visible panes discards remembered taskbar focus from the old layout");
        foreground.Observe(firstPane, false); foreground.SetTargets([]);
        Check(foreground.Observe(shell, true) == 0 && !foreground.IsWorkspace(firstPane),
            "Minimized or unavailable workspaces cannot arm a taskbar gesture from stale AI focus");
        click.Begin(firstPane, true, false, 2500);
        Check(!click.Pending(3501), "An unactivated taskbar press stops holding focus restoration after expiry");

        // Replay the captured Windows secondary-taskbar sequence: the hosted
        // AI owns mouse-down, foreground is zero at mouse-up, then the hub activates.
        click.Begin(firstPane, true, false, 4000);
        click.Release(0, onTaskbar: true, workspaceForeground: false, taskbarForeground: false);
        Check(click.Pending(4100) && !click.Take(firstPane, false, false, 4100),
            "Taskbar mouse-up without a foreground window retains the click but waits for hub activation");
        click.Activate(true);
        Check(click.Take(firstPane, true, false, 4150) && !click.Take(firstPane, true, false, 4160),
            "The recorded secondary-taskbar foreground gap minimizes exactly once after hub activation");
        click.Begin(firstPane, true, false, 4200);
        click.Release(unrelated, onTaskbar: true, workspaceForeground: false, taskbarForeground: false);
        click.Activate(true);
        Check(!click.Take(firstPane, true, false, 4250),
            "Mouse-up with a known unrelated foreground window still cancels the taskbar gesture");
        click.Begin(firstPane, true, false, 4300);
        click.Release(0, onTaskbar: false, workspaceForeground: false, taskbarForeground: false);
        click.Activate(true);
        Check(!click.Take(firstPane, true, false, 4350),
            "A zero foreground window cannot validate mouse-up outside the taskbar");

        Check(TaskbarToggle.IsTaskbarClass("Shell_TrayWnd") && TaskbarToggle.IsTaskbarClass("Shell_SecondaryTrayWnd")
            && !TaskbarToggle.IsTaskbarClass("ApplicationFrameWindow"), "Primary and secondary Windows taskbars are recognized without treating app windows as taskbars");
        using var monitor = new TaskbarToggle(hub);
        Check(await monitor.Ready.WaitAsync(TimeSpan.FromSeconds(5)), "Taskbar click listener starts on its own message-loop thread");
    }
}
