using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace AIHub;

internal static class CopilotRuntimeChecks
{
    internal static void Run(string folder, List<string> lines)
    {
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            lines.Add("PASS " + name);
        }
        bool Reject(Action action)
        {
            try { action(); return false; }
            catch (InvalidDataException) { return true; }
        }
        string root = Path.Combine(folder, "copilot-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string Metadata(bool draft = false, bool prerelease = false, string version = "v1.0.114", string? url = null,
            string digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", long size = 100, int count = 1) =>
            JsonSerializer.Serialize(new
            {
                draft, prerelease, tag_name = version,
                assets = Enumerable.Range(0, count).Select(_ => new
                {
                    name = CopilotRuntime.AssetName, digest, size,
                    browser_download_url = url ?? $"https://github.com/github/copilot-cli/releases/download/{version}/{CopilotRuntime.AssetName}"
                })
            });
        var release = CopilotRuntime.ParseRelease(Metadata());
        Check(release.Version == "1.0.114" && release.Download.Host == "github.com", "Copilot setup selects the official stable native Windows x64 asset");
        Check(Reject(() => CopilotRuntime.ParseRelease(Metadata(draft: true)))
            && Reject(() => CopilotRuntime.ParseRelease(Metadata(prerelease: true)))
            && Reject(() => CopilotRuntime.ParseRelease(Metadata(version: "v1.0.114-preview"))),
            "Copilot setup rejects unpublished, preview and malformed release versions");
        Check(Reject(() => CopilotRuntime.ParseRelease(Metadata(url: "https://example.invalid/copilot.zip")))
            && Reject(() => CopilotRuntime.ParseRelease(Metadata(count: 0)))
            && Reject(() => CopilotRuntime.ParseRelease(Metadata(count: 2))),
            "Copilot setup rejects foreign download origins and missing or ambiguous runtime assets");
        Check(Reject(() => CopilotRuntime.ParseRelease(Metadata(digest: "sha256:bad")))
            && Reject(() => CopilotRuntime.ParseRelease(Metadata(size: 0)))
            && Reject(() => CopilotRuntime.ParseRelease(Metadata(size: CopilotRuntime.MaxDownloadBytes + 1))),
            "Copilot setup requires a valid SHA-256 checksum and bounded nonempty download");
        string archive = Path.Combine(root, "verified.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            Write(zip, "package/copilot.exe", "disposable runtime fixture");
            Write(zip, "package/licenses/NOTICE.txt", "retained notice");
        }
        string hash;
        using (var file = File.OpenRead(archive)) hash = Convert.ToHexString(SHA256.HashData(file));
        var fixture = release with { Sha256 = hash, Size = new FileInfo(archive).Length };
        CopilotRuntime.VerifyArchive(archive, fixture);
        Check(Reject(() => CopilotRuntime.VerifyArchive(archive, fixture with { Size = fixture.Size + 1 }))
            && Reject(() => CopilotRuntime.VerifyArchive(archive, fixture with { Sha256 = new string('0', 64) })),
            "Copilot setup verifies both downloaded size and content hash before extraction");
        string payload = Path.Combine(root, "valid-payload");
        string relative = CopilotRuntime.ExtractArchive(archive, payload);
        Check(relative.Replace('\\', '/') == "package/copilot.exe"
            && File.ReadAllText(Path.Combine(payload, "package", "licenses", "NOTICE.txt")) == "retained notice",
            "Copilot setup preserves the native runtime directory layout and license files");
        Check(Reject(() => CopilotRuntime.ExtractArchive(archive, payload)), "Copilot extraction cannot overwrite an existing installation");
        foreach (string unsafeName in new[] { "../escape.txt", "C:/outside.txt", "/outside.txt", "package/file:stream", "package/../escape", "package/NUL.txt", "package/trailing. " })
        {
            string unsafeArchive = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
            using (var zip = ZipFile.Open(unsafeArchive, ZipArchiveMode.Create))
            {
                Write(zip, "copilot.exe", "fixture");
                Write(zip, unsafeName, "must not extract");
            }
            string unsafePayload = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Check(Reject(() => CopilotRuntime.ExtractArchive(unsafeArchive, unsafePayload)) && !Directory.Exists(unsafePayload),
                "Copilot archive validation rejects unsafe paths before writing: " + unsafeName);
        }
        string duplicate = Path.Combine(root, "duplicate.zip");
        using (var zip = ZipFile.Open(duplicate, ZipArchiveMode.Create))
        {
            Write(zip, "copilot.exe", "fixture");
            Write(zip, "COPILOT.EXE", "duplicate");
        }
        Check(Reject(() => CopilotRuntime.ExtractArchive(duplicate, Path.Combine(root, "duplicate-payload"))),
            "Copilot archive validation rejects Windows case-insensitive duplicate files");
        string symlink = Path.Combine(root, "symlink.zip");
        using (var zip = ZipFile.Open(symlink, ZipArchiveMode.Create))
        {
            Write(zip, "copilot.exe", "fixture");
            var entry = zip.CreateEntry("linked"); entry.ExternalAttributes = unchecked((int)0xA1FF0000);
        }
        Check(Reject(() => CopilotRuntime.ExtractArchive(symlink, Path.Combine(root, "linked-payload"))),
            "Copilot archive validation rejects symbolic links");
        var candidates = CopilotRuntime.ExternalCandidates(@"C:\VSCode\copilot-shim;C:\Tools;relative", @"C:\Local", @"C:\User").ToArray();
        Check(!candidates.Any(p => p.Contains("copilot-shim", StringComparison.OrdinalIgnoreCase))
            && candidates.Contains(@"C:\Tools\copilot.exe") && candidates.All(Path.IsPathFullyQualified),
            "Copilot discovery ignores VS Code launcher shims and searches real native executables");
        Check(Reject(() => CopilotRuntime.SafeChild(root, "../outside/copilot.exe"))
            && Reject(() => CopilotRuntime.SafeChild(root, @"C:\outside\copilot.exe")),
            "A corrupted active-runtime pointer cannot escape the managed installation directory");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        bool cancelled = false; string cancelledPayload = Path.Combine(root, "cancelled-payload");
        try { CopilotRuntime.ExtractArchive(archive, cancelledPayload, cancel.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && !Directory.Exists(cancelledPayload), "Cancelled Copilot setup stops before extracting its runtime");
    }

    private static void Write(ZipArchive archive, string name, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open()); writer.Write(text);
    }
}
