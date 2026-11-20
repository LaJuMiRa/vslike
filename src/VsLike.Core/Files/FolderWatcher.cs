namespace VsLike.Core.Files;

/// <summary>
/// Beobachtet einen Ordner rekursiv und meldet Änderungen gebündelt (Debounce), damit z. B. ein Build
/// mit tausenden Dateiänderungen nur ein Ereignis auslöst. Ignorierte Ordner (bin/obj/.git) lösen nichts aus.
/// </summary>
public sealed class FolderWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _timer;
    private readonly TimeSpan _delay;
    private readonly object _gate = new();
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);

    public FolderWatcher(string rootPath, TimeSpan? delay = null)
    {
        RootPath = Path.GetFullPath(rootPath);
        _delay = delay ?? TimeSpan.FromMilliseconds(300);
        _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(RootPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Created += (_, e) => Queue(e.FullPath);
        _watcher.Deleted += (_, e) => Queue(e.FullPath);
        _watcher.Changed += (_, e) => Queue(e.FullPath);
        _watcher.Renamed += (_, e) =>
        {
            Queue(e.OldFullPath);
            Queue(e.FullPath);
        };
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Raised on a thread-pool thread with the distinct changed paths.</summary>
    public event EventHandler<IReadOnlyList<string>>? Changed;

    public string RootPath { get; }

    public void Dispose()
    {
        _watcher.Dispose();
        _timer.Dispose();
    }

    private void Queue(string path)
    {
        if (FileTreeBuilder.IsIgnored(RootPath, path))
        {
            return;
        }

        lock (_gate)
        {
            _pending.Add(path);
            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    private void Flush()
    {
        string[] paths;
        lock (_gate)
        {
            paths = [.. _pending];
            _pending.Clear();
        }

        if (paths.Length > 0)
        {
            Changed?.Invoke(this, paths);
        }
    }
}
