using System.Text;

namespace VsLike.Projects;

/// <summary>
/// Leitet den Namespace einer neuen Datei aus RootNamespace und Ordnerpfad ab (wie Visual-Studio-artige Tools
/// und <c>dotnet new</c>): Ordnernamen werden zu gültigen Bezeichnern, Segmente mit Punkt verbunden.
/// </summary>
public static class NamespaceResolver
{
    public static string Resolve(string projectFile, string? rootNamespace, string folder)
    {
        var root = string.IsNullOrWhiteSpace(rootNamespace) ? ToIdentifier(Path.GetFileNameWithoutExtension(projectFile)) : rootNamespace;
        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectFile))!;
        var relative = Path.GetRelativePath(projectDirectory, Path.GetFullPath(folder));
        if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal))
        {
            return root;
        }

        var segments = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Select(ToIdentifier);
        return string.Join('.', [root, .. segments]);
    }

    /// <summary>Turns a name like "my-app 2" into "my_app_2"; dots are kept as namespace separators.</summary>
    public static string ToIdentifier(string name)
    {
        var parts = name.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(part =>
        {
            var builder = new StringBuilder();
            foreach (var c in part)
            {
                builder.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            }

            if (builder.Length == 0 || char.IsDigit(builder[0]))
            {
                builder.Insert(0, '_');
            }

            return builder.ToString();
        });
        return string.Join('.', parts);
    }

    /// <summary>Returns the content of a new class file with a file-scoped namespace.</summary>
    public static string ClassFile(string @namespace, string className, string kind = "class")
        => $"namespace {@namespace};\n\npublic {kind} {className}\n{{\n}}\n";
}
