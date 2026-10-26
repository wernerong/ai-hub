using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace AIHub;

internal sealed class SavedWindow
{
    public long Handle { get; set; }
    public int Pid { get; set; }
    public long StartTicks { get; set; }
    public long Style { get; set; }
    public long ExStyle { get; set; }
    public long Parent { get; set; }
    public bool Visible { get; set; }
    public Native.Placement Placement { get; set; }
    public string Title { get; set; } = "";
    public bool Matches()
    {
        if (!Native.IsWindow((nint)Handle)) return false;
        Native.GetWindowThreadProcessId((nint)Handle, out uint pid);
        if (pid != Pid) return false;
        try { using var process = Process.GetProcessById(Pid); return process.StartTime.ToUniversalTime().Ticks == StartTicks; }
        catch { return false; }
    }
    public static SavedWindow Capture(nint hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        using var process = Process.GetProcessById((int)pid);
        var placement = new Native.Placement { Length = Marshal.SizeOf<Native.Placement>() };
        if (!Native.GetWindowPlacement(hwnd, ref placement)) throw new InvalidOperationException("Unable to save the app's original window position.");
        return new SavedWindow { Handle = (long)hwnd, Pid = (int)pid, StartTicks = process.StartTime.ToUniversalTime().Ticks,
            Style = (long)Native.GetWindowLong(hwnd, Native.GWL_STYLE), ExStyle = (long)Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE),
            Parent = (long)Native.GetParent(hwnd), Visible = Native.IsWindowVisible(hwnd), Placement = placement, Title = Native.Title(hwnd) };
    }
    public void Restore()
    {
        if (!Matches()) return;
        var hwnd = (nint)Handle;
        Native.ShowWindow(hwnd, Native.SW_HIDE);
        // Grouped apps stay top-level throughout; never reparent a user's app.
        Native.ChangeStyle(hwnd, Native.GWL_STYLE, Style);
        Native.ChangeStyle(hwnd, Native.GWL_EXSTYLE, ExStyle);
        var placement = Placement;
        placement.Length = Marshal.SizeOf<Native.Placement>();
        if (!Native.SetWindowPlacement(hwnd, ref placement)) throw new InvalidOperationException("Unable to restore the app's original position.");
        Native.SetWindowPos(hwnd, 0, 0, 0, 0, 0, 0x1 | 0x2 | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
        if (Visible) Native.ShowWindow(hwnd, placement.ShowCmd);
    }
}

internal static class Recovery
{
    internal static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true };
    internal static List<SavedWindow> Read(string path)
    {
        if (!File.Exists(path)) return [];
        // Save replaces the file atomically; readers must allow that rename.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonSerializer.Deserialize<List<SavedWindow>>(stream, Json) ?? [];
    }
    internal static void Save(string path, IEnumerable<SavedWindow> windows)
    {
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(windows, Json));
        File.Move(path + ".tmp", path, true);
    }
    internal static void RestoreFile(string path)
    {
        try
        {
            var pending = new List<SavedWindow>();
            foreach (var saved in Read(path))
            {
                try { saved.Restore(); }
                catch { pending.Add(saved); }
            }
            if (File.Exists(path)) Save(path, pending);
        }
        catch (IOException) { }
        catch (JsonException) { }
    }
    internal static void Guard(int pid, string path)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            File.WriteAllText(path + ".ready", "ready");
            process.WaitForExit();
        }
        catch (ArgumentException) { }
        RestoreFile(path);
        try { File.Delete(path + ".ready"); } catch (IOException) { }
    }
}

internal sealed class DockManager : IDisposable
{
    private readonly string statePath;
    private readonly Dictionary<string, SavedWindow> windows = [];
    private Process? guard;
    internal DockManager(string path) => statePath = path;
    internal IReadOnlyDictionary<string, SavedWindow> Windows => windows;
    internal bool Has(string key) => windows.TryGetValue(key, out var saved) && saved.Matches();

