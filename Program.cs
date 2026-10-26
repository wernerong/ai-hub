using System.Diagnostics;
using System.Text.Json;

namespace AIHub;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--dpi-v1")) Application.SetHighDpiMode(HighDpiMode.PerMonitor);
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--usage-diagnostics")
        {
            using var readers = new AIHub.Usage.UsageReaders();
            using var monitor = new AIHub.Usage.UsageMonitor(readers.Providers);
            monitor.RefreshAsync().GetAwaiter().GetResult();
            File.WriteAllText(args[1], JsonSerializer.Serialize(monitor.States, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }
        if (args.Length >= 4 && args[0] == "--crash-host")
        {
            SelfTest.CrashHost((nint)long.Parse(args[1]), int.Parse(args[2]), args[3]);
            return;
        }
        if (args.Length >= 2 && args[0] == "--diagnostics")
        {
            var all = Native.Windows();
            var hub = all.FirstOrDefault(w => w.Title == AppInfo.DisplayName);
            var foreground = Native.GetForegroundWindow();
            Native.GetWindowThreadProcessId(foreground, out uint foregroundPid);
            var lines = new List<string> { $"Foreground: handle={foreground}, pid={foregroundPid}; hub={hub?.Handle}, hubExStyle={(hub is null ? 0 : Native.GetWindowLong(hub.Handle, Native.GWL_EXSTYLE)):X}" };
            var candidates = all.Where(w => AppEntry.All.Any(a => a.Matches(w))).Select(w => w.Handle).ToHashSet();
            var stateFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIHub");
            if (Directory.Exists(stateFolder))
                foreach (var path in Directory.EnumerateFiles(stateFolder, "windows*.json"))
                    foreach (var saved in Recovery.Read(path).Where(w => w.Matches())) candidates.Add((nint)saved.Handle);
            foreach (var hwnd in candidates)
            {
                Native.GetWindowThreadProcessId(hwnd, out uint pid);
                using var process = Process.GetProcessById((int)pid);
                Native.GetWindowRect(hwnd, out var rect);
                lines.Add($"{process.ProcessName}: handle={hwnd}, visible={Native.IsWindowVisible(hwnd)}, minimized={Native.IsIconic(hwnd)}, aboveHub={hub is not null && Native.IsAbove(hwnd, hub.Handle)}, exStyle={Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE):X}, bounds={rect.Left},{rect.Top},{rect.Right},{rect.Bottom}, DPI={Native.DpiName(hwnd)}");
            }
            File.WriteAllLines(args[1], lines);
            return;
        }
        if (args.Length >= 3 && args[0] == "--guard")
        {
            Recovery.Guard(int.Parse(args[1]), args[2]);
            return;
        }
        if (args.Length > 0 && args[0] == "--sample")
        {
            var sample = new Form { Text = "AI Hub test window", Size = new Size(700, 480), StartPosition = FormStartPosition.CenterScreen };
            if (args.Contains("--minimum-size")) sample.MinimumSize = new Size(400, 1000);
            sample.Controls.Add(new TextBox { Multiline = true, Dock = DockStyle.Fill, Text = "Disposable window for dock, resize and restore checks.", Font = new Font("Segoe UI", 15) });
            Application.Run(sample);
            return;
        }
        if (args.Length >= 2 && args[0] == "--self-test")
        {
            SelfTest.Run(args[1]);
            return;
        }
        using var mutex = new Mutex(true, @"Local\AIHub.Desktop", out bool first);
        if (!first)
        {
            MessageBox.Show("AI Hub is already running. Open its window from the taskbar.", "AI Hub");
            return;
        }
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIHub");
        Directory.CreateDirectory(data);
        foreach (var oldState in Directory.EnumerateFiles(data, "windows*.json")) Recovery.RestoreFile(oldState);
        var state = Path.Combine(data, $"windows-{Environment.ProcessId}-{Guid.NewGuid():N}.json");
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "AI Hub", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Run(new HubForm(state));
    }

    internal static Process StartSelf(params string[] args)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = args.FirstOrDefault() == "--sample" ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return Process.Start(start) ?? throw new InvalidOperationException("Unable to start the recovery helper.");
    }
}
