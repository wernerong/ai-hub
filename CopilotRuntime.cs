using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIHub;

internal sealed record CopilotInstallation(string Executable, string Version);
internal sealed record CopilotRuntimeRelease(string Version, Uri Download, long Size, string Sha256);

internal sealed class CopilotRuntime
{
    internal const string ReleaseApi = "https://api.github.com/repos/github/copilot-cli/releases/latest";
    internal const string AssetName = "copilot-win32-x64.zip";
    internal const long MaxDownloadBytes = 512L * 1024 * 1024;
    internal const long MaxExpandedBytes = 2L * 1024 * 1024 * 1024;
    private readonly string runtimeRoot;
    private readonly SemaphoreSlim setup = new(1, 1);
    private sealed record ActiveRuntime(string Executable, string Version);

    internal CopilotRuntime(string stateRoot) => runtimeRoot = Path.Combine(Path.GetFullPath(stateRoot), "runtime", "copilot");

    internal async Task<CopilotInstallation?> FindAsync(CancellationToken cancellation = default)
    {
        var candidates = new List<string>();
        try
        {
            var manifest = JsonSerializer.Deserialize<ActiveRuntime>(File.ReadAllText(Path.Combine(runtimeRoot, "active.json")));
            if (manifest is not null) candidates.Add(SafeChild(runtimeRoot, manifest.Executable));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException) { }
        candidates.AddRange(ExternalCandidates(Environment.GetEnvironmentVariable("PATH"),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellation.ThrowIfCancellationRequested();
            if (!IsUsableCandidate(candidate)) continue;
            try
            {
                string? version = await ReadVersion(candidate, cancellation);
                if (version is not null) return new(candidate, version);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException) { }
        }
        return null;
    }

    internal async Task<CopilotInstallation> InstallAsync(IProgress<string>? progress = null, CancellationToken cancellation = default)
    {
        await setup.WaitAsync(cancellation);
        string? staging = null, installed = null;
        bool committed = false;
        try
        {
            Directory.CreateDirectory(runtimeRoot);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromMinutes(15));
            var token = deadline.Token;
            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AIHub/" + AppInfo.Version);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            progress?.Report("Checking the official Copilot release…");
            using var metadataResponse = await client.GetAsync(ReleaseApi, HttpCompletionOption.ResponseHeadersRead, token);
            metadataResponse.EnsureSuccessStatusCode();
            using var metadata = new MemoryStream();
            await CopyBounded(await metadataResponse.Content.ReadAsStreamAsync(token), metadata, 2 * 1024 * 1024, token);
            var release = ParseRelease(Encoding.UTF8.GetString(metadata.ToArray()));
            staging = Path.Combine(runtimeRoot, ".install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            string archive = Path.Combine(staging, "copilot.zip"), payload = Path.Combine(staging, "payload");
            progress?.Report($"Downloading Copilot {release.Version} ({release.Size / 1024 / 1024} MB)…");
            using (var response = await client.GetAsync(release.Download, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is long length && length != release.Size)
                    throw new InvalidDataException("The Copilot download size does not match its release.");
                await using var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                long reported = 0;
                await CopyBounded(await response.Content.ReadAsStreamAsync(token), output, release.Size, token, received =>
                {
                    if (received - reported < 4 * 1024 * 1024 && received != release.Size) return;
                    reported = received;
                    progress?.Report($"Downloading Copilot {release.Version}: {received / 1024 / 1024} of {release.Size / 1024 / 1024} MB…");
                });
            }
            progress?.Report("Verifying and preparing Copilot…");
            await Task.Run(() => VerifyArchive(archive, release), token);
            string relativeExecutable = await Task.Run(() => ExtractArchive(archive, payload, token), token);
            var version = await ReadVersion(SafeChild(payload, relativeExecutable), token);
            if (version != release.Version) throw new InvalidDataException("The downloaded Copilot version does not match its release.");
            token.ThrowIfCancellationRequested();
            // A unique version directory leaves an already-running runtime untouched.
            installed = Path.Combine(runtimeRoot, release.Version + "-" + Guid.NewGuid().ToString("N"));
            Directory.Move(payload, installed);
            string executable = SafeChild(installed, relativeExecutable);
            string active = Path.Combine(runtimeRoot, "active.json");
            File.WriteAllText(active + ".tmp", JsonSerializer.Serialize(new ActiveRuntime(Path.GetRelativePath(runtimeRoot, executable), version)));
            LocalFile.Replace(active + ".tmp", active);
            committed = true;
            progress?.Report("Copilot is ready. Sign in with your GitHub account.");
            return new(executable, version);
        }
        finally
        {
            if (staging is not null) RemoveOwnedDirectory(runtimeRoot, staging);
            if (!committed && installed is not null) RemoveOwnedDirectory(runtimeRoot, installed);
            setup.Release();
        }
    }

    internal async Task SignInAsync(CopilotInstallation installation, IProgress<string>? progress = null, CancellationToken cancellation = default)
    {
        if (!IsUsableCandidate(installation.Executable)) throw new FileNotFoundException("Set up Copilot before signing in.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        using var process = Start(installation.Executable, "login", "--host", "github.com", "--web-flow");
        progress?.Report("Complete Copilot sign-in in your browser. Credentials stay with Copilot.");
        var output = ForwardLines(process.StandardOutput, progress, deadline.Token);
        var error = ForwardLines(process.StandardError, progress, deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            await Task.WhenAll(output, error);
            if (process.ExitCode != 0) throw new InvalidOperationException("Copilot sign-in did not complete. Retry Sign in after checking the browser.");
            progress?.Report("Sign-in finished. Connect to verify your Copilot access.");
        }
        finally
        {
            await StopOwned(process);
            try { await Task.WhenAll(output, error); } catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
        }
    }

    internal static CopilotRuntimeRelease ParseRelease(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
            throw new InvalidDataException("Copilot setup requires a published stable release.");
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$"))
            throw new InvalidDataException("Copilot returned an unsupported release version.");
        var assets = root.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == AssetName).ToArray();
        if (assets.Length != 1) throw new InvalidDataException("The official Windows x64 Copilot package is missing or ambiguous.");
        var asset = assets[0];
        string expected = $"https://github.com/github/copilot-cli/releases/download/{tag}/{AssetName}";
        if (asset.GetProperty("browser_download_url").GetString() != expected)
            throw new InvalidDataException("Copilot setup only accepts the official release package.");
        string digest = asset.GetProperty("digest").GetString() ?? "";
        long size = asset.GetProperty("size").GetInt64();
        if (!Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$") || size is <= 0 or > MaxDownloadBytes)
            throw new InvalidDataException("Copilot requires a valid SHA-256 checksum and bounded package size.");
        return new(tag[1..], new Uri(expected), size, digest[7..].ToLowerInvariant());
    }

    internal static void VerifyArchive(string archive, CopilotRuntimeRelease release)
    {
        using var input = File.OpenRead(archive);
        if (input.Length != release.Size || !Convert.ToHexString(SHA256.HashData(input)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The Copilot download failed checksum or size verification. Try setup again.");
    }

    internal static string ExtractArchive(string archive, string destination, CancellationToken cancellation = default)
    {
        if (Directory.Exists(destination)) throw new InvalidDataException("Copilot extraction needs a new staging directory.");
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count == 0 || zip.Entries.Count > 20000) throw new InvalidDataException("The Copilot package has an invalid file count.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<(ZipArchiveEntry Entry, string Target, bool Directory)>();
        string? executable = null;
        long expanded = 0;
        foreach (var entry in zip.Entries)
        {
            cancellation.ThrowIfCancellationRequested();
            bool directory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            string relative = entry.FullName.TrimEnd('/', '\\').Replace('/', Path.DirectorySeparatorChar);
            string target = SafeChild(destination, relative);
            if (!paths.Add(target) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000
                || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The Copilot archive contains duplicate or linked paths.");
            expanded = checked(expanded + entry.Length);
            if (expanded > MaxExpandedBytes || entry.Length > MaxExpandedBytes / 2)
                throw new InvalidDataException("The expanded Copilot package is too large.");
            if (!directory && Path.GetFileName(target).Equals("copilot.exe", StringComparison.OrdinalIgnoreCase))
            {
                if (executable is not null) throw new InvalidDataException("The Copilot archive contains multiple executables with the same name.");
                executable = relative;
            }
            entries.Add((entry, target, directory));
        }
        if (executable is null) throw new InvalidDataException("The Copilot executable is missing from the package.");
        // Complete validation precedes any extraction, including path traversal checks.
        Directory.CreateDirectory(destination);
        foreach (var item in entries)
        {
            cancellation.ThrowIfCancellationRequested();
            if (item.Directory) Directory.CreateDirectory(item.Target);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(item.Target)!);
                item.Entry.ExtractToFile(item.Target, overwrite: false);
            }
        }
        return executable;
    }

    internal static string SafeChild(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new InvalidDataException("Copilot package path is invalid.");
        var segments = relative.Replace('/', '\\').Split('\\');
        if (segments.Any(p => string.IsNullOrEmpty(p) || p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.')
            || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase)))
            throw new InvalidDataException("Copilot package path is invalid.");
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string result = Path.GetFullPath(Path.Combine(prefix, relative));
        if (!result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Copilot package path escapes its installation.");
        return result;
    }

    internal static IEnumerable<string> ExternalCandidates(string? path, string local, string home)
    {
        yield return Path.Combine(local, "Microsoft", "WinGet", "Links", "copilot.exe");
        yield return Path.Combine(home, ".local", "bin", "copilot.exe");
        foreach (var directory in (path ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string value = directory.Trim().Trim('"');
            if (Path.IsPathFullyQualified(value) && !value.Contains("copilot-shim", StringComparison.OrdinalIgnoreCase))
                yield return Path.Combine(value, "copilot.exe");
        }
    }

    private static bool IsUsableCandidate(string path) => Path.IsPathFullyQualified(path)
        && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && !path.Contains("copilot-shim", StringComparison.OrdinalIgnoreCase) && File.Exists(path);

    private static async Task<string?> ReadVersion(string executable, CancellationToken cancellation)
    {
        using var process = Start(executable, "--version");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var output = ReadLimited(process.StandardOutput, timeout.Token);
        var error = ReadLimited(process.StandardError, timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            string text = await output;
            await error;
            if (process.ExitCode != 0) return null;
            var match = Regex.Match(text, @"\b(?:GitHub\s+)?Copilot(?:\s+CLI)?\s+(?:version\s+)?v?(\d+\.\d+\.\d+)\b", RegexOptions.IgnoreCase);
            if (!match.Success) match = Regex.Match(text.Trim(), @"^v?(\d+\.\d+\.\d+)(?:\s|$)");
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return null; }
        finally
        {
            await StopOwned(process);
            try { await Task.WhenAll(output, error); } catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
        }
    }

    private static Process Start(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new IOException("Copilot could not be started.");
    }

    private static async Task StopOwned(Process process)
    {
        if (process.HasExited) return;
        try { process.StandardInput.Close(); } catch (IOException) { }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true); // Only the setup/version/login child created by this instance.
            await process.WaitForExitAsync();
        }
    }

    private static async Task<string> ReadLimited(StreamReader reader, CancellationToken token)
    {
        char[] buffer = new char[2048]; var text = new StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), token)) != 0)
        {
            if (text.Length + read > 32768) throw new InvalidDataException("Copilot returned too much diagnostic output.");
            text.Append(buffer, 0, read);
        }
        return text.ToString();
    }

    private static async Task ForwardLines(StreamReader reader, IProgress<string>? progress, CancellationToken token)
    {
        char[] buffer = new char[512]; int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), token)) != 0)
        {
            string value = Regex.Replace(new string(buffer, 0, read), @"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07]*(?:\x07|\x1B\\))", "");
            // Sign-in output goes only to the current UI, never to logs or persisted chat history.
            if (!string.IsNullOrWhiteSpace(value)) progress?.Report(value.Trim());
        }
    }

    private static async Task CopyBounded(Stream input, Stream output, long maximum, CancellationToken cancellation, Action<long>? progress = null)
    {
        byte[] buffer = new byte[81920]; long total = 0; int read;
        while (true)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            idle.CancelAfter(TimeSpan.FromMinutes(1));
            try { read = await input.ReadAsync(buffer, idle.Token); }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            { throw new IOException("The Copilot download stopped responding. Try setup again."); }
            if (read == 0) break;
            total += read;
            if (total > maximum) throw new InvalidDataException("The Copilot response exceeded its allowed size.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
            progress?.Invoke(total);
        }
    }

    private static void RemoveOwnedDirectory(string root, string directory)
    {
        string target = Path.GetFullPath(directory);
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || target.Length <= prefix.Length)
            throw new InvalidDataException("Refusing to clean a directory outside the Copilot runtime.");
        try { if (Directory.Exists(target)) Directory.Delete(target, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
