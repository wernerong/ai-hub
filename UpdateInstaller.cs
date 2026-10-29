using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace AIHub;

internal sealed record UpdatePlan(string InstallDirectory, string Version, string PackageSha256,
    string PreviousExeSha256, int ParentId, long ParentStartedUtcTicks, bool Relaunch);

internal static class UpdateInstaller
{
    internal static readonly string[] PackageFiles = ["README.txt", "validation.txt", "Install-AIHub.ps1", "Uninstall-AIHub.ps1", "AIHub.exe"];
    internal static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    internal static void VerifyHash(string path, string expected)
    {
        if (expected.Length != 64 || !Hash(path).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update checksum does not match the release.");
    }

    internal static void Extract(string archivePath, string destination, Version expectedVersion)
    {
        if (Directory.Exists(destination)) throw new IOException("Update extraction requires a new folder.");
        using var archive = ZipFile.OpenRead(archivePath);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        foreach (var entry in archive.Entries)
        {
            expanded += entry.Length;
            if (!PackageFiles.Contains(entry.FullName, StringComparer.Ordinal) || !names.Add(entry.FullName)
                || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || expanded > 512L * 1024 * 1024)
                throw new InvalidDataException("The update contains an unexpected file or path.");
        }
        if (!names.SetEquals(PackageFiles)) throw new InvalidDataException("The update package is incomplete.");
        Directory.CreateDirectory(destination);
        foreach (var entry in archive.Entries) entry.ExtractToFile(Path.Combine(destination, entry.FullName));
        string version = FileVersionInfo.GetVersionInfo(Path.Combine(destination, "AIHub.exe")).ProductVersion?.Split('+')[0] ?? "";
        if (version != expectedVersion.ToString()) throw new InvalidDataException("The executable version does not match the release.");
    }

    // All files are prepared before replacement. The executable is replaced last.
    // Backups let an IO failure restore every file already replaced.
    internal static void InstallFiles(string installation, string payload, string backup, Action<string, string>? replace = null)
    {
        replace ??= (source, target) => File.Move(source, target, true);
        if (!Directory.Exists(installation) || (File.GetAttributes(installation) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The installation folder is missing or redirected.");
        Directory.CreateDirectory(backup);
        string suffix = ".update-" + Guid.NewGuid().ToString("N");
        var replaced = new List<string>();
        var existed = new HashSet<string>();
        try
        {
            foreach (var name in PackageFiles)
            {
                string target = Path.Combine(installation, name);
                if (File.Exists(target))
                {
                    if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) throw new IOException("An installation file is redirected.");
                    File.Copy(target, Path.Combine(backup, name)); existed.Add(name);
                }
                File.Copy(Path.Combine(payload, name), target + suffix);
            }
            foreach (var name in PackageFiles)
            {
                replace(Path.Combine(installation, name) + suffix, Path.Combine(installation, name));
                replaced.Add(name);
            }
        }
        catch
        {
            foreach (var name in replaced.AsEnumerable().Reverse())
            {
                string target = Path.Combine(installation, name);
                if (existed.Contains(name)) File.Copy(Path.Combine(backup, name), target, true);
                else File.Delete(target);
            }
            throw;
        }
        finally
        {
            foreach (var name in PackageFiles)
                try { File.Delete(Path.Combine(installation, name) + suffix); } catch (IOException) { }
        }
    }

    internal static int Run(string planPath)
    {
        string stage = Path.GetDirectoryName(Path.GetFullPath(planPath))!;
        UpdatePlan? plan = null;
        try
        {
            if (Path.GetFileName(planPath) != "update-plan.json"
                || !Path.GetFullPath(Environment.ProcessPath!).Equals(Path.Combine(stage, "AIHub.Update.exe"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unexpected update helper location.");
            plan = JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(planPath)) ?? throw new InvalidDataException("Missing update plan.");
            string target = Path.Combine(Path.GetFullPath(plan.InstallDirectory), "AIHub.exe");
            if (target.StartsWith(stage + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The installation cannot be inside update staging.");
            WaitForParent(plan, target);
            // A window-recovery companion can briefly outlive the main process.
            // Wait for matching executables; never terminate them or provider apps.
            foreach (var process in Process.GetProcessesByName("AIHub"))
                using (process)
                {
                    try
                    {
                        if (string.Equals(process.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase) && !process.WaitForExit(10000))
                            throw new IOException("An AI Hub process is still using this installation.");
                    }
                    catch (InvalidOperationException) when (process.HasExited) { }
                    catch (System.ComponentModel.Win32Exception) when (process.HasExited) { }
                }
            VerifyHash(target, plan.PreviousExeSha256);
            VerifyHash(Path.Combine(stage, "package.zip"), plan.PackageSha256);
            string payload = Path.Combine(stage, "applying");
            Extract(Path.Combine(stage, "package.zip"), payload, Version.Parse(plan.Version));
            InstallFiles(plan.InstallDirectory, payload, Path.Combine(stage, "backup"));
            try
            {
                using var registrations = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", true);
                if (registrations is not null) UpdateRegistration(registrations, plan.InstallDirectory, plan.Version);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            try { File.WriteAllText(Path.Combine(stage, ".completed"), "Installed " + plan.Version); }
            catch (IOException) { }
            WriteResult(stage, "AI Hub v" + plan.Version + " installed successfully.");
            if (plan.Relaunch)
                try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
                catch { WriteResult(stage, "The update installed successfully. Open AI Hub from its shortcut."); }
            return 0;
        }
        catch
        {
            WriteResult(stage, "The update could not be installed. The previous version is retained; check for updates again.");
            if (plan?.Relaunch == true)
                try { Process.Start(new ProcessStartInfo(Path.Combine(plan.InstallDirectory, "AIHub.exe")) { UseShellExecute = true }); } catch { }
            return 1;
        }
    }

    internal static void UpdateRegistration(RegistryKey registrations, string installation, string version)
    {
        foreach (var name in new[] { "AIHub", "AIHub_is1" })
        {
            using var registration = registrations.OpenSubKey(name, true);
            if (registration?.GetValue("InstallLocation") is string location
                && string.Equals(location.TrimEnd('\\'), installation.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                registration.SetValue("DisplayVersion", version);
        }
    }

    private static void WaitForParent(UpdatePlan plan, string target)
    {
        Process parent;
        try { parent = Process.GetProcessById(plan.ParentId); }
        catch (ArgumentException) { return; }
        using (parent)
        {
            try
            {
                if (parent.HasExited || parent.StartTime.ToUniversalTime().Ticks != plan.ParentStartedUtcTicks) return;
                if (!string.Equals(parent.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The update process identity does not match.");
                if (!parent.WaitForExit(30000)) throw new IOException("AI Hub is still running; no files were replaced.");
            }
            catch (InvalidOperationException) when (parent.HasExited) { }
            catch (System.ComponentModel.Win32Exception) when (parent.HasExited) { }
        }
    }

    internal static void WriteResult(string folder, string message)
    {
        try
        {
            Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "update-result.txt"), message);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static void Cleanup(string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (var folder in Directory.EnumerateDirectories(root))
            if (File.Exists(Path.Combine(folder, ".completed")) || Directory.GetLastWriteTimeUtc(folder) < DateTime.UtcNow.AddDays(-7))
                RemoveStage(folder, root);
    }
    internal static void RemoveStage(string folder, string root)
    {
        try
        {
            string full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParseExact(Path.GetFileName(full), "N", out _) || !Directory.Exists(full)
                || (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) return;
            var pending = new Stack<string>(); pending.Push(full);
            while (pending.TryPop(out var directory))
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) return;
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                }
            Directory.Delete(full, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
