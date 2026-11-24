using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace VsLike.Kit;

public enum TestOutcome
{
    NotRun,
    Running,
    Passed,
    Failed,
    Skipped,
}

public sealed record TestResult(string FullName, TestOutcome Outcome, double Duration, string? ErrorMessage, string? StackTrace);

/// <summary><c>Ns.Sub.Class.Method(args)</c> split into namespace, class and method.</summary>
public sealed record TestName
{
    public TestName(string fullName)
    {
        FullName = fullName;
        var head = fullName.Split('(', 2)[0];
        var parts = head.Split('.', StringSplitOptions.RemoveEmptyEntries);
        switch (parts.Length)
        {
            case 0:
            case 1:
                Namespace = string.Empty;
                ClassName = string.Empty;
                Method = parts.Length > 0 ? parts[0] : fullName;
                break;
            case 2:
                Namespace = string.Empty;
                ClassName = parts[0];
                Method = parts[1];
                break;
            default:
                Namespace = string.Join(".", parts[..^2]);
                ClassName = parts[^2];
                Method = parts[^1];
                break;
        }
    }

    public string FullName { get; }

    public string Namespace { get; }

    public string ClassName { get; }

    public string Method { get; }

    public string ClassFullName => Namespace.Length == 0 ? ClassName : Namespace + "." + ClassName;

    public string MethodFullName => ClassFullName.Length == 0 ? Method : ClassFullName + "." + Method;

    /// <summary>Gets the argument part of a data-driven case, e.g. <c>(x: 1)</c>.</summary>
    public string Arguments => FullName.Length > MethodFullName.Length ? FullName[MethodFullName.Length..] : string.Empty;
}

/// <summary>Finds and runs tests with <c>dotnet test</c> (VSTest: xUnit, NUnit, MSTest). Results come from a TRX file.</summary>
public sealed record TestService(CommandRunner Runner)
{
    public async Task<(IReadOnlyList<string> Tests, ProcessResult Process)> Discover(string project, string configuration, Action<OutputLine>? onOutput = null, CancellationToken cancellationToken = default)
    {
        var process = await Runner.Run(
            ["test", project, "-c", configuration, "--list-tests", "-nologo", "-tl:off"],
            Path.GetDirectoryName(project),
            onOutput: onOutput,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return (ParseTestList(process.Output), process);
    }

    public async Task<(IReadOnlyList<TestResult> Results, ProcessResult Process)> Run(string project, string configuration, string? filter, Action<OutputLine>? onOutput = null, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(Path.GetTempPath(), "vslike-tests", Guid.NewGuid().ToString());
        var trx = Path.Combine(directory, "results.trx");
        try
        {
            var arguments = new List<string> { "test", project, "-c", configuration, "-nologo", "-tl:off", "--logger", "trx;LogFileName=" + trx, "--results-directory", directory };
            if (!string.IsNullOrEmpty(filter))
            {
                arguments.AddRange(["--filter", filter]);
            }

            var process = await Runner.Run(arguments, Path.GetDirectoryName(project), onOutput: onOutput, cancellationToken: cancellationToken).ConfigureAwait(false);
            var results = File.Exists(trx) ? ParseTrx(await File.ReadAllBytesAsync(trx, CancellationToken.None).ConfigureAwait(false)) : [];
            return (results, process);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Nothing to clean up.
            }
        }
    }

    /// <summary>Test names follow the (localized) header line ending with a colon and are indented by four spaces.</summary>
    public static IReadOnlyList<string> ParseTestList(IEnumerable<OutputLine> output)
    {
        var lines = output.Where(l => !l.IsError).Select(l => l.Text).ToList();
        var header = lines.FindLastIndex(l => l.Length > 0 && !char.IsWhiteSpace(l[0]) && l.Trim().EndsWith(':'));
        if (header < 0)
        {
            return [];
        }

        var seen = new HashSet<string>();
        return lines.Skip(header + 1)
            .TakeWhile(l => l.StartsWith("    ", StringComparison.Ordinal))
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && seen.Add(l)).ToList();
    }

    public static IReadOnlyList<TestResult> ParseTrx(byte[] data)
    {
        XDocument document;
        try
        {
            using var stream = new MemoryStream(data);
            document = XDocument.Load(stream);
        }
        catch (XmlException)
        {
            return [];
        }

        return document.Descendants().Where(e => e.Name.LocalName == "UnitTestResult").Select(element =>
        {
            var error = element.Elements().FirstOrDefault(e => e.Name.LocalName == "Output")?.Elements().FirstOrDefault(e => e.Name.LocalName == "ErrorInfo");
            var outcome = (string?)element.Attribute("outcome") switch
            {
                "Passed" => TestOutcome.Passed,
                "Failed" or "Error" or "Timeout" or "Aborted" => TestOutcome.Failed,
                "NotExecuted" or "Inconclusive" or "Skipped" => TestOutcome.Skipped,
                _ => TestOutcome.NotRun,
            };
            return new TestResult(
                (string?)element.Attribute("testName") ?? string.Empty,
                outcome,
                ParseDuration((string?)element.Attribute("duration")),
                error?.Elements().FirstOrDefault(e => e.Name.LocalName == "Message")?.Value,
                error?.Elements().FirstOrDefault(e => e.Name.LocalName == "StackTrace")?.Value);
        }).ToList();
    }

    /// <summary>First frame in the test's own source (<c>in /path/File.cs:line 42</c>).</summary>
    public static (string File, int Line)? FirstSourceFrame(string? stackTrace)
    {
        foreach (var raw in (stackTrace ?? string.Empty).Split('\n'))
        {
            var text = raw.Trim('\r');
            var marker = text.LastIndexOf(":line ", StringComparison.Ordinal);
            var inIndex = text.LastIndexOf(" in ", StringComparison.Ordinal);
            if (marker < 0 || inIndex < 0 || inIndex + 4 > marker
                || !int.TryParse(text[(marker + 6)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                continue;
            }

            return (text[(inIndex + 4)..marker], number);
        }

        return null;
    }

    /// <summary>A VSTest filter: exact matches for single tests, prefix matches (<c>~Ns.Class.</c>) for groups.</summary>
    public static string Filter(IEnumerable<string> exact, IEnumerable<string> prefixes)
    {
        var exactParts = exact.Select(e => "FullyQualifiedName=" + Escape(e.Split('(', 2)[0]));
        return string.Join("|", exactParts.Concat(prefixes.Select(p => "FullyQualifiedName~" + Escape(p))));
    }

    /// <summary><c>hh:mm:ss.fffffff</c> in seconds.</summary>
    internal static double ParseDuration(string? text)
    {
        var parts = text?.Split(':');
        if (parts is not { Length: 3 }
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var hours)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes)
            || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return 0;
        }

        return (hours * 3600) + (minutes * 60) + seconds;
    }

    internal static string Escape(string value)
    {
        var result = new System.Text.StringBuilder();
        foreach (var character in value)
        {
            if (@"\()&|=!~".Contains(character, StringComparison.Ordinal))
            {
                result.Append('\\');
            }

            result.Append(character);
        }

        return result.ToString();
    }
}
