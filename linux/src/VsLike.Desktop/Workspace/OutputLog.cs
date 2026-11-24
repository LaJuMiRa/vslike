using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>
/// The Output panel: build, test, engine and debugger output by channel. Appends are batched per dispatcher turn so
/// that fast builds do not redraw the panel for every line.
/// </summary>
public sealed partial class OutputLog : ObservableObject
{
    public const int MaxLines = 20_000;

    private readonly List<Line> _pending = [];
    private int _nextId;
    private bool _flushScheduled;

    public enum Channel
    {
        Build,
        Tests,
        Engine,
        Debugger,
    }

    [ObservableProperty]
    public partial IReadOnlyList<Line> Lines { get; private set; } = [];

    [ObservableProperty]
    public partial Channel SelectedChannel { get; set; } = Channel.Build;

    public static string Title(Channel channel) => channel switch
    {
        Channel.Build => L.T("Build"),
        Channel.Tests => L.T("Tests"),
        Channel.Engine => L.T("Analysis"),
        _ => L.T("Debugger"),
    };

    public IReadOnlyList<Line> LinesIn(Channel channel) => [.. Lines.Where(l => l.Channel == channel)];

    public void Append(string text, Channel channel, bool isError = false)
    {
        foreach (var part in text.Split('\n'))
        {
            _nextId++;
            _pending.Add(new Line(_nextId, channel, part, isError));
        }

        if (_flushScheduled)
        {
            return;
        }

        _flushScheduled = true;
        Dispatcher.UIThread.Post(Flush);
    }

    public void Clear(Channel channel)
    {
        Lines = [.. Lines.Where(l => l.Channel != channel)];
    }

    public void Flush()
    {
        _flushScheduled = false;
        if (_pending.Count == 0)
        {
            return;
        }

        var combined = Lines.Concat(_pending);
        var total = Lines.Count + _pending.Count;
        Lines = [.. total > MaxLines ? combined.Skip(total - MaxLines) : combined];
        _pending.Clear();
    }

    /// <summary>A thread-safe sink for process output.</summary>
    public Action<OutputLine> Sink(Channel channel) =>
        line => Dispatcher.UIThread.Post(() => Append(line.Text, channel, line.IsError));

    public sealed record Line(int Id, Channel Channel, string Text, bool IsError);
}
