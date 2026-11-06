using System.Runtime.InteropServices;
using System.Text;

namespace AIHub;

// A hosted AI is a separate top-level window. Explorer therefore activates the
// hub instead of minimizing it. Pair a real taskbar click with activation of
// this hub; clicking another taskbar button must never minimize our workspace.
internal sealed class TaskbarClick
{
    private nint target;
    private long started;
    private bool released, activated;
    internal void Begin(nint hosted, bool taskbar, bool modified, long now)
    {
        Cancel();
        if (hosted == 0 || !taskbar || modified) return;
        target = hosted; started = now;
    }
    internal void Release(bool validClick) { if (!validClick) Cancel(); else released = target != 0; }
    internal void Activate(bool sameWorkspace) { if (!sameWorkspace) Cancel(); else activated = target != 0; }
    internal bool Pending(long now) => target != 0 && now - started <= 1000;
    internal bool Take(nint selected, bool workspaceForeground, bool inputHeld, long now)
    {
        if (target == 0) return false;
        if (selected != target || now - started > 1000) { Cancel(); return false; }
        if (!activated || !released || inputHeld || !workspaceForeground) return false;
        Cancel(); return true;
    }
    internal void Cancel() { target = 0; released = activated = false; }
}

// Keep only the last hosted foreground identity through a taskbar focus hop.
// An unrelated app or the hub itself clears it: their next taskbar click must
// retain Windows' normal activation/restore/minimize behavior.
internal sealed class TaskbarForeground(nint hub)
{
    private nint[] targets = [];
    private nint lastHosted;
    internal bool SetTargets(IEnumerable<nint> hosted)
    {
        var next = hosted.Where(h => h != 0).Distinct().Order().ToArray();
        if (targets.SequenceEqual(next)) return false;
        targets = next; lastHosted = 0; return true;
    }
    internal nint Observe(nint foreground, bool taskbar)
    {
        if (targets.Contains(foreground)) lastHosted = foreground;
        else if (foreground != 0 && !taskbar) lastHosted = 0;
        return lastHosted;
    }
    internal bool IsWorkspace(nint foreground) => foreground != 0 && (foreground == hub || targets.Contains(foreground));
}

