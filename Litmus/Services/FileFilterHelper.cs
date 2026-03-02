using System.Text.RegularExpressions;
using Litmus.Models;

namespace Litmus.Services;

public static class FileFilterHelper
{
    public static List<string> GetEffectivePatterns(List<string> userPatterns)
        => AnalysisOptions.DefaultExclusions.Concat(userPatterns).ToList();

    public static bool MatchesAnyPattern(string filePath, List<string> patterns)
    {
        var fileName = Path.GetFileName(filePath);
        var normalizedPath = filePath.Replace('\\', '/');
        foreach (var pattern in patterns)
        {
            if (MatchGlob(normalizedPath, pattern) || MatchGlob(fileName, pattern))
                return true;
        }
        return false;
    }

    public static bool MatchGlob(string input, string pattern)
    {
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*\\*", "<<DOUBLESTAR>>")
            .Replace("\\*", ".*")
            .Replace("<<DOUBLESTAR>>", ".*")
            .Replace("\\?", ".") + "$";
        return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase);
    }
}
