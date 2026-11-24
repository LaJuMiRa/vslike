using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>Applying engine edits across files, navigation, rename and formatting.</summary>
public sealed partial class Workspace
{
    /// <summary>
    /// Applies an edit from a code fix, rename, completion or formatting. Open documents change undoably; closed
    /// files are written to disk and pushed to the engine; new files are created and opened.
    /// </summary>
    public async Task Apply(WorkspaceEdit edit, string actionName)
    {
        foreach (var file in edit.Files)
        {
            var path = RealPath.Resolve(file.FilePath);
            if (file.NewFileText is { } text)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file.FilePath)!);
                    await File.WriteAllTextAsync(file.FilePath, text);
                    if (Engine is { } engine && IsEngineReady)
                    {
                        await Run(() => engine.AddDocument(file.FilePath, text));
                    }

                    OpenFile(file.FilePath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Alert = new AlertMessage(actionName, ex.Message);
                }
            }
            else if (OpenDocuments.FirstOrDefault(d => d.Path == path) is { } document)
            {
                document.Apply(file.Edits, actionName);
            }
            else if (TryReadText(path) is { } current)
            {
                var updated = TextEditing.Apply(file.Edits, current);
                try
                {
                    await File.WriteAllTextAsync(path, updated);
                    Engine?.UpdateDocument(path, updated);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Alert = new AlertMessage(actionName, ex.Message);
                }
            }
        }

        if (edit.CaretFile is { } caretFile && edit.CaretOffset is { } offset && OpenFile(caretFile) is { } caretDocument)
        {
            caretDocument.PendingSelection = new TextRange(offset, 0);
        }

        RefreshTree();
        NotifyCodeChanged();
    }

    public void Navigate(SourceLocation location) => OpenFile(location.FilePath, location.Line, location.Column, location.Length);

    public void Navigate(Diagnostic diagnostic)
    {
        if (diagnostic.FilePath is { } file)
        {
            OpenFile(file, diagnostic.StartLine, diagnostic.StartColumn, diagnostic.Length);
        }
    }

    public async Task GoToDefinition(CodeDocument document, int offset)
    {
        if (Engine is not { } engine || !IsEngineReady)
        {
            return;
        }

        SyncNow(document);
        try
        {
            var locations = await engine.Definition(document.Path, offset);
            if (locations.Count == 1)
            {
                Navigate(locations[0]);
            }
            else if (locations.Count > 1)
            {
                References = new ReferenceList(L.T("Definitions"), locations);
            }
            else
            {
                Status = new StatusMessage(L.T("No definition found"), StatusStyle.Warning);
            }
        }
        catch (EngineException ex)
        {
            Status = new StatusMessage(ex.Message, StatusStyle.Error);
        }
    }

    public async Task FindReferences(CodeDocument document, int offset)
    {
        if (Engine is not { } engine || !IsEngineReady)
        {
            return;
        }

        SyncNow(document);
        try
        {
            var locations = await engine.References(document.Path, offset);
            References = new ReferenceList(L.T("%lld references", locations.Count), locations);
        }
        catch (EngineException ex)
        {
            Status = new StatusMessage(ex.Message, StatusStyle.Error);
        }
    }

    public async Task Rename(CodeDocument document, int offset)
    {
        if (Engine is not { } engine || !IsEngineReady)
        {
            return;
        }

        SyncNow(document);
        RenameTarget? target;
        try
        {
            target = await engine.PrepareRename(document.Path, offset);
        }
        catch (EngineException)
        {
            target = null;
        }

        if (target is null)
        {
            Status = new StatusMessage(L.T("Nothing to rename here"), StatusStyle.Warning);
            return;
        }

        if (!target.CanRename)
        {
            Alert = new AlertMessage(L.T("Rename"), target.Reason ?? L.T("This symbol cannot be renamed."));
            return;
        }

        Prompt = new PromptRequest(
            L.T("Rename %@", target.SymbolDisplay),
            L.T("%lld references will be updated.", target.ReferenceCount),
            target.CurrentName,
            L.T("Rename"),
            async newName =>
            {
                if (newName == target.CurrentName || newName.Length == 0)
                {
                    return;
                }

                try
                {
                    var edit = await engine.Rename(document.Path, offset, newName);
                    await Apply(edit, L.T("Rename"));
                }
                catch (EngineException ex)
                {
                    Alert = new AlertMessage(L.T("Rename"), ex.Message);
                }
            },
            target.CurrentName);
    }

    public async Task FormatDocument(CodeDocument document)
    {
        if (Engine is not { } engine || !IsEngineReady || !document.IsCSharp)
        {
            return;
        }

        SyncNow(document);
        try
        {
            await Apply(await engine.Format(document.Path), L.T("Format Document"));
        }
        catch (EngineException)
        {
            // try? in the original.
        }
    }

    /// <summary>Code actions at a range: fixes for diagnostics there (StyleCop, compiler) and refactorings.</summary>
    public async Task<IReadOnlyList<CodeAction>> CodeActions(CodeDocument document, TextRange range)
    {
        if (Engine is not { } engine || !IsEngineReady || !document.IsCSharp)
        {
            return [];
        }

        SyncNow(document);
        try
        {
            return await engine.CodeActions(document.Path, range.Location, range.Length);
        }
        catch (EngineException)
        {
            return [];
        }
    }

    public async Task Perform(CodeAction action)
    {
        if (Engine is not { } engine)
        {
            return;
        }

        try
        {
            var edit = await engine.ResolveCodeAction(action.Handle);
            if (edit.IsEmpty)
            {
                Status = new StatusMessage(L.T("The action made no changes"), StatusStyle.Warning);
            }
            else
            {
                await Apply(edit, action.Title);
                Status = new StatusMessage(action.Title, StatusStyle.Success);
            }
        }
        catch (EngineException ex)
        {
            Alert = new AlertMessage(action.Title, ex.Message);
        }
    }

    /// <summary>
    /// Applies every StyleCop "fix all in document" action for the document's rule violations, rule by rule with
    /// fresh diagnostics after each fix (offsets move).
    /// </summary>
    public async Task FixAllStyleCop(CodeDocument document)
    {
        if (Engine is not { } engine || !IsEngineReady)
        {
            return;
        }

        var handled = new HashSet<string>();
        var fixedCount = 0;
        while (true)
        {
            SyncNow(document);
            IReadOnlyList<Diagnostic> current;
            try
            {
                current = await engine.Diagnostics(document.Path);
            }
            catch (EngineException)
            {
                current = [];
            }

            Diagnostics.SetLive(current, document.Path);
            if (current.FirstOrDefault(d => d.IsStyleCop && !handled.Contains(d.Code)) is not { } next)
            {
                break;
            }

            handled.Add(next.Code);
            var actions = await CodeActions(document, new TextRange(next.Start, Math.Max(next.Length, 0)));
            var fixAll = actions.FirstOrDefault(a => a.Group == next.Code && a.IsFixAll
                && (a.Title.Contains("document", StringComparison.Ordinal) || a.Title.Contains("Dokument", StringComparison.Ordinal)));
            if ((fixAll ?? actions.FirstOrDefault(a => a.Group == next.Code && a.IsFix)) is { } action)
            {
                await Perform(action);
                fixedCount++;
            }
        }

        Status = fixedCount == 0
            ? new StatusMessage(L.T("No StyleCop issues to fix in this file"), StatusStyle.Success)
            : new StatusMessage(L.T("Fixed %lld StyleCop rules", fixedCount), StatusStyle.Success);
    }
}
