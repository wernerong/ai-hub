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
    internal bool Pending(long now) => target != 0 && activated && now - started <= 1000;
    internal bool Take(nint selected, bool hubForeground, bool inputHeld, long now)
    {
        if (target == 0) return false;
        if (selected != target || now - started > 1000) { Cancel(); return false; }
        if (!activated || !released || inputHeld || !hubForeground) return false;
        Cancel(); return true;
    }
    internal void Cancel() { target = 0; released = activated = false; }
}

internal sealed class TaskbarToggle : IDisposable
{
    private readonly object gate = new();
    private readonly TaskbarClick click = new();
    private readonly nint hub;
    private readonly Thread listener;
    private readonly HookProc callback;
    private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private nint target, pressedTarget;
    private Native.Point origin;
    private uint threadId;
    private bool disposed;
    private bool pressed;
    internal Task<bool> Ready => ready.Task;
    internal TaskbarToggle(nint hub)
    {
        this.hub = hub; callback = Observe;
        listener = new Thread(Listen) { IsBackground = true, Name = "AI Hub taskbar clicks" };
        listener.SetApartmentState(ApartmentState.STA); listener.Start();
    }
    internal void SetTarget(nint selected)
    {
        lock (gate) { if (target != selected) { click.Cancel(); pressedTarget = 0; } target = selected; }
    }
    internal void Cancel() { lock (gate) { click.Cancel(); pressedTarget = 0; } }
    internal void Activated(nint previous)
    {
        lock (gate) click.Activate(previous == 0 || previous == pressedTarget || IsTaskbarWindow(previous));
    }
    internal bool Pending { get { lock (gate) return click.Pending(Environment.TickCount64); } }
    internal bool Take(bool inputHeld)
    {
        lock (gate) return click.Take(target, Native.GetForegroundWindow() == hub, inputHeld, Environment.TickCount64);
    }
    private void Listen()
    {
        nint hook = 0;
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
                hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
            }
            ready.TrySetResult(hook != 0);
            if (hook != 0) Application.Run(context);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ExternalException) { ready.TrySetResult(false); }
        finally { if (hook != 0) UnhookWindowsHookEx(hook); ready.TrySetResult(false); }
    }
    private nint Observe(int code, nint message, nint data)
    {
        // Always pass input through. Only retain a short-lived left-click that
        // starts on a taskbar while the selected AI owns foreground focus.
        if (code >= 0 && message is 0x0200 or 0x0201 or 0x0202)
        {
            var input = Marshal.PtrToStructure<MouseInput>(data);
            lock (gate)
            {
                if (message == 0x0201)
                {
                    bool modified = new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(key => GetAsyncKeyState(key) < 0);
                    pressedTarget = target != 0 && Native.GetForegroundWindow() == target && IsTaskbarPoint(input.Point) && !modified ? target : 0;
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
                        var foreground = Native.GetForegroundWindow();
                        click.Release(IsTaskbarPoint(input.Point) && (foreground == pressedTarget || foreground == hub || IsTaskbarWindow(foreground)));
                    }
                }
            }
        }
        return CallNextHookEx(0, code, message, data);
    }
    internal static bool IsTaskbarClass(string name) => name is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    private static bool IsTaskbarWindow(nint window)
    {
        if (window == 0) return false;
        var name = new StringBuilder(128); GetClassName(GetAncestor(window, 2), name, name.Capacity);
        return IsTaskbarClass(name.ToString());
    }
    private static bool IsTaskbarPoint(Native.Point point) => IsTaskbarWindow(WindowFromPoint(point));
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; click.Cancel(); target = pressedTarget = 0; if (threadId != 0) PostThreadMessage(threadId, 0x0012, 0, 0); }
    }
    private delegate nint HookProc(int code, nint message, nint data);
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
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
