using System.Text.Json;
using DotNetTestRadar.Abstractions;
using DotNetTestRadar.Commands;
using DotNetTestRadar.Models;
using DotNetTestRadar.Services;
using FluentAssertions;

namespace DotNetTestRadar.Tests.Integration;

public class AnalyzeCommandIntegrationTests : IDisposable
{
    private readonly IntegrationTestFixture _fixture;
    private readonly IFileSystem _fileSystem;
    private readonly IProcessRunner _processRunner;

    public AnalyzeCommandIntegrationTests()
    {
        _fixture = new IntegrationTestFixture();
        _fileSystem = new FileSystemWrapper();
        _processRunner = new ProcessRunner();
    }

    public void Dispose() => _fixture.Dispose();

    private int RunAnalysis(AnalysisOptions? overrides = null)
    {
        var options = overrides ?? new AnalysisOptions
        {
            SolutionPath = _fixture.SolutionPath,
            CoveragePath = _fixture.CoveragePath,
            Since = DateTime.Today.AddYears(-1),
            Top = 20,
            NoColor = true
        };

        var coverageParser = new CoverageParser(_fileSystem);
        var coverageResult = coverageParser.Parse(options.CoveragePath);

        return AnalyzeCommand.RunAnalysis(options, coverageResult, _fileSystem, _processRunner);
    }

    private List<JsonElement> RunAndReadJsonResults(AnalysisOptions? overrides = null)
    {
        var outputPath = Path.Combine(_fixture.RootDir, $"results-{Guid.NewGuid():N}.json");
        var options = overrides ?? new AnalysisOptions
        {
            SolutionPath = _fixture.SolutionPath,
            CoveragePath = _fixture.CoveragePath,
            Since = DateTime.Today.AddYears(-1),
            Top = 20,
            NoColor = true
        };
        options.OutputPath = outputPath;

        var coverageParser = new CoverageParser(_fileSystem);
        var coverageResult = coverageParser.Parse(options.CoveragePath);
        var exitCode = AnalyzeCommand.RunAnalysis(options, coverageResult, _fileSystem, _processRunner);

        exitCode.Should().Be(0, "the analysis pipeline should succeed");

        var json = File.ReadAllText(outputPath);
        return JsonSerializer.Deserialize<List<JsonElement>>(json)!;
    }

    [Fact]
    public void HappyPath_ProducesRankedResults()
    {
        var results = RunAndReadJsonResults();

        // Should have exactly 3 source files (test project filtered out)
        results.Should().HaveCount(3);

        // OrderService has the highest RiskScore (most churn + lowest coverage + highest complexity).
        // However, its high dependency score (DateTime.Now, new HttpClient) lowers its StartingPriority,
        // so it may not rank #1 by priority — that's correct tool behavior.
        var orderService = results.First(r => r.GetProperty("file").GetString()!.Contains("OrderService"));
        var otherRiskScores = results
            .Where(r => !r.GetProperty("file").GetString()!.Contains("OrderService"))
            .Select(r => r.GetProperty("riskScore").GetDouble());

        orderService.GetProperty("riskScore").GetDouble()
            .Should().BeGreaterThanOrEqualTo(otherRiskScores.Max(),
                "OrderService has highest churn, lowest coverage, and highest complexity");
    }

    [Fact]
    public void RiskScores_AreNonZero_ForFilesWithChurn()
    {
        var results = RunAndReadJsonResults();

        // All files were committed, so all should have some churn
        results.Should().OnlyContain(r => r.GetProperty("riskScore").GetDouble() >= 0);

        // Files with git history should have risk > 0 (they have churn and < 100% coverage)
        var orderService = results.First(r => r.GetProperty("file").GetString()!.Contains("OrderService"));
        orderService.GetProperty("riskScore").GetDouble().Should().BeGreaterThan(0);

        // SimpleService has 90% coverage — its risk should be lower than OrderService (10% coverage)
        var simpleService = results.First(r => r.GetProperty("file").GetString()!.Contains("SimpleService"));
        simpleService.GetProperty("riskScore").GetDouble()
            .Should().BeLessThan(orderService.GetProperty("riskScore").GetDouble());
    }

