using System.Diagnostics;
using System.Globalization;
using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>Commands of menus, toolbar, palette and context menus.</summary>
public sealed partial class Workspace
{
    public void Show(BottomTab tab)
    {
        BottomTab = tab;
        IsBottomPanelVisible = true;
    }

    public async Task ShowTerminal()
    {
        Show(BottomTab.Terminal);
        if (Terminals.Sessions.Count == 0)
        {
            await Terminals.NewShell();
        }
    }

    /// <summary>Opens (or creates) <c>&lt;Project&gt;.cd</c> in the folder of the startup project (where Program.cs is) with all source types of the solution.</summary>
    public async Task OpenSolutionDiagram()
    {
        if (SolutionPath is null)
        {
            return;
        }

        var project = Build.StartupProject ?? Solution?.Projects.FirstOrDefault()?.FilePath;
        var folder = RealPath.Resolve(Path.GetDirectoryName(project ?? SolutionPath)!);
        var name = Path.GetFileNameWithoutExtension(project ?? SolutionPath);
        var path = Path.Combine(folder, name + "." + ClassDiagramFile.FileExtension);
        await OpenDiagram(path, RealPath.Resolve(Path.GetDirectoryName(SolutionPath)!));
    }

    /// <summary>Opens (or creates) <c>&lt;Folder&gt;.cd</c> for a folder or project.</summary>
    public Task OpenDiagramForFolder(string folder) =>
        OpenDiagram(Path.Combine(folder, Path.GetFileName(folder.TrimEnd('/')) + "." + ClassDiagramFile.FileExtension), folder);

    /// <summary>Asks for a file name and creates the file (a class skeleton for <c>.cs</c> files) in a folder.</summary>
    public void PromptNewFile(string? folder)
    {
        var target = folder ?? ActiveDocument?.Directory ?? RootPath;
        if (target is null)
        {
            return;
        }

        Prompt = new PromptRequest(
            L.T("New File"),
            L.T("In %@. A name without extension creates a C# class.", Path.GetFileName(target)),
            "Customer",
            L.T("Create"),
            name => CreateFile(name, target));
    }

    public async Task CreateFile(string rawName, string folder)
    {
        var name = rawName.Trim();
        if (name.Length == 0)
        {
            return;
        }

        var fileName = name.Contains('.', StringComparison.Ordinal) ? name : name + ".cs";
        var path = Path.Combine(folder, fileName);
        if (File.Exists(path))
        {
            Alert = new AlertMessage(L.T("New File"), L.T("%@ already exists.", fileName));
            return;
        }

        var text = string.Empty;
        var isCSharp = Path.GetExtension(path) == ".cs";
        if (isCSharp && Engine is { } engine && IsEngineReady)
        {
            var typeName = Path.GetFileNameWithoutExtension(path);
            var kind = typeName.Length > 1 && typeName[0] == 'I' && char.IsUpper(typeName[1]) ? DiagramTypeKind.Interface : DiagramTypeKind.Class;
            try
            {
                text = (await engine.NewType(kind, typeName, folder)).Text;
            }
            catch (EngineException)
            {
                text = string.Empty;
            }
        }

        try
        {
            await File.WriteAllTextAsync(path, text);
            if (isCSharp && Engine is { } current)
            {
                await Run(() => current.AddDocument(path, text));
            }

            RefreshTree();
            OpenFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Alert = new AlertMessage(L.T("New File"), ex.Message);
        }
    }

    public void PromptNewFolder(string folder)
    {
        Prompt = new PromptRequest(L.T("New Folder"), Path.GetFileName(folder), "Models", L.T("Create"), name =>
        {
            try
            {
                Directory.CreateDirectory(Path.Combine(folder, name));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // try? in the original.
            }

            RefreshTree();
            return Task.CompletedTask;
        });
    }

    public void PromptRename(string path)
    {
        var current = Path.GetFileName(path);
        Prompt = new PromptRequest(L.T("Rename"), current, current, L.T("Rename"), name => RenameItem(path, name), current);
    }

    /// <summary>Moves a file or folder to the trash after confirmation.</summary>
    public async Task MoveToTrash(string path)
    {
        var confirmed = Dialogs is not null && await Dialogs.Confirm(L.T("Move “%@” to the Trash?", Path.GetFileName(path)), L.T("Move to Trash"));
        if (!confirmed)
        {
            return;
        }

        try
        {
            await Trash.Move(path);
            Tabs = [.. Tabs.Where(t => t is not EditorTab.Code code || !code.Document.Path.StartsWith(path, StringComparison.Ordinal))];
            if (Engine is { } engine)
            {
                await Run(() => engine.RemoveDocument(path));
            }

            RefreshTree();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Alert = new AlertMessage(L.T("Move to Trash"), ex.Message);
        }
    }

