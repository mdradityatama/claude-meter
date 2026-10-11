using System.Text.Json;

namespace ClaudeMeter.Core;

/// <summary>Non-secret preferences in <c>settings.json</c>. "Start with Windows" stays in the registry.</summary>
internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _path;

    private AppSettings(string path, bool keepInactiveLive, bool showResetDate)
    {
        _path = path;
        KeepInactiveLive = keepInactiveLive;
        ShowResetDate = showResetDate;
    }

    public bool KeepInactiveLive { get; private set; }

    /// <summary>The panel shows reset dates instead of the time left.</summary>
    public bool ShowResetDate { get; private set; }

    public static AppSettings Load(string path)
    {
        try
        {
            var file = LiveFiles.ReadOrNull(path) is { } bytes ? JsonSerializer.Deserialize<SettingsFile>(bytes, Options) : null;
            return new AppSettings(path, file?.KeepInactiveLive ?? true, file?.ShowResetDate ?? false);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings(path, keepInactiveLive: true, showResetDate: false);
        }
    }

    /// <returns>False if the save failed; the value still applies for this session.</returns>
    public bool SetKeepInactiveLive(bool value)
    {
        KeepInactiveLive = value;
        return Save();
    }

    /// <returns>False if the save failed; the value still applies for this session.</returns>
    public bool SetShowResetDate(bool value)
    {
        ShowResetDate = value;
        return Save();
    }

    private bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            AtomicFile.Write(_path, JsonSerializer.SerializeToUtf8Bytes(new SettingsFile(KeepInactiveLive, ShowResetDate), Options));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed record SettingsFile(bool? KeepInactiveLive, bool? ShowResetDate);
}
