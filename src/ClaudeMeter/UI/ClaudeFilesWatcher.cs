using ClaudeMeter.Core;

namespace ClaudeMeter.UI;

/// <summary>Raises <see cref="Changed"/> on the UI thread ~2 s after Claude Code's login files stop changing.</summary>
internal sealed class ClaudeFilesWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 2000 };
    private readonly SynchronizationContext _ui = SynchronizationContext.Current
        ?? throw new InvalidOperationException("Create the watcher on the UI thread.");

    public event EventHandler? Changed;

    public ClaudeFilesWatcher(ClaudePaths paths)
    {
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Changed?.Invoke(this, EventArgs.Empty);
        };

        foreach (var file in new[] { paths.Credentials, paths.GlobalConfig })
        {
            var folder = Path.GetDirectoryName(file)!;
            if (!Directory.Exists(folder))
                continue;
            var watcher = new FileSystemWatcher(folder, Path.GetFileName(file))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            watcher.Changed += OnEvent;
            watcher.Created += OnEvent;
            watcher.Renamed += OnEvent;
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
    }

    private void OnEvent(object sender, FileSystemEventArgs e) => _ui.Post(_ =>
    {
        _debounce.Stop();
        _debounce.Start();
    }, null);

    public void Dispose()
    {
        foreach (var watcher in _watchers)
            watcher.Dispose();
        _debounce.Dispose();
    }
}