    /// <summary>Shows the item in the file manager (Finder on macOS): opens its folder with <c>xdg-open</c>.</summary>
    public void RevealInFinder(string path)
    {
        var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path;
        try
        {
            using var process = Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { folder }, UseShellExecute = false });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Alert = new AlertMessage(L.T("Show in File Manager"), ex.Message);
        }
    }

    public async Task RemoveProjectReference(string reference, string project)
    {
        try
        {
            await new ProjectService(CommandRunner.Dotnet()).RemoveReference(reference, project);
            RefreshTree();
            await RestartEngine();
        }
        catch (CommandException ex)
        {
            Alert = new AlertMessage(L.T("Remove Reference"), ex.Message);
        }
    }

    public async Task RemoveProjectFromSolution(string project)
    {
        if (SolutionPath is null)
        {
            return;
        }

        try
        {
            await new ProjectService(CommandRunner.Dotnet()).RemoveProject(project, SolutionPath);
            RefreshTree();
            await RestartEngine();
        }
        catch (CommandException ex)
        {
            Alert = new AlertMessage(L.T("Remove Project"), ex.Message);
        }
    }

    private async Task OpenDiagram(string path, string typesBelow)
    {
        if (!File.Exists(path))
        {
            if (!IsEngineReady)
            {
                Status = new StatusMessage(L.T("Wait until the solution is loaded"), StatusStyle.Warning);
                return;
            }

            try
            {
                var factory = DiagramFactory ?? throw new InvalidOperationException("No class diagram factory is registered.");
                await factory.Create(path, typesBelow, this);
                RefreshTree();
            }
            catch (Exception ex) when (ex is EngineException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Alert = new AlertMessage(L.T("Class Diagram"), ex.Message);
                return;
            }
        }

        OpenDiagram(path);
    }

    private async Task RenameItem(string path, string name)
    {
        if (name.Length == 0 || name == Path.GetFileName(path))
        {
            return;
        }

        var target = Path.Combine(Path.GetDirectoryName(path)!, name);
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Move(path, target);
            }
            else
            {
                File.Move(path, target);
            }

            OpenDocuments.FirstOrDefault(d => d.Path == path)?.Rename(target);
            if (Path.GetExtension(path) == ".cs" && Engine is { } engine)
            {
                await Run(() => engine.RemoveDocument(path));
                if (TryReadText(target) is { } text)
                {
                    await Run(() => engine.AddDocument(target, text));
                }
            }

            RefreshTree();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Alert = new AlertMessage(L.T("Rename"), ex.Message);
        }
    }

    /// <summary>
    /// The freedesktop.org trash (FileManager.trashItem on macOS): <c>gio trash</c> when available, otherwise the home trash
    /// (<c>$XDG_DATA_HOME/Trash</c>) written directly following the Trash specification.
    /// </summary>
    private static class Trash
    {
        public static async Task Move(string path)
        {
            if (await TryGio(path))
            {
                return;
            }

            var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } data ? data : Path.Combine(AppPaths.Home, ".local/share");
            var files = Path.Combine(dataHome, "Trash", "files");
            var info = Path.Combine(dataHome, "Trash", "info");
            Directory.CreateDirectory(files);
            Directory.CreateDirectory(info);
            var name = Path.GetFileName(path.TrimEnd('/'));
            var candidate = name;
            for (var counter = 2; File.Exists(Path.Combine(info, candidate + ".trashinfo")) || Path.Exists(Path.Combine(files, candidate)); counter++)
            {
                candidate = $"{Path.GetFileNameWithoutExtension(name)} {counter}{Path.GetExtension(name)}";
            }

            var deletion = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            var encoded = string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
            await File.WriteAllTextAsync(Path.Combine(info, candidate + ".trashinfo"), $"[Trash Info]\nPath={encoded}\nDeletionDate={deletion}\n");
            if (Directory.Exists(path))
            {
                Directory.Move(path, Path.Combine(files, candidate));
            }
            else
            {
                File.Move(path, Path.Combine(files, candidate));
            }
        }

        private static async Task<bool> TryGio(string path)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo("gio") { ArgumentList = { "trash", "--", path }, UseShellExecute = false, RedirectStandardError = true });
                if (process is null)
                {
                    return false;
                }

                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }
    }
}
