using DotNetTestRadar.Services;
using FluentAssertions;

namespace DotNetTestRadar.Tests.Services;

public class RiskScorerTests
{
    private readonly RiskScorer _sut = new();

    private static ChurnResult MakeChurn(string file, int weightedChurn, double churnNorm, int commits = 1)
    {
        return new ChurnResult
        {
            Files = new Dictionary<string, FileChurnData>
            {
                [file] = new FileChurnData
                {
                    WeightedChurn = weightedChurn,
                    ChurnNorm = churnNorm,
                    Commits = commits
                }
            }
        };
    }

    private static CoverageResult MakeCoverage(string file, double rate)
    {
        return new CoverageResult
        {
            FileCoverage = new Dictionary<string, double>
            {
                [file] = rate
            }
        };
    }

    private static ComplexityResult MakeComplexity(string gitRootRelFile, int complexity, double norm)
    {
        return new ComplexityResult
        {
            FileComplexity = new Dictionary<string, int> { [gitRootRelFile] = complexity },
            FileComplexityNorm = new Dictionary<string, double> { [gitRootRelFile] = norm }
        };
    }

    [Fact]
    public void Score_ZeroChurn_RiskScoreIsZero()
    {
        var churn = new ChurnResult(); // file not in churn = 0 churn
        var coverage = MakeCoverage("MyApp/File.cs", 0.0);
        var complexity = MakeComplexity("MyApp/File.cs", 10, 1.0);

        var reports = _sut.Score(churn, coverage, complexity, "/repo", "/repo");

        reports.Should().AllSatisfy(r => r.RiskScore.Should().Be(0));
    }

    [Fact]
    public void Score_FullCoverage_RiskScoreIsZero()
    {
        var churn = MakeChurn("MyApp/File.cs", 100, 1.0);
        var coverage = MakeCoverage("MyApp/File.cs", 1.0);
        var complexity = MakeComplexity("MyApp/File.cs", 50, 1.0);

        var reports = _sut.Score(churn, coverage, complexity, "/repo", "/repo");

        var file = reports.First(r => r.File == "MyApp/File.cs");
        file.RiskScore.Should().Be(0);
    }

    [Fact]
    public void Score_HighChurnLowCoverageHighComplexity_ScoresHigherThanWithZeroComplexity()
    {
        // High complexity case
        var churn1 = MakeChurn("MyApp/FileA.cs", 100, 1.0);
        var coverage1 = MakeCoverage("MyApp/FileA.cs", 0.1);
        var complexity1 = MakeComplexity("MyApp/FileA.cs", 50, 1.0);

        var reports1 = _sut.Score(churn1, coverage1, complexity1, "/repo", "/repo");
        var scoreWithComplexity = reports1.First(r => r.File == "MyApp/FileA.cs").RiskScore;

        // Zero complexity case
        var churn2 = MakeChurn("MyApp/FileB.cs", 100, 1.0);
        var coverage2 = MakeCoverage("MyApp/FileB.cs", 0.1);
        var complexity2 = MakeComplexity("MyApp/FileB.cs", 0, 0.0);

        var reports2 = _sut.Score(churn2, coverage2, complexity2, "/repo", "/repo");
        var scoreWithoutComplexity = reports2.First(r => r.File == "MyApp/FileB.cs").RiskScore;

        scoreWithComplexity.Should().BeGreaterThan(scoreWithoutComplexity);
    }

    [Theory]
    [InlineData(1.0, 0.0, 1.0, "High")]   // RiskScore = 1.0 * (1-0) * (1+1) = 2.0
    [InlineData(0.6, 0.0, 0.0, "High")]   // RiskScore = 0.6 * 1.0 * 1.0 = 0.6
    [InlineData(0.3, 0.0, 0.0, "Medium")] // RiskScore = 0.3 * 1.0 * 1.0 = 0.3
    [InlineData(0.2, 0.0, 0.0, "Medium")] // RiskScore = 0.2 * 1.0 * 1.0 = 0.2
    [InlineData(0.1, 0.0, 0.0, "Low")]    // RiskScore = 0.1 * 1.0 * 1.0 = 0.1
    public void Score_ClassifiesCorrectlyAtBoundaries(double churnNorm, double coverageRate, double complexityNorm, string expectedLevel)
    {
        var churn = MakeChurn("File.cs", 100, churnNorm);
        var coverage = MakeCoverage("File.cs", coverageRate);
        var complexity = MakeComplexity("File.cs", 10, complexityNorm);

        var reports = _sut.Score(churn, coverage, complexity, "/repo", "/repo");

        var file = reports.First(r => r.File == "File.cs");
        file.RiskLevel.Should().Be(expectedLevel);
    }

    [Fact]
    public void Score_NeverExceedsTwo()
    {
        // Maximum possible: ChurnNorm=1.0, Coverage=0.0, ComplexityNorm=1.0
        // = 1.0 * (1-0) * (1+1) = 2.0
        var churn = MakeChurn("File.cs", 100, 1.0);
        var coverage = MakeCoverage("File.cs", 0.0);
        var complexity = MakeComplexity("File.cs", 50, 1.0);

        var reports = _sut.Score(churn, coverage, complexity, "/repo", "/repo");

        reports.Should().AllSatisfy(r => r.RiskScore.Should().BeLessThanOrEqualTo(2.0));
    }
}
