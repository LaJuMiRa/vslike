using Avalonia.Threading;
using AvaloniaEdit.Document;
using VsLike.App.Editor;
using VsLike.Core.Code;
using VsLike.Editor;
using VsLike.Editor.Snippets;

namespace VsLike.App.Intelligence;

/// <summary>
/// Verbindet ein offenes C#-Dokument mit Workspace und Sprachfunktionen: Text synchronisieren, Diagnosen,
/// semantische Farben, Hover, Completion (inkl. Snippets), Parameterhilfe und ⌘-Klick.
/// Alle Roslyn-Aufrufe laufen im Hintergrund und werden verworfen, wenn sich der Text inzwischen geändert hat.
/// </summary>
internal sealed class DocumentBinding : IDisposable
{
    private const int ClassifyMargin = 4000;
    private static readonly TimeSpan SyncDelay = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan ClassifyDelay = TimeSpan.FromMilliseconds(200);

    private readonly ICodeWorkspace _workspace;
    private readonly ILanguageFeatures _features;
    private readonly WorkspaceEditApplier _applier;
    private readonly EditorDocumentViewModel _document;
    private readonly DispatcherTimer _syncTimer;
    private readonly DispatcherTimer _classifyTimer;
    private ITextSourceVersion? _pushedVersion;
    private EditorSession? _session;
    private CancellationTokenSource? _classifyCancellation;
    private CancellationTokenSource? _hoverCancellation;
    private CancellationTokenSource? _completionCancellation;
    private CancellationTokenSource? _signatureCancellation;
    private IReadOnlyList<DiagnosticInfo> _diagnostics = [];

