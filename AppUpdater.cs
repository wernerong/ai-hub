using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIHub;

internal sealed record UpdateRelease(Version Version, Uri Download, string Sha256, long Size)
{
    internal const string Repository = "wernerong/ai-hub";
    internal const string ReleasesUrl = "https://github.com/" + Repository + "/releases";
    internal const string LatestApi = "https://api.github.com/repos/" + Repository + "/releases/latest";
    internal const long MaxPackageBytes = 200L * 1024 * 1024;

    internal static UpdateRelease? Parse(string json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$")
            || !Version.TryParse(tag[1..], out var version))
            throw new InvalidDataException("The release version is invalid.");
        if (version <= current) return null;
        string name = $"AIHub-v{version}-windows-x64.zip";
        var assets = root.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == name).ToArray();
        if (assets.Length != 1) throw new InvalidDataException("The release package is missing or ambiguous.");
        var asset = assets[0];
        string expectedUrl = ReleasesUrl + $"/download/{tag}/{name}";
        if (asset.GetProperty("browser_download_url").GetString() != expectedUrl)
            throw new InvalidDataException("The package is outside the AI Hub release channel.");
        string digest = asset.GetProperty("digest").GetString() ?? "";
        long size = asset.GetProperty("size").GetInt64();
        if (!Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$") || size is <= 0 or > MaxPackageBytes)
            throw new InvalidDataException("The release package has no valid checksum or size.");
        return new(version, new Uri(expectedUrl), digest[7..].ToLowerInvariant(), size);
    }
}

internal sealed class UpdatePreferences
{
    public bool Automatic { get; set; } = true;
    internal static UpdatePreferences Load(string path)
    {
        try { return JsonSerializer.Deserialize<UpdatePreferences>(File.ReadAllText(path)) ?? new(); }
        catch { return new(); }
    }
}

internal sealed class AppUpdater : IDisposable
{
    private readonly string stateDirectory, executable, preferencesPath;
    private readonly Version currentVersion;
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly Func<DateTimeOffset> clock;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? request;
    private DateTimeOffset lastCheck = DateTimeOffset.MinValue;
    private UpdatePreferences preferences;
    private string? stagedPlan;
    private bool disposed;
    internal bool Automatic => preferences.Automatic;
    internal bool Checking { get; private set; }
    internal bool Ready => stagedPlan is not null;
    internal string Status { get; private set; } = "Updates come from the public GitHub release channel.";
    internal event Action? Changed;