internal sealed class TaskbarToggle : IDisposable
{
    internal const int StateChangedMessage = 0x8051;
    private readonly object gate = new();
    private readonly TaskbarClick click = new();
    private readonly nint hub;
    private readonly Thread listener;
    private readonly HookProc callback;
    private readonly WinEventProc foregroundCallback;
    private readonly TaskbarForeground foreground;
    private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private nint pressedTarget;
    private Native.Point origin;
    private uint threadId;
    private bool disposed;
    private bool pressed;
    internal Task<bool> Ready => ready.Task;
    internal TaskbarToggle(nint hub)
    {
        this.hub = hub; callback = Observe; foreground = new(hub);
        // Out-of-context events can be queued behind newer mouse input. Refresh
        // the current foreground instead of replaying an obsolete event HWND.
        foregroundCallback = (_, _, _, _, _, _, _) => { lock (gate) TrackForeground(Native.GetForegroundWindow()); };
        listener = new Thread(Listen) { IsBackground = true, Name = "AI Hub taskbar clicks" };
        listener.SetApartmentState(ApartmentState.STA); listener.Start();
    }
    internal void SetTarget(nint selected)
    {
        SetTargets(selected == 0 ? [] : [selected]);
    }
    internal void SetTargets(IEnumerable<nint> hosted)
    {
        lock (gate)
        {
            if (foreground.SetTargets(hosted))
            {
                click.Cancel(); pressedTarget = 0; pressed = false;
                TrackForeground(Native.GetForegroundWindow());
            }
        }
    }
    internal void Cancel() { lock (gate) { click.Cancel(); pressedTarget = 0; pressed = false; } }
    internal void Activated(nint previous)
    {
        lock (gate)
        {
            click.Activate(previous == 0 || foreground.IsWorkspace(WorkspaceRoot(previous)) || IsTaskbarWindow(previous));
            if (click.Pending(Environment.TickCount64)) PostMessage(hub, StateChangedMessage, 0, 0);
        }
    }
    internal bool Pending { get { lock (gate) return click.Pending(Environment.TickCount64); } }
    internal bool Take(bool inputHeld)
    {
        lock (gate)
        {
            nint current = Native.GetForegroundWindow();
            TrackForeground(current);
            return click.Take(pressedTarget, foreground.IsWorkspace(WorkspaceRoot(current)), inputHeld, Environment.TickCount64);
        }
    }
    private void TrackForeground(nint window)
    {
        if (disposed) return;
        bool taskbar = IsTaskbarWindow(window);
        nint root = WorkspaceRoot(window);
        foreground.Observe(root, taskbar);
        if (window != 0 && !taskbar && !foreground.IsWorkspace(root))
        {
            click.Cancel(); pressedTarget = 0; pressed = false;
        }
    }
    private nint WorkspaceRoot(nint window)
    {
        if (foreground.IsWorkspace(window)) return window;
        nint owner = GetAncestor(window, 3);
        return foreground.IsWorkspace(owner) ? owner : window;
    }
    private void Listen()
    {
        nint hook = 0, foregroundHook = 0;
        try
        {
            // Give this thread its own message loop: a busy UI must not stall
            // Windows mouse input. No keystrokes or mouse history are recorded.
            using var context = new ApplicationContext();
            using var pump = new Control(); _ = pump.Handle;
            lock (gate)
            {
                if (disposed) { ready.TrySetResult(false); return; }
                threadId = GetCurrentThreadId();
                foregroundHook = SetWinEventHook(3, 3, 0, foregroundCallback, 0, 0, 0); // EVENT_SYSTEM_FOREGROUND
                hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
                TrackForeground(Native.GetForegroundWindow());
            }
            ready.TrySetResult(hook != 0 && foregroundHook != 0);
            if (hook != 0 && foregroundHook != 0) Application.Run(context);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ExternalException) { ready.TrySetResult(false); }
        finally
        {
            if (hook != 0) UnhookWindowsHookEx(hook);
            if (foregroundHook != 0) UnhookWinEvent(foregroundHook);
            ready.TrySetResult(false);
        }
    }
    private nint Observe(int code, nint message, nint data)
    {
        // Always pass input through. Only retain a short-lived left-click that
        // starts on a taskbar from a hosted AI, including a shell focus hop.
        if (code >= 0 && message is 0x0200 or 0x0201 or 0x0202)
        {
            var input = Marshal.PtrToStructure<MouseInput>(data);
            lock (gate)
            {
                if (message == 0x0201)
                {
                    bool modified = new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(key => GetAsyncKeyState(key) < 0);
                    nint current = Native.GetForegroundWindow();
                    TrackForeground(current);
                    nint hosted = foreground.Observe(WorkspaceRoot(current), IsTaskbarWindow(current));
                    pressedTarget = IsTaskbarPoint(input.Point) && !modified ? hosted : 0;
                    origin = input.Point;
                    pressed = pressedTarget != 0;
                    click.Begin(pressedTarget, pressedTarget != 0, modified, Environment.TickCount64);
                }
                else if (pressedTarget != 0 && pressed)
                {
                    bool moved = Math.Abs(input.Point.X - origin.X) > SystemInformation.DragSize.Width
                        || Math.Abs(input.Point.Y - origin.Y) > SystemInformation.DragSize.Height;
                    if (moved) { click.Cancel(); pressedTarget = 0; }
                    else if (message == 0x0202)
                    {
                        pressed = false;
                        nint current = Native.GetForegroundWindow();
                        TrackForeground(current);
                        click.Release(IsTaskbarPoint(input.Point) && (foreground.IsWorkspace(WorkspaceRoot(current)) || IsTaskbarWindow(current)));
                        if (click.Pending(Environment.TickCount64)) PostMessage(hub, StateChangedMessage, 0, 0);
                    }
                }
            }
        }
        return CallNextHookEx(0, code, message, data);
    }
    internal static bool IsTaskbarClass(string name) => name is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    private static nint TaskbarRoot(nint window)
    {
        if (window == 0) return 0;
        nint root = GetAncestor(window, 2);
        var name = new StringBuilder(128); GetClassName(root, name, name.Capacity);
        if (IsTaskbarClass(name.ToString())) return root;
        nint owner = GetAncestor(window, 3);
        GetClassName(owner, name, name.Capacity);
        return IsTaskbarClass(name.ToString()) ? owner : 0;
    }
    private static bool IsTaskbarWindow(nint window) => TaskbarRoot(window) != 0;
    private static bool IsTaskbarPoint(Native.Point point)
    {
        // Owned shell popups can carry foreground focus, but selecting a
        // thumbnail outside the taskbar must retain normal Windows behavior.
        nint taskbar = TaskbarRoot(WindowFromPoint(point));
        return taskbar != 0 && Native.GetWindowRect(taskbar, out var bounds)
            && point.X >= bounds.Left && point.X < bounds.Right && point.Y >= bounds.Top && point.Y < bounds.Bottom;
    }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; click.Cancel(); foreground.SetTargets([]); pressedTarget = 0; if (threadId != 0) PostThreadMessage(threadId, 0x0012, 0, 0); }
    }
    private delegate nint HookProc(int code, nint message, nint data);
    private delegate void WinEventProc(nint hook, uint eventType, nint window, int objectId, int childId, uint threadId, uint time);
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public Native.Point Point; public uint MouseData, Flags, Time; public nuint Extra; }
    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Native.Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int size);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint first, uint last, nint module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
