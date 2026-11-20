using VsLike.Core.Build;
using VsLike.Projects.ProjectFiles;

namespace VsLike.Projects.Testing;

/// <summary>Findet und startet Tests über <c>dotnet test</c> (VSTest; xUnit, NUnit, MSTest).</summary>
public sealed class TestService
{
    private static readonly string[] TestPackages = ["Microsoft.NET.Test.Sdk", "MSTest.Sdk", "xunit.v3", "TUnit"];

    private readonly IDotnetCli _cli;

    public TestService(IDotnetCli cli)
    {
        _cli = cli;
    }

    public static bool IsTestProject(string projectPath)
    {
        try
        {
            var project = ProjectFile.Load(projectPath);
            return string.Equals(project.GetProperty("IsTestProject"), "true", StringComparison.OrdinalIgnoreCase)
                || project.PackageReferences().Any(p => TestPackages.Contains(p.Id, StringComparer.OrdinalIgnoreCase))
                || File.ReadAllText(projectPath).Contains("Sdk=\"MSTest.Sdk", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Builds a VSTest filter: exact matches for single tests, prefix matches (<c>~Ns.Class.</c>) for groups.
    /// Characters with a meaning in the filter syntax are escaped.
    /// </summary>
    public static string BuildFilter(IReadOnlyCollection<string> exactNames, IReadOnlyCollection<string> prefixes)
        => string.Join('|', exactNames.Select(n => "FullyQualifiedName=" + Escape(StripArguments(n)))
            .Concat(prefixes.Select(p => "FullyQualifiedName~" + Escape(p))));

    public async Task<(IReadOnlyList<string> Tests, ProcessResult Process)> DiscoverAsync(string project, string configuration, Action<OutputLine>? onOutput, CancellationToken cancellationToken)
    {
        var process = await _cli.RunAsync(["test", project, "-c", configuration, "--list-tests", "-nologo", "-tl:off"], Path.GetDirectoryName(project), onOutput, cancellationToken).ConfigureAwait(false);
        return (TestOutputParser.ParseTestList(process.Output), process);
    }

    public async Task<(IReadOnlyList<TestResultInfo> Results, ProcessResult Process)> RunAsync(string project, string configuration, string? filter, Action<OutputLine>? onOutput, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), "vslike-tests", Guid.NewGuid().ToString("N"));
        var trx = Path.Combine(directory, "results.trx");
        var arguments = new List<string> { "test", project, "-c", configuration, "-nologo", "-tl:off", "--logger", "trx;LogFileName=" + trx, "--results-directory", directory };
        if (!string.IsNullOrEmpty(filter))
        {
            arguments.AddRange(["--filter", filter]);
        }

        try
        {
            var process = await _cli.RunAsync(arguments, Path.GetDirectoryName(project), onOutput, cancellationToken).ConfigureAwait(false);
            var results = File.Exists(trx) ? TestOutputParser.ParseTrx(await File.ReadAllTextAsync(trx, cancellationToken).ConfigureAwait(false)) : [];
            return (results, process);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static string StripArguments(string name)
    {
        var paren = name.IndexOf('(', StringComparison.Ordinal);
        return paren < 0 ? name : name[..paren];
    }

    private static string Escape(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c is '\\' or '(' or ')' or '&' or '|' or '=' or '!' or '~')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
