using System.Reflection;

namespace AIHub;

internal static class AppInfo
{
    internal static string Version { get; } = typeof(AppInfo).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
        .InformationalVersion.Split('+')[0];

    internal static string DisplayName => $"AI Hub v{Version}";

    internal static Icon LoadIcon()
    {
        using var stream = typeof(AppInfo).Assembly.GetManifestResourceStream("AIHub.Assets.AIHub.ico")
            ?? throw new InvalidOperationException("The app icon is missing.");
        using var icon = new Icon(stream, new Size(256, 256));
        return (Icon)icon.Clone();
    }
}
