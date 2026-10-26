using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AIHub;

internal static class Native
{
    internal const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    internal const long Frame = 0x00C00000L | 0x00040000L | 0x00080000L | 0x00020000L | 0x00010000L;
    internal const uint SWP_FRAMECHANGED = 0x20, SWP_NOACTIVATE = 0x10, SWP_NOZORDER = 0x4;
    internal const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4, SW_SHOW = 5, SW_SHOWNA = 8, SW_RESTORE = 9;
    internal delegate bool EnumProc(nint window, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Placement
    {
        public int Length, Flags, ShowCmd;
        public Point MinPosition, MaxPosition;
        public Rect NormalPosition;
    }

    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, nint param);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetParent(nint hwnd);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint hwnd, StringBuilder text, int max);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] internal static extern nint GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern nint SetWindowLong(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetWindowPlacement(nint hwnd, ref Placement placement);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPlacement(nint hwnd, ref Placement placement);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint threadId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern short VkKeyScanEx(char character, nint layout);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public KeyboardInput Keyboard;
    }
    internal static void SendZoomChord(nint target, char command)
    {
        if (GetForegroundWindow() != target) throw new InvalidOperationException("The selected app is not focused.");
        if (new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(k => GetAsyncKeyState(k) < 0))
            throw new InvalidOperationException("Release Ctrl, Alt and Shift, then open the panel again to apply zoom.");
        uint thread = GetWindowThreadProcessId(target, out _);
        short mapping = VkKeyScanEx(command, GetKeyboardLayout(thread));
        if (mapping == -1 || ((mapping >> 8) & 6) != 0)
            throw new InvalidOperationException("This keyboard layout cannot send the app's zoom shortcut.");
        ushort key = (ushort)(mapping & 0xFF);
        bool shift = ((mapping >> 8) & 1) != 0;
        static Input Stroke(ushort key, bool up) => new() { Type = 1, Keyboard = new KeyboardInput { Key = key, Flags = up ? 2u : 0u } };
        var input = new List<Input> { Stroke(0x11, false) };
        if (shift) input.Add(Stroke(0x10, false));
        input.Add(Stroke(key, false));
        input.Add(Stroke(key, true));
        if (shift) input.Add(Stroke(0x10, true));
        input.Add(Stroke(0x11, true));
        if (SendInput((uint)input.Count, input.ToArray(), Marshal.SizeOf<Input>()) != input.Count)
            throw new InvalidOperationException("Windows could not deliver the zoom shortcut to this app.");
    }
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetWindow(nint hwnd, uint command);
    internal static bool IsAbove(nint window, nint other)
    {
        for (var current = GetWindow(other, 3); current != 0; current = GetWindow(current, 3))
            if (current == window) return true;
        return false;
    }
    [DllImport("user32.dll")] internal static extern nint GetWindowDpiAwarenessContext(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool AreDpiAwarenessContextsEqual(nint first, nint second);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    internal static string Title(nint hwnd)
    {
        var text = new StringBuilder(1024);
        GetWindowText(hwnd, text, text.Capacity);
        return text.ToString();
    }
    internal static string DpiName(nint hwnd)
    {
        var context = GetWindowDpiAwarenessContext(hwnd);
        foreach (var (value, name) in new (int, string)[] { (-1, "Unaware"), (-2, "SystemAware"), (-3, "PerMonitor"), (-4, "PerMonitorV2"), (-5, "UnawareGdiScaled") })
            if (AreDpiAwarenessContextsEqual(context, value)) return name;
        return context.ToString();
    }
    internal static void ChangeStyle(nint hwnd, int index, long style)
    {
        Marshal.SetLastPInvokeError(0);
        var result = SetWindowLong(hwnd, index, (nint)style);
        int error = Marshal.GetLastPInvokeError();
        if (result == 0 && error != 0) throw new Win32Exception(error);
    }
    internal static void Align(nint hwnd, nint viewport, bool raise = false, bool frameChanged = false)
    {
        if (!GetWindowRect(viewport, out var rect)) throw new InvalidOperationException("The workspace is not available.");
        GetWindowRect(hwnd, out var current);
        if (!raise && !frameChanged && current.Left == rect.Left && current.Top == rect.Top && current.Right == rect.Right && current.Bottom == rect.Bottom) return;
        if (!SetWindowPos(hwnd, 0, rect.Left, rect.Top, Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top), SWP_NOACTIVATE | (frameChanged ? SWP_FRAMECHANGED : 0) | (raise ? 0 : SWP_NOZORDER)))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
    internal static List<WindowInfo> Windows()
    {
        var result = new List<WindowInfo>();
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || GetParent(window) != 0) return true;
            GetWindowThreadProcessId(window, out uint pid);
            try
            {
                using var process = Process.GetProcessById((int)pid);
                var title = Title(window);
                if (!string.IsNullOrWhiteSpace(title)) result.Add(new WindowInfo(window, process.Id, process.ProcessName, title));
            }
            catch (ArgumentException) { }
            catch (Win32Exception) { }
            return true;
        }, 0);
        return result;
    }
}

internal sealed record WindowInfo(nint Handle, int Pid, string ProcessName, string Title)
{
    public override string ToString() => $"{Title}  —  {ProcessName}";
}
