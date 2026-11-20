using Avalonia.Headless.XUnit;
using VsLike.App.Output;
using VsLike.Core.Documents;

namespace VsLike.App.Tests;

public sealed class OutputViewModelTests
{
    [AvaloniaFact]
    public async Task Append_FromBackgroundThreads_IsFlushedInOrderAndDiagnosticsNavigate()
    {
        var navigator = new RecordingNavigator();
        var output = new OutputViewModel(navigator);
        output.Begin("Build");

        await Task.Run(() =>
        {
            output.AppendText("  App -> /src/App/bin/App.dll");
            output.AppendText("/src/App/Program.cs(3,7): error CS1002: ; expected [/src/App/App.csproj]", isError: false);
        });
        output.Flush();

        Assert.Equal(["Build", "  App -> /src/App/bin/App.dll", "/src/App/Program.cs(3,7): error CS1002: ; expected [/src/App/App.csproj]"], output.Lines.Select(l => l.Text));
        Assert.False(output.Lines[1].IsNavigable);
        await output.OpenAsync(output.Lines[2]);
        Assert.Equal(("/src/App/Program.cs", 3, 7), navigator.Last);
    }

    [AvaloniaFact]
    public void Flush_KeepsOnlyTheNewestLines()
    {
        var output = new OutputViewModel(new RecordingNavigator());
        for (var i = 0; i < OutputViewModel.MaxLines + 10; i++)
        {
            output.AppendText($"line {i}");
        }

        output.Flush();

        Assert.Equal(OutputViewModel.MaxLines, output.Lines.Count);
        Assert.Equal("line 10", output.Lines[0].Text);
    }

    private sealed class RecordingNavigator : IDocumentNavigator
    {
        public (string Path, int? Line, int? Column)? Last { get; private set; }

        public Task OpenAsync(string path, int? line = null, int? column = null)
        {
            Last = (path, line, column);
            return Task.CompletedTask;
        }
    }
}
