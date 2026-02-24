using System.Text.RegularExpressions;
using System.Xml.Linq;
using DotNetTestRadar.Abstractions;

namespace DotNetTestRadar.Services;

public class SolutionParseResult
{
    public required string SolutionDirectory { get; set; }
    public required string GitRoot { get; set; }
    public required List<string> ProjectDirectories { get; set; }
}

public partial class SolutionParser
{
    private readonly IFileSystem _fileSystem;
    private readonly IProcessRunner _processRunner;

    public SolutionParser(IFileSystem fileSystem, IProcessRunner processRunner)
    {
        _fileSystem = fileSystem;
        _processRunner = processRunner;
    }

    public SolutionParseResult Parse(string solutionPath)
    {
        if (!_fileSystem.FileExists(solutionPath))
            throw new FileNotFoundException($"Solution file not found: {solutionPath}");

        var extension = Path.GetExtension(solutionPath).ToLowerInvariant();
        if (extension is not ".sln" and not ".slnx")
            throw new ArgumentException($"Solution file must be a .sln or .slnx file: {solutionPath}");

        var solutionDir = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;

        var gitRoot = _processRunner.Run("git", "rev-parse --show-toplevel", solutionDir).Trim();
        gitRoot = NormalizePath(gitRoot);

        var csprojPaths = extension == ".slnx"
            ? ParseSlnx(solutionPath)
            : ParseSln(solutionPath);

        var solutionDirRelativeToGitRoot = Path.GetRelativePath(gitRoot, solutionDir);

        var projectDirs = csprojPaths
            .Select(p => NormalizePath(p))
            .Select(p => Path.GetDirectoryName(p) ?? "")
            .Where(d => !string.IsNullOrEmpty(d))
            .Select(d =>
            {
                if (solutionDirRelativeToGitRoot == ".")
                    return d;
                return Path.Combine(solutionDirRelativeToGitRoot, d);
            })
            .Select(d => d.Replace('\\', '/'))
            .Distinct()
            .ToList();

        // Handle projects at solution root (csproj in same dir as sln)
        var rootProjects = csprojPaths
            .Where(p => !p.Contains('/') && !p.Contains('\\'))
            .ToList();

        if (rootProjects.Count > 0)
        {
            var rootDir = solutionDirRelativeToGitRoot == "." ? "." : solutionDirRelativeToGitRoot.Replace('\\', '/');
            if (!projectDirs.Contains(rootDir))
                projectDirs.Add(rootDir);
        }

        return new SolutionParseResult
        {
            SolutionDirectory = solutionDir,
            GitRoot = gitRoot,
            ProjectDirectories = projectDirs
        };
    }

    private List<string> ParseSln(string solutionPath)
    {
        var content = _fileSystem.ReadAllText(solutionPath);
        var matches = SlnProjectRegex().Matches(content);

        var paths = new List<string>();
        foreach (Match match in matches)
        {
            var projectPath = match.Groups[1].Value;
            if (projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                paths.Add(NormalizePath(projectPath));
            }
        }

        if (paths.Count == 0)
            throw new InvalidOperationException("No .csproj project entries found in the .sln file.");

        return paths;
    }

    private List<string> ParseSlnx(string solutionPath)
    {
        var content = _fileSystem.ReadAllText(solutionPath);

        XDocument doc;
        try
        {
            doc = XDocument.Parse(content);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new InvalidOperationException($"The .slnx file contains invalid XML: {ex.Message}", ex);
        }

        if (doc.Root?.Name.LocalName != "Solution")
            throw new InvalidOperationException("The .slnx file must have a <Solution> root element.");

        var paths = doc.Root
            .Descendants("Project")
            .Select(e => e.Attribute("Path")?.Value)
            .Where(p => p != null && p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(p => NormalizePath(p!))
            .ToList();

        if (paths.Count == 0)
            throw new InvalidOperationException("No .csproj project entries found in the .slnx file.");

        return paths;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
    }

    [GeneratedRegex(@"Project\(""\{[^}]+\}""\)\s*=\s*""[^""]*""\s*,\s*""([^""]*)""\s*,")]
    private static partial Regex SlnProjectRegex();
}
