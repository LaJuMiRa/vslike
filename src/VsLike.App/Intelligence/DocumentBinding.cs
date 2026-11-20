using Avalonia.Threading;
using AvaloniaEdit.Document;
using VsLike.App.Editor;
using VsLike.Core.Code;
using VsLike.Editor;

namespace VsLike.App.Intelligence;

/// <summary>
/// Verbindet ein offenes C#-Dokument mit dem <see cref="ICodeWorkspace"/>: Text synchronisieren, semantisch
/// einfärben, Diagnosen anzeigen und Hover beantworten. Alle Roslyn-Aufrufe laufen im Hintergrund.
/// </summary>
internal sealed class DocumentBinding : IDisposable
{
    private const int ClassifyMargin = 4000;
    private static readonly TimeSpan SyncDelay = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan ClassifyDelay = TimeSpan.FromMilliseconds(200);

    private readonly ICodeWorkspace _workspace;
    private readonly EditorDocumentViewModel _document;
    private readonly DispatcherTimer _syncTimer;
    private readonly DispatcherTimer _classifyTimer;
    private ITextSourceVersion? _pushedVersion;
    private EditorSession? _session;
    private CancellationTokenSource? _classifyCancellation;
    private CancellationTokenSource? _hoverCancellation;
    private IReadOnlyList<DiagnosticInfo> _diagnostics = [];

    public DocumentBinding(ICodeWorkspace workspace, EditorDocumentViewModel document)
    {
        _workspace = workspace;
        _document = document;
        _syncTimer = new DispatcherTimer { Interval = SyncDelay };
        _syncTimer.Tick += (_, _) =>
        {
            _syncTimer.Stop();
            Sync();
            ScheduleClassification();
        };
        _classifyTimer = new DispatcherTimer { Interval = ClassifyDelay };
        _classifyTimer.Tick += async (_, _) =>
        {
            _classifyTimer.Stop();
            await ClassifyAsync().ConfigureAwait(true);
        };
        document.TextDocument.TextChanged += OnTextChanged;
        document.SessionChanged += OnSessionChanged;
        OnSessionChanged(this, EventArgs.Empty);
    }

    public string? FilePath => _document.FilePath;

    /// <summary>Pushes the current text (also unsaved) to the workspace, e.g. after a solution (re)load.</summary>
    public void ForceSync()
    {
        _pushedVersion = null;
        Sync();
        ScheduleClassification();
    }

    public void ApplyDiagnostics(IReadOnlyList<DiagnosticInfo> diagnostics)
    {
        _diagnostics = diagnostics;
        _session?.SetDiagnostics(diagnostics);
    }

    public void Dispose()
    {
        _syncTimer.Stop();
        _classifyTimer.Stop();
        _classifyCancellation?.Cancel();
        _hoverCancellation?.Cancel();
        _document.TextDocument.TextChanged -= OnTextChanged;
        _document.SessionChanged -= OnSessionChanged;
        DetachSession();
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        _syncTimer.Stop();
        _syncTimer.Start();
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        DetachSession();
        _session = _document.Session;
        if (_session is null)
        {
            return;
        }

        _session.HoverRequested += OnHoverRequested;
        _session.ViewportChanged += OnViewportChanged;
        _session.SetDiagnostics(_diagnostics);
        if (_document.IsDirty || (_document.FilePath is { } path && !_workspace.ContainsDocument(path)))
        {
            ForceSync();
        }
        else
        {
            ScheduleClassification();
        }
    }

    private void DetachSession()
    {
        if (_session is not null)
        {
            _session.HoverRequested -= OnHoverRequested;
            _session.ViewportChanged -= OnViewportChanged;
            _session = null;
        }
    }

    private void OnViewportChanged(object? sender, EventArgs e) => ScheduleClassification();

    private void Sync()
    {
        var version = _document.TextDocument.Version;
        if (_document.FilePath is not { } path || (version is not null && _pushedVersion is not null && version.CompareAge(_pushedVersion) == 0))
        {
            return;
        }

        _workspace.UpdateDocumentText(path, _document.TextDocument.Text);
        _pushedVersion = version;
    }

    private void ScheduleClassification()
    {
        _classifyTimer.Stop();
        _classifyTimer.Start();
    }

    private async Task ClassifyAsync()
    {
        if (_session is not { } session || _document.FilePath is not { } path)
        {
            return;
        }

        Sync();
        _classifyCancellation?.Cancel();
        var cancellation = _classifyCancellation = new CancellationTokenSource();
        var version = _document.TextDocument.Version;
        var (start, end) = session.VisibleRange;
        var from = Math.Max(0, start - ClassifyMargin);
        var to = Math.Min(_document.TextDocument.TextLength, end + ClassifyMargin);
        try
        {
            var spans = await Task.Run(() => _workspace.ClassifyAsync(path, from, to - from, cancellation.Token), cancellation.Token).ConfigureAwait(true);
            if (!cancellation.IsCancellationRequested && ReferenceEquals(session, _session))
            {
                session.ApplyClassifications(spans, version);
            }
        }
        catch (OperationCanceledException)
        {
            // Neuere Anfrage unterwegs.
        }
    }

    private async void OnHoverRequested(object? sender, int offset)
    {
        if (_session is not { } session || _document.FilePath is not { } path)
        {
            return;
        }

        Sync();
        _hoverCancellation?.Cancel();
        var cancellation = _hoverCancellation = new CancellationTokenSource();
        try
        {
            var info = await Task.Run(() => _workspace.GetQuickInfoAsync(path, offset, cancellation.Token), cancellation.Token).ConfigureAwait(true);
            if (!cancellation.IsCancellationRequested && ReferenceEquals(session, _session))
            {
                session.ShowQuickInfo(info, offset);
            }
        }
        catch (OperationCanceledException)
        {
            // Maus wurde weiterbewegt.
        }
    }
}
