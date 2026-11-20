using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VsLike.Core.Build;
using VsLike.Core.Documents;
using VsLike.Projects.Build;

namespace VsLike.App.Output;

/// <summary>
/// Ausgabefenster für Build, Start und Tests. Zeilen kommen von Hintergrund-Threads und werden gebündelt auf dem
/// UI-Thread angehängt; ältere Zeilen fallen ab <see cref="MaxLines"/> heraus.
/// </summary>
public sealed partial class OutputViewModel : ObservableObject
{
    public const int MaxLines = 20_000;

    private readonly IDocumentNavigator _navigator;
    private readonly object _gate = new();
    private readonly List<OutputLine> _pending = [];
    private bool _flushScheduled;

    [ObservableProperty]
    private string _title = string.Empty;

    public OutputViewModel(IDocumentNavigator navigator)
    {
        _navigator = navigator;
    }

    /// <summary>Raised on the UI thread after new lines were appended.</summary>
    public event EventHandler? LinesAppended;

    public ObservableCollection<OutputEntry> Lines { get; } = [];

    /// <summary>Starts a new section: clears the window and shows a title line.</summary>
    public void Begin(string title)
    {
        lock (_gate)
        {
            _pending.Clear();
        }

        Lines.Clear();
        Title = title;
        Lines.Add(new OutputEntry(title, false, null));
    }

    public void Clear()
    {
        lock (_gate)
        {
            _pending.Clear();
        }

        Lines.Clear();
    }

    /// <summary>Thread-safe; lines become visible on the next dispatcher turn.</summary>
    public void Append(OutputLine line)
    {
        lock (_gate)
        {
            _pending.Add(line);
            if (_flushScheduled)
            {
                return;
            }

            _flushScheduled = true;
        }

        Dispatcher.UIThread.Post(Flush, DispatcherPriority.Background);
    }

    public void AppendText(string text, bool isError = false) => Append(new OutputLine(text, isError));

    /// <summary>Moves pending lines into <see cref="Lines"/> (UI thread).</summary>
    public void Flush()
    {
        List<OutputLine> batch;
        lock (_gate)
        {
            batch = [.. _pending];
            _pending.Clear();
            _flushScheduled = false;
        }

        if (batch.Count == 0)
        {
            return;
        }

        foreach (var line in batch)
        {
            BuildOutputParser.TryParse(line.Text, out var diagnostic);
            Lines.Add(new OutputEntry(line.Text, line.IsError, diagnostic));
        }

        var overflow = Lines.Count - MaxLines;
        for (var i = 0; i < overflow; i++)
        {
            Lines.RemoveAt(0);
        }

        LinesAppended?.Invoke(this, EventArgs.Empty);
    }

    public Task OpenAsync(OutputEntry entry)
        => entry.Diagnostic is { FilePath: { } path } diagnostic ? _navigator.OpenAsync(path, diagnostic.StartLine, diagnostic.StartColumn) : Task.CompletedTask;
}
