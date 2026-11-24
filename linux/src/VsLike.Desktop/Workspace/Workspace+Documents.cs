using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>Documents, tabs, saving and breakpoints (part of Workspace.swift).</summary>
public sealed partial class Workspace
{
    /// <summary>Gets all breakpoints of the solution, including files that are not open.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<SourceBreakpoint>> AllBreakpoints
    {
        get
        {
            var all = new Dictionary<string, IReadOnlyList<SourceBreakpoint>>(Settings.Breakpoints ?? new Dictionary<string, IReadOnlyList<SourceBreakpoint>>());
            foreach (var document in OpenDocuments)
            {
                var list = document.Breakpoints.Values.OrderBy(b => b.Line).ToList();
                if (list.Count == 0)
                {
                    all.Remove(document.Path);
                }
                else
                {
                    all[document.Path] = list;
                }
            }

            return all;
        }
    }

    /// <summary>Opens a file in a tab (class diagrams open on the canvas and return null).</summary>
    public CodeDocument? OpenFile(string rawPath, int? line = null, int column = 1, int selectLength = 0)
    {
        if (DiagramDocument.IsDiagram(rawPath))
        {
            OpenDiagram(rawPath);
            return null;
        }

        var path = RealPath.Resolve(rawPath);
        var document = OpenDocuments.FirstOrDefault(d => d.Path == path);
        if (document is null)
        {
            try
            {
                document = CodeDocument.Load(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Alert = new AlertMessage(L.T("Cannot open file"), ex.Message);
                return null;
            }

            document.OnChange = DocumentChanged;
            document.Breakpoints = (Settings.Breakpoints?.GetValueOrDefault(path) ?? []).ToDictionary(b => b.Line);
            InsertTab(new EditorTab.Code(document));
            if (document.IsCSharp && IsEngineReady)
            {
                Engine?.UpdateDocument(path, document.Text);
            }
        }

        if (line is { } target)
        {
            document.PendingSelection = new TextRange(document.Offset(target, column), selectLength);
        }

        ActiveTabId = document.Id;
        return document;
    }

    public void OpenDiagram(string path)
    {
        if (Tabs.OfType<EditorTab.Diagram>().FirstOrDefault(d => d.Editor.Path == path) is { } existing)
        {
            ActiveTabId = existing.Id;
            return;
        }

        var factory = DiagramFactory ?? throw new InvalidOperationException("No class diagram factory is registered.");
        var diagram = factory.Open(path, this);
        InsertTab(new EditorTab.Diagram(diagram));
        ActiveTabId = diagram.Id;
        diagram.ScheduleRefresh(immediately: true);
    }

    public async Task Close(EditorTab tab)
    {
        if (tab is EditorTab.Code { Document: { IsDirty: true } document })
        {
            switch (await ConfirmSave([document.Name]))
            {
                case SaveChoice.Save:
                    Save(document);
                    break;
                case SaveChoice.Discard:
                    if (document.IsCSharp && TryReadText(document.Path) is { } disk)
                    {
                        Engine?.UpdateDocument(document.Path, disk);
                    }

                    break;
                case SaveChoice.Cancel:
                    return;
            }
        }

        var list = Tabs.ToList();
        var index = list.FindIndex(t => t.Id == tab.Id);
        if (index < 0)
        {
            return;
        }

        list.RemoveAt(index);
        Tabs = list;
        if (ActiveTabId == tab.Id)
        {
            ActiveTabId = list.Count == 0 ? null : list[Math.Min(index, list.Count - 1)].Id;
        }
    }

    public async Task CloseOtherTabs(EditorTab tab)
    {
        foreach (var other in Tabs.Where(t => t.Id != tab.Id).ToList())
        {
            await Close(other);
        }
    }

    public void Save(CodeDocument document)
    {
        try
        {
            document.Save();
            Status = new StatusMessage(L.T("Saved %@", document.Name));
            if (document.Name.Equals(StyleCopSettingsFile.FileName, StringComparison.OrdinalIgnoreCase))
            {
                _ = ReloadStyleCopSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Alert = new AlertMessage(L.T("Saving failed"), ex.Message);
        }
    }

    public void SaveAll()
    {
        foreach (var document in OpenDocuments.Where(d => d.IsDirty))
        {
            Save(document);
        }
    }

    public async Task SaveActive()
    {
        if (ActiveDocument is not { } document)
        {
            return;
        }

        if (Preferences.FormatOnSave && document.IsCSharp)
        {
            await FormatDocument(document);
        }

        Save(document);
    }

    public void PersistBreakpoints()
    {
        if (SolutionPath is null)
        {
            return;
        }

        var all = AllBreakpoints;
        Settings = Settings with { Breakpoints = all.Count == 0 ? null : all };
        TrySaveSettings();
    }

    private void InsertTab(EditorTab tab)
    {
        var list = Tabs.ToList();
        var active = list.FindIndex(t => t.Id == ActiveTabId);
        if (active >= 0)
        {
            list.Insert(active + 1, tab);
        }
        else
        {
            list.Add(tab);
        }

        Tabs = list;
    }
}
