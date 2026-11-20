using Avalonia.Headless.XUnit;
using VsLike.App.Services;
using VsLike.Diagram.Layout;
using VsLike.Diagram.Model;

namespace VsLike.App.Tests;

/// <summary>Abnahme Phase 8: Diagramm ↔ Code in beide Richtungen, ohne Formatierungsverluste, mit gemeinsamem Undo.</summary>
public sealed class DiagramAppTests : IDisposable
{
    private readonly TestPlatform _platform = new();
    private readonly TestSolution _solution = new();
    private readonly FakeDialogService _dialogs = new();

    [AvaloniaFact]
    public async Task DiagramEdits_WriteCode_AndCodeChangesUpdateDiagram()
    {
        using var workbench = new Workbench(new AppServices(_platform), _dialogs, () => null);
        var window = new MainWindow(workbench);
        window.Show();
        Assert.True(await workbench.OpenSolutionAsync(_solution.SolutionPath), workbench.Shell.StatusText);
        var lib1 = _solution.PathOf("Lib1");

        var diagram = await workbench.Diagrams.ShowForFolderAsync(lib1, "Lib1");
        Assert.NotNull(diagram);
        Assert.True(File.Exists(Path.Combine(lib1, "Lib1" + DiagramDocument.Extension)));
        Assert.NotNull(diagram.Layout.Find("Lib1.Car"));

        Assert.True(await diagram.CreateTypeAsync(DiagramTypeKind.Class, "Truck", 40, 400));
        var truckPath = Path.Combine(lib1, "Truck.cs");
        Assert.Equal("namespace Lib1;\n\npublic class Truck\n{\n}\n", File.ReadAllText(truckPath));
        Assert.NotNull(diagram.Model.Find("Lib1.Truck"));
        Assert.Equal((40d, 400d), (diagram.Layout.Find("Lib1.Truck")!.X, diagram.Layout.Find("Lib1.Truck")!.Y));

        await diagram.ConnectAsync("Lib1.Truck", "Lib1.Car");
        Assert.Contains("public class Truck : Car", File.ReadAllText(truckPath), StringComparison.Ordinal);
        Assert.Contains(diagram.Model.Relations, r => r is { From: "Lib1.Truck", To: "Lib1.Car", Kind: RelationKind.Inheritance });

        var carPath = _solution.PathOf("Lib1/Car.cs");
        var carBefore = File.ReadAllText(carPath);
        var enginePath = _solution.PathOf("Lib2/Engine.cs");
        var engineBefore = File.ReadAllText(enginePath);
        await diagram.AddMemberAsync("Lib1.Car", new DiagramMember(MemberKind.Method, "Honk", "string"));
        var carAfter = File.ReadAllText(carPath);
        var tail = carBefore[carBefore.LastIndexOf('}')..];
        Assert.StartsWith(carBefore[..carBefore.LastIndexOf('}')].TrimEnd('\n'), carAfter, StringComparison.Ordinal);
        Assert.EndsWith(tail, carAfter, StringComparison.Ordinal);
        Assert.Contains("public string Honk()", carAfter, StringComparison.Ordinal);
        Assert.Equal(engineBefore, File.ReadAllText(enginePath));

        File.WriteAllText(carPath, carAfter.Replace("public string Honk()", "public int Wheels { get; set; } = 4;\n\n    public string Honk()", StringComparison.Ordinal));
        await UiWait.UntilAsync(() => diagram.Model.Find("Lib1.Car")?.MemberList.Any(m => m.Name == "Wheels") == true, TimeSpan.FromSeconds(15), "external change in diagram");
        Assert.Equal((40d, 400d), (diagram.Layout.Find("Lib1.Truck")!.X, diagram.Layout.Find("Lib1.Truck")!.Y));

        await diagram.UndoAsync();
        Assert.DoesNotContain("Honk", File.ReadAllText(carPath), StringComparison.Ordinal);
        await diagram.UndoAsync();
        Assert.DoesNotContain(": Car", File.ReadAllText(truckPath), StringComparison.Ordinal);
        await diagram.RedoAsync();
        Assert.Contains("public class Truck : Car", File.ReadAllText(truckPath), StringComparison.Ordinal);
        Assert.Equal(DiagramDocument.Load(diagram.FilePath).Find("Lib1.Truck"), diagram.Layout.Find("Lib1.Truck"));
    }

    [AvaloniaFact]
    public async Task RenameAndDelete_UseRoslynAndAskForConfirmation()
    {
        using var workbench = new Workbench(new AppServices(_platform), _dialogs, () => null);
        Assert.True(await workbench.OpenSolutionAsync(_solution.SolutionPath));
        var diagram = await workbench.Diagrams.ShowForFolderAsync(_solution.PathOf("Lib2"), "Lib2");
        Assert.NotNull(diagram);

        await diagram.RenameTypeAsync("Lib2.Engine", "Motor");

        Assert.True(File.Exists(_solution.PathOf("Lib2/Motor.cs")));
        Assert.False(File.Exists(_solution.PathOf("Lib2/Engine.cs")));
        Assert.Contains("Motor _engine", File.ReadAllText(_solution.PathOf("Lib1/Car.cs")), StringComparison.Ordinal);
        Assert.NotNull(diagram.Layout.Find("Lib2.Motor"));

        _dialogs.NextConfirm = false;
        await diagram.DeleteTypeAsync("Lib2.Motor");
        Assert.True(File.Exists(_solution.PathOf("Lib2/Motor.cs")));
        _dialogs.NextConfirm = true;
        await diagram.DeleteTypeAsync("Lib2.Motor");
        Assert.False(File.Exists(_solution.PathOf("Lib2/Motor.cs")));
        Assert.Null(diagram.Layout.Find("Lib2.Motor"));

        await diagram.UndoAsync();
        Assert.True(File.Exists(_solution.PathOf("Lib2/Motor.cs")));
    }

    public void Dispose()
    {
        _platform.Dispose();
        _solution.Dispose();
    }
}