    internal AppUpdater(string stateDirectory, string executable, Version? currentVersion = null,
        HttpClient? client = null, Func<DateTimeOffset>? clock = null)
    {
        this.stateDirectory = stateDirectory;
        this.executable = executable;
        this.currentVersion = currentVersion ?? Version.Parse(AppInfo.Version);
        preferencesPath = Path.Combine(stateDirectory, "update-settings.json");
        preferences = UpdatePreferences.Load(preferencesPath);
        this.client = client ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        ownsClient = client is null;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    internal void SetAutomatic(bool enabled)
    {
        Directory.CreateDirectory(stateDirectory);
        var proposed = new UpdatePreferences { Automatic = enabled };
        File.WriteAllText(preferencesPath + ".tmp", JsonSerializer.Serialize(proposed));
        File.Move(preferencesPath + ".tmp", preferencesPath, true);
        preferences = proposed;
        if (!enabled) request?.Cancel();
        Changed?.Invoke();
    }

    internal async Task CheckAsync(bool manual = false)
    {
        if (disposed || Checking || Ready || (!manual && !Automatic)) return;
        if (clock() - lastCheck < (manual ? TimeSpan.FromMinutes(1) : TimeSpan.FromHours(6))) return;
        lastCheck = clock();
        Checking = true;
        request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        request.CancelAfter(TimeSpan.FromMinutes(5));
        var cancellation = request.Token;
        string? stage = null;
        try
        {
            SetStatus("Checking for updates…");
            using var apiRequest = Request(UpdateRelease.LatestApi);
            using var apiDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            apiDeadline.CancelAfter(TimeSpan.FromSeconds(30));
            using var response = await client.SendAsync(apiRequest, HttpCompletionOption.ResponseHeadersRead, apiDeadline.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) { SetStatus("The public release channel is not available yet."); return; }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 2 * 1024 * 1024) throw new InvalidDataException("Release metadata is too large.");
            using var metadata = new MemoryStream();
            await CopyLimited(await response.Content.ReadAsStreamAsync(apiDeadline.Token), metadata, 2 * 1024 * 1024, apiDeadline.Token);
            var release = UpdateRelease.Parse(System.Text.Encoding.UTF8.GetString(metadata.ToArray()), currentVersion);
            if (release is null) { SetStatus($"AI Hub v{currentVersion} is up to date."); return; }
            SetStatus($"Downloading AI Hub v{release.Version}…");
            stage = Path.Combine(stateDirectory, "updates", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            string archive = Path.Combine(stage, "package.zip");
            using var downloadRequest = Request(release.Download.AbsoluteUri);
            using (var download = await client.SendAsync(downloadRequest, HttpCompletionOption.ResponseHeadersRead, cancellation))
            {
                download.EnsureSuccessStatusCode();
                if (download.Content.Headers.ContentLength is long length && length != release.Size)
                    throw new InvalidDataException("The downloaded package size is incorrect.");
                await using var output = new FileStream(archive, FileMode.CreateNew);
                await CopyLimited(await download.Content.ReadAsStreamAsync(cancellation), output, release.Size, cancellation);
            }
            if (new FileInfo(archive).Length != release.Size) throw new InvalidDataException("The downloaded package is incomplete.");
            await Task.Run(() =>
            {
                UpdateInstaller.VerifyHash(archive, release.Sha256);
                UpdateInstaller.Extract(archive, Path.Combine(stage, "payload"), release.Version);
            }, cancellation);
            cancellation.ThrowIfCancellationRequested();
            using var parent = Process.GetCurrentProcess();
            var plan = new UpdatePlan(Path.GetDirectoryName(executable)!, release.Version.ToString(), release.Sha256,
                UpdateInstaller.Hash(executable), parent.Id, parent.StartTime.ToUniversalTime().Ticks, false);
            string planPath = Path.Combine(stage, "update-plan.json");
            File.WriteAllText(planPath, JsonSerializer.Serialize(plan));
            stagedPlan = planPath;
            SetStatus($"AI Hub v{release.Version} is ready. It installs when you close AI Hub, or restart now.");
        }
        catch (OperationCanceledException) { if (!disposed) SetStatus("Update check cancelled. The current version is unchanged."); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException or FormatException
            or InvalidOperationException or KeyNotFoundException or UnauthorizedAccessException)
        { SetStatus("Could not verify an update. The current version is unchanged; check your connection or try again later."); }
        finally
        {
            if (stage is not null && stagedPlan is null) UpdateInstaller.RemoveStage(stage, Path.Combine(stateDirectory, "updates"));
            Checking = false;
            request.Dispose(); request = null;
            if (!disposed) Changed?.Invoke();
        }
    }

    internal void ApplyOnClose(bool restart)
    {
        if (stagedPlan is null || (!Automatic && !restart)) return;
        try
        {
            var plan = JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(stagedPlan))! with { Relaunch = restart };
            File.WriteAllText(stagedPlan, JsonSerializer.Serialize(plan));
            string helper = Path.Combine(Path.GetDirectoryName(stagedPlan)!, "AIHub.Update.exe");
            File.Copy(executable, helper, true);
            var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(stagedPlan);
            using var process = Process.Start(start) ?? throw new IOException("The update helper did not start.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { UpdateInstaller.WriteResult(stateDirectory, "The update could not start. Run AI Hub and check for updates again."); }
    }

    private void SetStatus(string status) { Status = status; if (!disposed) Changed?.Invoke(); }
    private static HttpRequestMessage Request(string url)
    {
        var message = new HttpRequestMessage(HttpMethod.Get, url);
        message.Headers.UserAgent.ParseAdd("AIHub/" + AppInfo.Version);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        message.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return message;
    }
    private static async Task CopyLimited(Stream input, Stream output, long limit, CancellationToken token)
    {
        using (input)
        {
            byte[] buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, token)) > 0)
            {
                total += read;
                if (total > limit) throw new InvalidDataException("The download exceeds its expected size.");
                await output.WriteAsync(buffer.AsMemory(0, read), token);
            }
        }
    }
    public void Dispose()
    {
        disposed = true; lifetime.Cancel(); lifetime.Dispose();
        if (ownsClient) client.Dispose();
    }
}