    [Fact]
    public void DependencySignals_DetectedCorrectly()
    {
        var results = RunAndReadJsonResults();

        // OrderService uses DateTime.Now and new HttpClient() — infrastructure calls
        var orderService = results.First(r => r.GetProperty("file").GetString()!.Contains("OrderService"));
        orderService.GetProperty("infrastructureCalls").GetInt32().Should().BeGreaterThan(0);

        // DataAccess uses new SqlConnection() — counted as infrastructure call (Signal 1)
        var dataAccess = results.First(r => r.GetProperty("file").GetString()!.Contains("DataAccess"));
        dataAccess.GetProperty("infrastructureCalls").GetInt32().Should().BeGreaterThan(0);

        // SimpleService has no infra calls or instantiations
        var simpleService = results.First(r => r.GetProperty("file").GetString()!.Contains("SimpleService"));
        simpleService.GetProperty("infrastructureCalls").GetInt32().Should().Be(0);
        simpleService.GetProperty("directInstantiations").GetInt32().Should().Be(0);
    }

    [Fact]
    public void ExcludePattern_FiltersFiles()
    {
        var results = RunAndReadJsonResults(new AnalysisOptions
        {
            SolutionPath = _fixture.SolutionPath,
            CoveragePath = _fixture.CoveragePath,
            Since = DateTime.Today.AddYears(-1),
            Top = 20,
            NoColor = true,
            ExcludePatterns = ["**/DataAccess*"]
        });

        results.Should().HaveCount(2);
        results.Should().NotContain(r => r.GetProperty("file").GetString()!.Contains("DataAccess"));
    }

    [Fact]
    public void JsonExport_ProducesValidOutput()
    {
        var results = RunAndReadJsonResults();

        // Verify all 16 expected fields are present on each result
        var expectedFields = new[]
        {
            "file", "commits", "weightedChurn", "coverageRate",
            "cyclomaticComplexity", "riskScore", "riskLevel",
            "infrastructureCalls", "directInstantiations", "concreteConstructorParams",
            "staticCalls", "rawDependencyScore", "dependencyNorm",
            "dependencyLevel", "startingPriority", "priorityLevel"
        };

        foreach (var result in results)
        {
            foreach (var field in expectedFields)
            {
                result.TryGetProperty(field, out _).Should().BeTrue($"field '{field}' should be present");
            }
        }
    }

    [Fact]
    public void CsvExport_ProducesValidOutput()
    {
        var outputPath = Path.Combine(_fixture.RootDir, $"results-{Guid.NewGuid():N}.csv");
        var options = new AnalysisOptions
        {
            SolutionPath = _fixture.SolutionPath,
            CoveragePath = _fixture.CoveragePath,
            Since = DateTime.Today.AddYears(-1),
            Top = 20,
            NoColor = true,
            OutputPath = outputPath
        };

        var exitCode = RunAnalysis(options);
        exitCode.Should().Be(0);

        var lines = File.ReadAllLines(outputPath);
        lines.Length.Should().BeGreaterThanOrEqualTo(4); // header + 3 data rows

        // Header should have 16 columns
        var headerColumns = lines[0].Split(',');
        headerColumns.Should().HaveCount(16);
        headerColumns[0].Should().Be("file");

        // Data rows should also have 16 columns
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var cols = lines[i].Split(',');
            cols.Should().HaveCount(16, $"data row {i} should have 16 columns");
        }
    }

    [Fact]
    public void TestProject_IsExcludedFromResults()
    {
        var results = RunAndReadJsonResults();

        results.Should().NotContain(r =>
            r.GetProperty("file").GetString()!.Contains("MyApp.Tests", StringComparison.OrdinalIgnoreCase));
        results.Should().NotContain(r =>
            r.GetProperty("file").GetString()!.Contains("SampleTest", StringComparison.OrdinalIgnoreCase));
    }
}
