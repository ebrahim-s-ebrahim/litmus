using DotNetTestRadar.Abstractions;

namespace DotNetTestRadar.Services;

public class ChurnResult
{
    public Dictionary<string, FileChurnData> Files { get; set; } = new();
}

public class FileChurnData
{
    public int Commits { get; set; }
    public int WeightedChurn { get; set; }
    public double ChurnNorm { get; set; }
}

public partial class GitChurnAnalyzer
{
    private readonly IProcessRunner _processRunner;

    public GitChurnAnalyzer(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public ChurnResult Analyze(
        string gitRoot,
        string solutionDirectory,
        List<string> projectDirectories,
        DateTime since,
        List<string> excludePatterns)
    {
        var allPatterns = FileFilterHelper.GetEffectivePatterns(excludePatterns);

        var pathspecs = projectDirectories
            .Select(d => $"\"{d}/**/*.cs\"")
            .ToList();

        if (pathspecs.Count == 0)
            return new ChurnResult();

        var sinceStr = since.ToString("yyyy-MM-dd");
        var args = $"log --since={sinceStr} --numstat --pretty=format:\"\" -- {string.Join(" ", pathspecs)}";

        var output = _processRunner.Run("git", args, gitRoot);
        return ParseNumstatOutput(output, gitRoot, solutionDirectory, allPatterns);
    }

    private static ChurnResult ParseNumstatOutput(
        string output,
        string gitRoot,
        string solutionDirectory,
        List<string> excludePatterns)
    {
        var result = new ChurnResult();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Track per-commit contributions to apply noise floor
        // numstat output groups lines per commit, separated by empty lines from --pretty=format:""
        // But since we split on non-empty, we process each line individually.
        // The format is: added\tdeleted\tfilepath per commit per file.

        var solutionRelPath = Path.GetRelativePath(gitRoot, solutionDirectory).Replace('\\', '/');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            var parts = trimmed.Split('\t');
            if (parts.Length < 3)
                continue;

            // Skip binary files
            if (parts[0] == "-" || parts[1] == "-")
                continue;

            if (!int.TryParse(parts[0], out var added) || !int.TryParse(parts[1], out var deleted))
                continue;

            var filePath = parts[2].Trim();

            // Apply noise floor: skip if this commit's contribution is <= 2
            if (added + deleted <= 2)
                continue;

            // Convert to relative to solution directory
            var relPath = filePath;
            if (solutionRelPath != "." && relPath.StartsWith(solutionRelPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                relPath = relPath[(solutionRelPath.Length + 1)..];
            }

            // Apply exclusion patterns
            if (FileFilterHelper.MatchesAnyPattern(relPath, excludePatterns))
                continue;

            if (!result.Files.TryGetValue(relPath, out var data))
            {
                data = new FileChurnData();
                result.Files[relPath] = data;
            }

            data.Commits++;
            data.WeightedChurn += added + deleted;
        }

        // Normalize
        var maxChurn = result.Files.Values.MaxBy(f => f.WeightedChurn)?.WeightedChurn ?? 0;
        if (maxChurn > 0)
        {
            foreach (var file in result.Files.Values)
            {
                file.ChurnNorm = (double)file.WeightedChurn / maxChurn;
            }
        }

        return result;
    }

}
