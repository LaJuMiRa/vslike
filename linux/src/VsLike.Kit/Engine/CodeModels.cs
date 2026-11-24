using System.Text.Json.Serialization;

namespace VsLike.Kit;

// Messages of the engine protocol (see engine/src/VsLike.Engine/Protocol). Offsets are UTF-16 code units, the same
// unit .NET strings and AvaloniaEdit use; lines and columns are 1-based.

/// <summary>Ordered from least to most severe, like the Swift original.</summary>
public enum DiagnosticSeverity
{
    Hidden,
    Info,
    Warning,
    Error,
}

public sealed record Diagnostic(
    [property: JsonPropertyName("id")] string Code,
    string Message,
    DiagnosticSeverity Severity,
    string? FilePath,
    int Start,
    int Length,
    int StartLine,
    int StartColumn,
    string Category,
    string? ProjectName = null,
    string? HelpLink = null)
{
    /// <summary>Gets the identity used for lists ("file:line:column:code:message").</summary>
    [JsonIgnore]
    public string Id => $"{FilePath ?? string.Empty}:{StartLine}:{StartColumn}:{Code}:{Message}";

    [JsonIgnore]
    public bool IsStyleCop => Category == "StyleCop";
}

[JsonConverter(typeof(CompletionKindConverter))]
public enum CompletionKind
{
    Other,
    Keyword,
    Class,
    Struct,
    Interface,
    Enum,
    EnumMember,
    Delegate,
    Method,
    ExtensionMethod,
    Property,
    Field,
    Event,
    Local,
    Parameter,
    Namespace,
    TypeParameter,
    Constant,
    Snippet,
}

public sealed record CompletionItem
{
    [JsonConstructor]
    public CompletionItem(string displayText, string? filterText, string? sortText, CompletionKind kind, string? inlineDescription, bool isPreselected, int handle)
    {
        DisplayText = displayText;
        FilterText = filterText ?? displayText;
        SortText = sortText ?? displayText;
        Kind = kind;
        InlineDescription = inlineDescription;
        IsPreselected = isPreselected;
        Handle = handle;
    }

    public CompletionItem(string displayText, CompletionKind kind, int handle, string? filterText = null, string? sortText = null, string? inlineDescription = null, bool isPreselected = false)
        : this(displayText, filterText, sortText, kind, inlineDescription, isPreselected, handle)
    {
    }

    public string DisplayText { get; init; }

    public string FilterText { get; init; }

    public string SortText { get; init; }

    public CompletionKind Kind { get; init; }

    public string? InlineDescription { get; init; }

    public bool IsPreselected { get; init; }

    public int Handle { get; init; }

    [JsonIgnore]
    public int Id => Handle;
}

public sealed record CompletionList(IReadOnlyList<CompletionItem> Items, int SpanStart, int SpanLength);

public sealed record TextEdit(int Start, int Length, string NewText);

/// <param name="NewFileText">Complete content when the edit creates the file.</param>
public sealed record FileEdit(string FilePath, IReadOnlyList<TextEdit> Edits, string? NewFileText = null);

public sealed record WorkspaceEdit(IReadOnlyList<FileEdit> Files, string? CaretFile = null, int? CaretOffset = null)
{
    public static readonly WorkspaceEdit Empty = new([]);

    [JsonIgnore]
    public bool IsEmpty => Files.All(f => f.Edits.Count == 0 && f.NewFileText is null);
}

/// <param name="Group">Diagnostic id for fixes ("SA1028", "CS0103"), otherwise a refactoring group.</param>
public sealed record CodeAction(string Title, string Group, bool IsFix, int Handle)
{
    [JsonIgnore]
    public int Id => Handle;

    [JsonIgnore]
    public bool IsFixAll => Title.StartsWith("Fix all", StringComparison.Ordinal) || Title.StartsWith("Alle im", StringComparison.Ordinal);
}

public sealed record QuickInfo(int Start, int Length, string Signature, string? Documentation, IReadOnlyList<Diagnostic> Diagnostics);

public sealed record SignatureInfo(string Prefix, IReadOnlyList<string> Parameters, string Suffix, string? Documentation)
{
    [JsonIgnore]
    public string Label => Prefix + string.Join(", ", Parameters) + Suffix;
}

public sealed record SignatureHelp(IReadOnlyList<SignatureInfo> Signatures, int ActiveSignature, int ActiveParameter);

public sealed record SourceLocation(string FilePath, int Start, int Length, int Line, int Column, string LineText)
{
    [JsonIgnore]
    public string Id => $"{FilePath}:{Start}";
}

public sealed record RenameTarget(string CurrentName, string SymbolDisplay, bool CanRename, string? Reason, int ReferenceCount);

public sealed record ClassifiedSpan(int Start, int Length, string Classification);

public sealed record WorkspaceProject(
    string Name,
    string FilePath,
    string? TargetFramework,
    string? RootNamespace,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> Documents)
{
    [JsonIgnore]
    public string Id => FilePath;
}

/// <param name="Duration">.NET TimeSpan text, e.g. "00:00:03.1234567".</param>
public sealed record WorkspaceLoadResult(string Path, IReadOnlyList<WorkspaceProject> Projects, IReadOnlyList<string> Warnings, string Duration);

public sealed record EngineInfo(string EngineVersion, string MsBuildPath, string SdkVersion);

/// <summary>Unknown kinds decode as <see cref="CompletionKind.Other"/> (newer engines may add kinds).</summary>
public sealed class CompletionKindConverter : JsonConverter<CompletionKind>
{
    public override CompletionKind Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        var raw = reader.GetString();
        return Enum.TryParse<CompletionKind>(raw, ignoreCase: true, out var kind) ? kind : CompletionKind.Other;
    }

    public override void Write(System.Text.Json.Utf8JsonWriter writer, CompletionKind value, System.Text.Json.JsonSerializerOptions options)
    {
        var name = value.ToString();
        writer.WriteStringValue(char.ToLowerInvariant(name[0]) + name[1..]);
    }
}
