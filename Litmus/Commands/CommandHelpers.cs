using Litmus.Abstractions;

namespace Litmus.Commands;

internal static class CommandHelpers
{
    /// <summary>
    /// Resolves the solution path from an explicitly provided value, or auto-discovers
    /// a single .sln/.slnx file in the current working directory when none is given.
    /// Returns (path, null) on success, (null, errorMessage) on failure.
    /// </summary>
    internal static (string? Path, string? Error) ResolveSolutionPath(
        string? provided, IFileSystem fileSystem)
    {
        if (provided != null)
            return (provided, null);

        var cwd = fileSystem.GetCurrentDirectory();
        IEnumerable<string> slnFiles, slnxFiles;
        try
        {
            slnFiles = fileSystem.GetFiles(cwd, "*.sln", recursive: false);
            slnxFiles = fileSystem.GetFiles(cwd, "*.slnx", recursive: false);
        }
        catch (Exception ex)
        {
            return (null, $"Could not search for solution files: {ex.Message}");
        }

        var found = slnFiles.Concat(slnxFiles).ToList();

        return found.Count switch
        {
            0 => (null,
                "No .sln or .slnx file found in the current directory.\n" +
                "Use --solution to specify the path."),
            1 => (found[0], null),
            _ => (null,
                "Multiple solution files found. Use --solution to specify which one:\n" +
                string.Join("\n", found.Select(f => $"  {Path.GetFileName(f)}")))
        };
    }
}