    internal async Task EnsureGuard()
    {
        if (guard is { HasExited: false }) return;
        var ready = statePath + ".ready";
        if (File.Exists(ready)) File.Delete(ready);
        guard = Program.StartSelf("--guard", Environment.ProcessId.ToString(), statePath);
        for (int i = 0; i < 50; i++)
        {
            if (File.Exists(ready)) return;
            if (guard.HasExited) break;
            await Task.Delay(100);
        }
        throw new InvalidOperationException("The window recovery helper could not start. No apps were attached.");
    }

    internal async Task Attach(string key, WindowInfo window, nint parent)
    {
        if (Has(key)) return;
        if (Native.GetParent(window.Handle) != 0) throw new InvalidOperationException("This app window already belongs to another window.");
        await EnsureGuard();
        var saved = SavedWindow.Capture(window.Handle);
        windows[key] = saved;
        Persist(); // Durable recovery record exists before the first window mutation.
        try
        {
            Native.ShowWindow(window.Handle, Native.SW_RESTORE);
            Native.ShowWindow(window.Handle, Native.SW_HIDE);
            Native.ChangeStyle(window.Handle, Native.GWL_STYLE, saved.Style & ~Native.Frame & ~0x01000000L & ~0x20000000L);
            Native.ChangeStyle(window.Handle, Native.GWL_EXSTYLE, (saved.ExStyle & ~0x00040000L) | 0x80); // One hub taskbar entry; no forced ownership.
            Native.Align(window.Handle, parent, true, true);
            Native.ShowWindow(window.Handle, Native.SW_SHOWNA);
        }
        catch
        {
            saved.Restore();
            windows.Remove(key);
            Persist();
            throw;
        }
    }

    internal void Select(string key, nint parent, bool activate = false)
    {
        // Hide other apps first. Showing an already-visible app with SW_SHOW
        // activates it in the middle of the hub's mouse/activation messages.
        foreach (var (name, saved) in windows)
        {
            if (name != key && saved.Matches() && Native.IsWindowVisible((nint)saved.Handle))
                Native.ShowWindow((nint)saved.Handle, Native.SW_HIDE);
        }
        Resize(key, parent, true);
        // An independent app can stay below the foreground hub despite
        // HWND_TOP + SWP_NOACTIVATE. Transfer focus after the panel click.
        if (activate && windows.TryGetValue(key, out var selected) && selected.Matches())
            Native.SetForegroundWindow((nint)selected.Handle);
    }
    internal bool IsForeground(string key)
    {
        return windows.TryGetValue(key, out var saved) && saved.Matches()
            && Native.GetForegroundWindow() == (nint)saved.Handle;
    }
    private static void ShowWithoutActivation(nint hwnd)
    {
        if (Native.IsIconic(hwnd)) Native.ShowWindow(hwnd, Native.SW_SHOWNOACTIVATE);
        else if (!Native.IsWindowVisible(hwnd)) Native.ShowWindow(hwnd, Native.SW_SHOWNA);
    }
    internal void Resize(string key, nint parent, bool raise = false)
    {
        if (windows.TryGetValue(key, out var saved) && saved.Matches())
        {
            if (raise) ShowWithoutActivation((nint)saved.Handle);
            Native.Align((nint)saved.Handle, parent, raise);
        }
    }
    internal void Detach(string key)
    {
        if (!windows.TryGetValue(key, out var saved)) return;
        saved.Restore();
        windows.Remove(key);
        Persist();
    }
    internal void HideAll()
    {
        foreach (var saved in windows.Values) if (saved.Matches()) Native.ShowWindow((nint)saved.Handle, Native.SW_HIDE);
    }
    internal void RestoreAll()
    {
        List<string> failures = [];
        foreach (var key in windows.Keys.ToArray())
            try { Detach(key); } catch (Exception ex) { failures.Add(ex.Message); }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
    }
    private void Persist() => Recovery.Save(statePath, windows.Values);
    public void Dispose()
    {
        RestoreAll();
        guard?.Dispose(); // Guard exits after this process and never terminates an app.
    }
}
