using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text.Json;

namespace AIHub;

internal static class UpdateChecks
{
    internal static async Task Run(string folder, List<string> lines)
    {
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            lines.Add("PASS " + label);
        }
        bool Reject(Action action)
        {
            try { action(); return false; }
            catch (InvalidDataException) { return true; }
        }
        string root = Path.Combine(folder, "updates-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var current = new Version(0, 2, 1);
        string digest = new('a', 64);
        string Metadata(string version, string hash, long size, bool draft = false, bool prerelease = false, string? download = null) =>
            JsonSerializer.Serialize(new
            {
                draft, prerelease, tag_name = "v" + version,
                assets = new[] { new { name = $"AIHub-v{version}-windows-x64.zip", digest = "sha256:" + hash, size,
                    browser_download_url = download ?? UpdateRelease.ReleasesUrl + $"/download/v{version}/AIHub-v{version}-windows-x64.zip" } }
            });
        Check(UpdateRelease.Parse(Metadata("0.3.0", digest, 100), current)?.Version == new Version(0, 3, 0), "Updater selects a newer public Windows release");
        Check(UpdateRelease.Parse(Metadata("0.2.1", digest, 100), current) is null
            && UpdateRelease.Parse(Metadata("0.1.3", digest, 100), current) is null, "Updater rejects equal versions and downgrades");
        Check(UpdateRelease.Parse(Metadata("0.3.0", digest, 100, draft: true), current) is null
            && UpdateRelease.Parse(Metadata("0.3.0", digest, 100, prerelease: true), current) is null, "Draft and prerelease builds never enter automatic updates");
        Check(Reject(() => UpdateRelease.Parse(Metadata("0.3.0", digest, 100, download: "https://example.invalid/update.zip"), current)), "Updater rejects packages outside the configured GitHub repository");
        Check(Reject(() => UpdateRelease.Parse(Metadata("0.3.0", "bad", 100), current))
            && Reject(() => UpdateRelease.Parse(Metadata("0.3.0", digest, UpdateRelease.MaxPackageBytes + 1), current)), "Updater requires a valid SHA-256 digest and bounded package size");
        string corrupt = Path.Combine(root, "corrupt.zip"); File.WriteAllText(corrupt, "not a release");
        Check(Reject(() => UpdateInstaller.VerifyHash(corrupt, digest)), "A corrupt download fails verification before installation");
        string unsafeZip = Path.Combine(root, "unsafe.zip");
        using (var zip = ZipFile.Open(unsafeZip, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("../escape.txt").Open())) writer.Write("escape");
        Check(Reject(() => UpdateInstaller.Extract(unsafeZip, Path.Combine(root, "unsafe-payload"), new Version(0, 3, 0)))
            && !File.Exists(Path.Combine(root, "escape.txt")), "Update extraction rejects traversal before writing any files");
        string duplicateZip = Path.Combine(root, "duplicate.zip");
        using (var zip = ZipFile.Open(duplicateZip, ZipArchiveMode.Create)) { zip.CreateEntry("AIHub.exe"); zip.CreateEntry("AIHub.exe"); }
        Check(Reject(() => UpdateInstaller.Extract(duplicateZip, Path.Combine(root, "duplicate-payload"), new Version(0, 3, 0))), "Duplicate package files are rejected");
        string wrongVersion = Path.Combine(root, "wrong-version.zip");
        using (var zip = ZipFile.Open(wrongVersion, ZipArchiveMode.Create))
            foreach (var name in UpdateInstaller.PackageFiles)
            {
                if (name == "AIHub.exe") zip.CreateEntryFromFile(Environment.ProcessPath!, name, CompressionLevel.Fastest);
                else zip.CreateEntry(name);
            }
        Check(Reject(() => UpdateInstaller.Extract(wrongVersion, Path.Combine(root, "wrong-version-payload"), new Version(99, 0, 0))), "The embedded executable version must match the release tag");

        string installation = Path.Combine(root, "installation"), payload = Path.Combine(root, "fixture-payload");
        Directory.CreateDirectory(installation); Directory.CreateDirectory(payload);
        foreach (var name in UpdateInstaller.PackageFiles) { File.WriteAllText(Path.Combine(installation, name), "old " + name); File.WriteAllText(Path.Combine(payload, name), "new " + name); }
        File.WriteAllText(Path.Combine(installation, "personal.txt"), "keep");
        bool rolledBack = false;
        try
        {
            UpdateInstaller.InstallFiles(installation, payload, Path.Combine(root, "rollback-backup"), (source, target) =>
            {
                if (Path.GetFileName(target) == "AIHub.exe") throw new IOException("Synthetic locked executable");
                File.Move(source, target, true);
            });
        }
        catch (IOException) { rolledBack = true; }
        Check(rolledBack && UpdateInstaller.PackageFiles.All(n => File.ReadAllText(Path.Combine(installation, n)) == "old " + n), "An installation failure rolls back every replaced file");
        UpdateInstaller.InstallFiles(installation, payload, Path.Combine(root, "success-backup"));
        Check(UpdateInstaller.PackageFiles.All(n => File.ReadAllText(Path.Combine(installation, n)) == "new " + n)
            && File.ReadAllText(Path.Combine(installation, "personal.txt")) == "keep", "Installation replaces only package files and preserves unrelated data");

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var handler = new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Metadata("0.2.1", digest, 100)) });
        using var client = new HttpClient(handler);
        string state = Path.Combine(root, "preferences");
        using (var updater = new AppUpdater(state, Environment.ProcessPath!, current, client, () => now))
        {
            Check(updater.Automatic, "Public release updates default to enabled");
            updater.SetAutomatic(false); await updater.CheckAsync();
            Check(handler.Calls == 0, "Disabling automatic updates prevents background network requests");
            using var reload = new AppUpdater(state, Environment.ProcessPath!, current, client);
            Check(!reload.Automatic, "Automatic update opt-out persists across restarts");
            updater.SetAutomatic(true); await updater.CheckAsync(); await updater.CheckAsync(true);
            Check(handler.Calls == 1 && updater.Status.Contains("up to date"), "Update requests are throttled and current releases do not download");
            now = now.AddHours(6); await updater.CheckAsync();
            Check(handler.Calls == 2, "Background update checks resume after six hours");
        }
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var cancelClient = new HttpClient(new AsyncHandler(async token =>
        {
            waiting.SetResult(); await Task.Delay(Timeout.Infinite, token); return new HttpResponseMessage(HttpStatusCode.OK);
        })))
        using (var updater = new AppUpdater(Path.Combine(root, "cancel"), Environment.ProcessPath!, current, cancelClient))
        {
            var pending = updater.CheckAsync(); await waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
            updater.SetAutomatic(false); await pending;
            Check(!updater.Ready && !updater.Checking, "Disabling updates cancels an in-flight request without staging an update");
        }

        string releaseZip = Path.Combine(root, "release.zip");
        using (var zip = ZipFile.Open(releaseZip, ZipArchiveMode.Create))
            foreach (var name in UpdateInstaller.PackageFiles)
            {
                if (name == "AIHub.exe") zip.CreateEntryFromFile(Environment.ProcessPath!, name, CompressionLevel.Fastest);
                else { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write("new release " + name); }
            }
        string releaseHash = UpdateInstaller.Hash(releaseZip);
        long releaseSize = new FileInfo(releaseZip).Length;
        string publicMetadata = Metadata(AppInfo.Version, releaseHash, releaseSize);
        using var releaseClient = new HttpClient(new TestHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = request.RequestUri!.AbsoluteUri == UpdateRelease.LatestApi ? new StringContent(publicMetadata)
                : new StreamContent(File.OpenRead(releaseZip))
        }));
        string liveState = Path.Combine(root, "live-state"), fakeInstallation = Path.Combine(root, "helper-installation");
        Directory.CreateDirectory(fakeInstallation);
        string fakeExecutable = Path.Combine(fakeInstallation, "AIHub.exe");
        File.Copy(Environment.ProcessPath!, fakeExecutable);
        File.WriteAllText(Path.Combine(fakeInstallation, "README.txt"), "old readme");
        using (var updater = new AppUpdater(liveState, fakeExecutable, current, releaseClient))
        {
            await updater.CheckAsync();
            Check(updater.Ready && File.ReadAllText(Path.Combine(fakeInstallation, "README.txt")) == "old readme",
                "Verified releases stage successfully without changing a running installation");
        }
        string stage = Directory.EnumerateDirectories(Path.Combine(liveState, "updates")).Single();
        string planPath = Path.Combine(stage, "update-plan.json");
        string readyFile = Path.Combine(root, "host-ready"), exitFile = Path.Combine(root, "host-exit");
        using var host = Start(fakeExecutable, "--update-test-host", readyFile, exitFile);
        try
        {
            for (int i = 0; i < 100 && !File.Exists(readyFile) && !host.HasExited; i++) await Task.Delay(50);
            Check(File.Exists(readyFile), "Disposable update host starts from its own installation");
            var plan = JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(planPath))! with
            { ParentId = host.Id, ParentStartedUtcTicks = host.StartTime.ToUniversalTime().Ticks };
            File.WriteAllText(planPath, JsonSerializer.Serialize(plan));
            string helperPath = Path.Combine(stage, "AIHub.Update.exe"); File.Copy(Environment.ProcessPath!, helperPath);
            using var helper = Start(helperPath, "--apply-update", planPath);
            await Task.Delay(400);
            Check(!helper.HasExited && File.ReadAllText(Path.Combine(fakeInstallation, "README.txt")) == "old readme", "Update helper waits for the original process to exit");
            File.WriteAllText(exitFile, "exit");
            await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Check(helper.ExitCode == 0 && File.ReadAllText(Path.Combine(fakeInstallation, "README.txt")) == "new release README.txt"
                && File.Exists(Path.Combine(stage, ".completed")), "Detached update helper verifies, installs and completes after shutdown");
            UpdateInstaller.Cleanup(Path.Combine(liveState, "updates"));
            Check(!Directory.Exists(stage), "Completed update staging and helper binaries are cleaned up safely");
        }
        finally
        {
            File.WriteAllText(exitFile, "exit");
            if (!host.HasExited) await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        using var mismatchClient = new HttpClient(new TestHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = request.RequestUri!.AbsoluteUri == UpdateRelease.LatestApi ? new StringContent(Metadata(AppInfo.Version, digest, releaseSize))
                : new StreamContent(File.OpenRead(releaseZip))
        }));
        using (var updater = new AppUpdater(Path.Combine(root, "mismatch"), fakeExecutable, current, mismatchClient))
        {
            await updater.CheckAsync();
            Check(!updater.Ready && updater.Status.Contains("current version is unchanged"), "A mismatched release digest never becomes an installable update");
        }
        using var oversizeClient = new HttpClient(new TestHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = request.RequestUri!.AbsoluteUri == UpdateRelease.LatestApi ? new StringContent(Metadata(AppInfo.Version, digest, 10))
                : new ByteArrayContent(new byte[11])
        }));
        using (var updater = new AppUpdater(Path.Combine(root, "oversize"), fakeExecutable, current, oversizeClient))
        {
            await updater.CheckAsync();
            Check(!updater.Ready && updater.Status.Contains("current version is unchanged"), "An oversized download is rejected without changing the installation");
        }
    }

    private static Process Start(string executable, params string[] args)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return Process.Start(info) ?? throw new IOException("Disposable update process failed to start.");
    }
    private sealed class TestHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; return Task.FromResult(respond(request)); }
    }
    private sealed class AsyncHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(token);
    }
}