    public DocumentBinding(ICodeWorkspace workspace, ILanguageFeatures features, WorkspaceEditApplier applier, EditorDocumentViewModel document)
    {
        _workspace = workspace;
        _features = features;
        _applier = applier;
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

    /// <summary>Raised when the user ⌘-clicks a symbol (handled by the workbench navigation).</summary>
    public event EventHandler<int>? DefinitionRequested;

    public string? FilePath => _document.FilePath;

    public EditorDocumentViewModel Document => _document;

    public void ForceSync()
    {
        _pushedVersion = null;
        Sync();
        ScheduleClassification();
    }

    /// <summary>Pushes the current editor text to the workspace if it changed since the last push.</summary>
    public void Sync()
    {
        var version = _document.TextDocument.Version;
        if (_document.FilePath is not { } path || (version is not null && _pushedVersion is not null && version.CompareAge(_pushedVersion) == 0))
        {
            return;
        }

        _workspace.UpdateDocumentText(path, _document.TextDocument.Text);
        _pushedVersion = version;
    }

    public void ApplyDiagnostics(IReadOnlyList<DiagnosticInfo> diagnostics)
    {
        _diagnostics = diagnostics;
        _session?.SetDiagnostics(diagnostics);
    }

    /// <summary>Opens the completion list at the caret (also used by ⌃Space).</summary>
    public async Task TriggerCompletionAsync()
    {
        if (_session is not { } session || _document.FilePath is not { } path)
        {
            return;
        }

        Sync();
        _completionCancellation?.Cancel();
        var cancellation = _completionCancellation = new CancellationTokenSource();
        var version = _document.TextDocument.Version;
        var caret = session.Editor.CaretOffset;
        CompletionList? list;
        try
        {
            list = await Task.Run(() => _features.GetCompletionAsync(path, caret, cancellation.Token), cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (list is null || cancellation.IsCancellationRequested || !ReferenceEquals(session, _session) || !IsSameOrOnlyAppended(version, list))
        {
            return;
        }

        var memberAccess = list.SpanStart > 0 && _document.TextDocument.GetCharAt(list.SpanStart - 1) == '.';
        session.Completion.Show(
            list,
            memberAccess ? [] : SnippetLibrary.CSharp,
            (entry, start, length) => CommitAsync(list, entry, start, length),
            (snippet, start, length) => session.InsertSnippetAsync(snippet, start, length),
            entry => _features.GetCompletionDescriptionAsync(path, entry, CancellationToken.None));
    }

    public async Task ShowSignatureHelpAsync(bool closeIfNone = true)
    {
        if (_session is not { } session || _document.FilePath is not { } path)
        {
            return;
        }

        Sync();
        _signatureCancellation?.Cancel();
        var cancellation = _signatureCancellation = new CancellationTokenSource();
        var caret = session.Editor.CaretOffset;
        try
        {
            var help = await Task.Run(() => _features.GetSignatureHelpAsync(path, caret, cancellation.Token), cancellation.Token).ConfigureAwait(true);
            if (cancellation.IsCancellationRequested || !ReferenceEquals(session, _session))
            {
                return;
            }

            if (help is not null)
            {
                session.SignatureHelp.Show(help);
            }
            else if (closeIfNone)
            {
                session.SignatureHelp.Close();
            }
        }
        catch (OperationCanceledException)
        {
            // Neuere Anfrage unterwegs.
        }
    }

    public void Dispose()
    {
        _syncTimer.Stop();
        _classifyTimer.Stop();
        _classifyCancellation?.Cancel();
        _hoverCancellation?.Cancel();
        _completionCancellation?.Cancel();
        _signatureCancellation?.Cancel();
        _document.TextDocument.TextChanged -= OnTextChanged;
        _document.SessionChanged -= OnSessionChanged;
        DetachSession();
    }

    private bool IsSameOrOnlyAppended(ITextSourceVersion? requested, CompletionList list)
        => requested is null || _document.TextDocument.Version is not { } current || requested.CompareAge(current) == 0 || _session?.Editor.CaretOffset >= list.SpanStart;

    private async Task CommitAsync(CompletionList list, CompletionEntry entry, int segmentStart, int segmentLength)
    {
        if (_document.FilePath is not { } path)
        {
            return;
        }

        var edit = await _features.ResolveCompletionAsync(path, entry, CancellationToken.None).ConfigureAwait(true);
        var file = edit.Files.FirstOrDefault(f => f.FilePath == path);
        if (file is null)
        {
            return;
        }

        var (edits, caret) = CompletionEditMapper.Map(file.Edits, edit.CaretOffset, list.SpanStart, list.SpanLength, segmentStart, segmentLength);
        await _applier.ApplyAsync(new WorkspaceEdit([new FileEdit(path, edits)], path, caret)).ConfigureAwait(true);
        _pushedVersion = _document.TextDocument.Version;
        if (entry.Kind is CompletionKind.Method or CompletionKind.ExtensionMethod && _session is { } session
            && session.Editor.CaretOffset > 0 && _document.TextDocument.GetCharAt(session.Editor.CaretOffset - 1) == '(')
        {
            await ShowSignatureHelpAsync().ConfigureAwait(true);
        }
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
        _session.CharTyped += OnCharTyped;
        _session.CaretMoved += OnCaretMoved;
        _session.DefinitionRequested += OnDefinitionRequested;
        _session.TypeNameResolver = offset => _document.FilePath is { } p ? _features.GetEnclosingTypeNameAsync(p, offset, CancellationToken.None) : Task.FromResult<string?>(null);
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
            _session.CharTyped -= OnCharTyped;
            _session.CaretMoved -= OnCaretMoved;
            _session.DefinitionRequested -= OnDefinitionRequested;
            _session.TypeNameResolver = null;
            _session = null;
        }
    }

    private void OnDefinitionRequested(object? sender, int offset) => DefinitionRequested?.Invoke(this, offset);

    private void OnViewportChanged(object? sender, EventArgs e) => ScheduleClassification();

    private void OnCaretMoved(object? sender, (int Line, int Column) caret)
    {
        if (_session?.SignatureHelp.IsOpen == true)
        {
            _ = ShowSignatureHelpAsync();
        }
    }

    private async void OnCharTyped(object? sender, (char Typed, int Offset) e)
    {
        if (_session is not { } session || _document.FilePath is not { } path || session.IsSnippetActive)
        {
            return;
        }

        if (e.Typed is '(' or ',')
        {
            await ShowSignatureHelpAsync().ConfigureAwait(true);
        }
        else if (e.Typed == ')')
        {
            await ShowSignatureHelpAsync().ConfigureAwait(true);
        }

        if (session.Completion.IsOpen)
        {
            return;
        }

        Sync();
        if (await _features.ShouldTriggerCompletionAsync(path, e.Offset, e.Typed, CancellationToken.None).ConfigureAwait(true))
        {
            await TriggerCompletionAsync().ConfigureAwait(true);
        }
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
