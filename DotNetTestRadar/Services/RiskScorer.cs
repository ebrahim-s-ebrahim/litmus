using DotNetTestRadar.Models;

namespace DotNetTestRadar.Services;

public class RiskScorer
{
    public List<FileRiskReport> Score(
        ChurnResult churn,
        CoverageResult coverage,
        ComplexityResult complexity,
        string solutionDirectory,
        string gitRoot)
    {
        var allFiles = new HashSet<string>();
        foreach (var f in churn.Files.Keys) allFiles.Add(f);

        var solutionRelPath = Path.GetRelativePath(gitRoot, solutionDirectory).Replace('\\', '/');

        foreach (var f in complexity.FileComplexity.Keys)
        {
            // Convert from git-root-relative to solution-relative
            var rel = f;
            if (solutionRelPath != "." && rel.StartsWith(solutionRelPath + "/", StringComparison.OrdinalIgnoreCase))
                rel = rel[(solutionRelPath.Length + 1)..];
            allFiles.Add(rel);
        }

        var reports = new List<FileRiskReport>();

        foreach (var file in allFiles)
        {
            var churnData = churn.Files.GetValueOrDefault(file);
            var churnNorm = churnData?.ChurnNorm ?? 0.0;
            var commits = churnData?.Commits ?? 0;
            var weightedChurn = churnData?.WeightedChurn ?? 0;

            var coverageRate = CoverageParser.GetCoverageForFile(coverage, file);

            // Build git-root-relative path for complexity lookup
            var gitRootRelPath = file;
            if (solutionRelPath != ".")
                gitRootRelPath = solutionRelPath + "/" + file;

            var complexityNorm = complexity.FileComplexityNorm.GetValueOrDefault(gitRootRelPath, 0.0);
            var rawComplexity = complexity.FileComplexity.GetValueOrDefault(gitRootRelPath, 0);

            var riskScore = churnNorm * (1 - coverageRate) * (1 + complexityNorm);
            var riskLevel = riskScore switch
            {
                >= 0.6 => "High",
                >= 0.2 => "Medium",
                _ => "Low"
            };

            reports.Add(new FileRiskReport
            {
                File = file,
                Commits = commits,
                WeightedChurn = weightedChurn,
                ChurnNorm = churnNorm,
                CoverageRate = coverageRate,
                CyclomaticComplexity = rawComplexity,
                ComplexityNorm = complexityNorm,
                RiskScore = Math.Round(riskScore, 4),
                RiskLevel = riskLevel
            });
        }

        return reports.OrderByDescending(r => r.RiskScore).ToList();
    }
}
