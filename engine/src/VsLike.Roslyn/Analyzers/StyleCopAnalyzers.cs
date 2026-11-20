using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;

namespace VsLike.Roslyn.Analyzers;

/// <summary>Lädt Analyzer und Code-Fix-Provider von StyleCop einmal pro Prozess aus dem NuGet-Cache.</summary>
public static class StyleCopAnalyzers
{
    private static readonly object Gate = new();
    private static (ImmutableArray<DiagnosticAnalyzer> Analyzers, ImmutableArray<CodeFixProvider> Fixes)? _loaded;

    public static ImmutableArray<DiagnosticAnalyzer> Analyzers => Load().Analyzers;

    public static ImmutableArray<CodeFixProvider> FixProviders => Load().Fixes;

    private static (ImmutableArray<DiagnosticAnalyzer> Analyzers, ImmutableArray<CodeFixProvider> Fixes) Load()
    {
        lock (Gate)
        {
            if (_loaded is { } loaded)
            {
                return loaded;
            }

            if (!StyleCopPackage.IsInCache)
            {
                throw new InvalidOperationException($"{StyleCopPackage.Id} {StyleCopPackage.Version} is not in the NuGet cache ({StyleCopPackage.AnalyzerDirectory}).");
            }

            var reference = new AnalyzerFileReference(StyleCopPackage.AnalyzerAssembly, Loader.Instance);
            var analyzers = reference.GetAnalyzers(LanguageNames.CSharp);
            var fixAssembly = Loader.Instance.LoadFromPath(StyleCopPackage.CodeFixAssembly);
            var fixes = fixAssembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(CodeFixProvider).IsAssignableFrom(t) && t.GetCustomAttribute<ExportCodeFixProviderAttribute>() is not null && t.GetConstructor(Type.EmptyTypes) is not null)
                .Select(t => (CodeFixProvider)Activator.CreateInstance(t)!)
                .ToImmutableArray();
            _loaded = (analyzers, fixes);
            return _loaded.Value;
        }
    }

    private sealed class Loader : IAnalyzerAssemblyLoader
    {
        public static readonly Loader Instance = new();

        public void AddDependencyLocation(string fullPath)
        {
        }

        public Assembly LoadFromPath(string fullPath)
            => AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a => !a.IsDynamic && string.Equals(a.Location, fullPath, StringComparison.Ordinal))
                ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
    }
}
