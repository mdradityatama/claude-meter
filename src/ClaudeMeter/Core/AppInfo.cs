using System.Reflection;

namespace ClaudeMeter.Core;

internal static class AppInfo
{
    /// <summary>The release version (e.g. 1.4.0), without the build's commit suffix.</summary>
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
}
