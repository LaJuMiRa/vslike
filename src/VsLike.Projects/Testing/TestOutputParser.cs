using System.Globalization;
using System.Xml.Linq;
using VsLike.Core.Build;

namespace VsLike.Projects.Testing;

/// <summary>Liest die Testliste von <c>dotnet test --list-tests</c> und Ergebnisse aus TRX-Dateien.</summary>
public static class TestOutputParser
{
    private static readonly XNamespace Trx = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    /// <summary>
    /// Test names follow the (localized) header line ending with a colon and are indented by four spaces.
    /// </summary>
    public static IReadOnlyList<string> ParseTestList(IEnumerable<OutputLine> output)
    {
        var lines = output.Where(l => !l.IsError).Select(l => l.Text).ToList();
        var header = lines.FindLastIndex(l => l.Length > 0 && !char.IsWhiteSpace(l[0]) && l.TrimEnd().EndsWith(':'));
        if (header < 0)
        {
            return [];
        }

        return lines.Skip(header + 1)
            .TakeWhile(l => l.StartsWith("    ", StringComparison.Ordinal))
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<TestResultInfo> ParseTrx(string xml)
    {
        var document = XDocument.Parse(xml);
        return document.Descendants(Trx + "UnitTestResult").Select(result =>
        {
            var error = result.Element(Trx + "Output")?.Element(Trx + "ErrorInfo");
            var duration = TimeSpan.TryParse((string?)result.Attribute("duration"), CultureInfo.InvariantCulture, out var d) ? d : TimeSpan.Zero;
            return new TestResultInfo(
                (string?)result.Attribute("testName") ?? string.Empty,
                ParseOutcome((string?)result.Attribute("outcome")),
                duration,
                error?.Element(Trx + "Message")?.Value,
                error?.Element(Trx + "StackTrace")?.Value);
        }).ToList();
    }

    /// <summary>Finds the first stack frame location in the test's own source (<c>in /path/File.cs:line 42</c>).</summary>
    public static (string File, int Line)? FirstSourceFrame(string? stackTrace)
    {
        if (string.IsNullOrEmpty(stackTrace))
        {
            return null;
        }

        foreach (var raw in stackTrace.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var marker = line.LastIndexOf(":line ", StringComparison.Ordinal);
            var inIndex = line.LastIndexOf(" in ", StringComparison.Ordinal);
            if (marker > 0 && inIndex > 0 && inIndex < marker
                && int.TryParse(line[(marker + 6)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                return (line[(inIndex + 4)..marker], number);
            }
        }

        return null;
    }

    private static TestOutcome ParseOutcome(string? outcome) => outcome switch
    {
        "Passed" => TestOutcome.Passed,
        "Failed" or "Error" or "Timeout" or "Aborted" => TestOutcome.Failed,
        "NotExecuted" or "Inconclusive" or "Skipped" => TestOutcome.Skipped,
        _ => TestOutcome.NotRun,
    };
}
