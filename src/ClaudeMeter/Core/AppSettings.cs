using System.Text.Json;

namespace ClaudeMeter.Core;

/// <summary>Non-secret preferences in <c>settings.json</c>. "Start with Windows" stays in the registry.</summary>
internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _path;

    private AppSettings(string path, bool keepInactiveLive)
    {
        _path = path;
        KeepInactiveLive = keepInactiveLive;
    }

    public bool KeepInactiveLive { get; private set; }

    public static AppSettings Load(string path)
    {
        try
        {
            var file = LiveFiles.ReadOrNull(path) is { } bytes ? JsonSerializer.Deserialize<SettingsFile>(bytes, Options) : null;
            return new AppSettings(path, file?.KeepInactiveLive ?? true);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new AppSettings(path, keepInactiveLive: true);
        }
    }

    /// <returns>False if the save failed; the value still applies for this session.</returns>
    public bool SetKeepInactiveLive(bool value)
    {
        KeepInactiveLive = value;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            AtomicFile.Write(_path, JsonSerializer.SerializeToUtf8Bytes(new SettingsFile(value), Options));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed record SettingsFile(bool? KeepInactiveLive);
}
