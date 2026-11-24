using System.Runtime.InteropServices;

namespace VsLike.Kit;

/// <summary>
/// Canonical file paths. The debugger, MSBuild and Roslyn report real paths (symbolic links resolved); <c>realpath(3)</c>
/// gives the form they use.
/// </summary>
public static partial class RealPath
{
    public static string Resolve(string path)
    {
        var resolved = NativeRealPath(path, nint.Zero);
        if (resolved == nint.Zero)
        {
            return Path.GetFullPath(path);
        }

        try
        {
            return Marshal.PtrToStringUTF8(resolved) ?? Path.GetFullPath(path);
        }
        finally
        {
            Free(resolved);
        }
    }

    [LibraryImport("libc", EntryPoint = "realpath", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint NativeRealPath(string path, nint resolved);

    [LibraryImport("libc", EntryPoint = "free")]
    private static partial void Free(nint pointer);
}
