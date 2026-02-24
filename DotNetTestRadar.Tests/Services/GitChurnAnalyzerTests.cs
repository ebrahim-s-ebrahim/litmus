using DotNetTestRadar.Abstractions;
using DotNetTestRadar.Services;
using DotNetTestRadar.Tests.Helpers;
using FluentAssertions;
using NSubstitute;

namespace DotNetTestRadar.Tests.Services;

public class GitChurnAnalyzerTests
{
    private readonly IProcessRunner _processRunner = Substitute.For<IProcessRunner>();
    private readonly GitChurnAnalyzer _sut;

    private const string GitRoot = "/repo";
    private const string SolutionDir = "/repo/backend";
    private static readonly List<string> ProjectDirs = ["backend/MyApp"];
    private static readonly DateTime Since = new(2024, 1, 1);

    public GitChurnAnalyzerTests()
    {
        _sut = new GitChurnAnalyzer(_processRunner);
    }

    private void SetupGitOutput(string output)
    {
        _processRunner.Run("git", Arg.Any<string>(), Arg.Any<string>()).Returns(output);
    }

    [Fact]
    public void Analyze_EmptyGitOutput_ReturnsEmptyResult()
    {
        SetupGitOutput("");

        var result = _sut.Analyze(GitRoot, SolutionDir, ProjectDirs, Since, []);

        result.Files.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_ValidOutput_SumsWeightedChurnPerFile()
    {
        SetupGitOutput(TestFixtures.GitNumstatOutput);

        var result = _sut.Analyze(GitRoot, SolutionDir, ProjectDirs, Since, []);

        // UserService: (10+5) + (30+15) + (8+4) = 72 (noise floor filters 1+1=2 for User.cs)
        var userService = result.Files.Values.FirstOrDefault(f =>
            result.Files.First(kv => kv.Value == f).Key.Contains("UserService"));
        userService.Should().NotBeNull();
        userService!.WeightedChurn.Should().Be(72);
    }

    [Fact]
    public void Analyze_NoiseFloor_IgnoresSmallCommits()
    {
        SetupGitOutput(TestFixtures.GitNumstatOutput);

        var result = _sut.Analyze(GitRoot, SolutionDir, ProjectDirs, Since, []);

        // User.cs has only 1+1=2 which is <= 2 noise threshold, so should be absent
        result.Files.Keys.Should().NotContain(k => k.Contains("User.cs"));
    }

    [Fact]
    public void Analyze_LineWeightedOverCommitCount_LargeCommitsScoreHigher()
    {
        // 3 large commits
        var largeCommits = """
            100	50	backend/MyApp/FileA.cs

            100	50	backend/MyApp/FileA.cs

            100	50	backend/MyApp/FileA.cs
            """;

        // 50 single-line commits (each 3 lines to pass noise floor)
        var manySmallCommits = string.Join("\n",
            Enumerable.Range(0, 50).Select(_ => "2\t1\tbackend/MyApp/FileB.cs"));

        SetupGitOutput(largeCommits + "\n" + manySmallCommits);

        var result = _sut.Analyze(GitRoot, SolutionDir, ProjectDirs, Since, []);

        var fileAChurn = result.Files.First(kv => kv.Key.Contains("FileA")).Value.WeightedChurn;
        var fileBChurn = result.Files.First(kv => kv.Key.Contains("FileB")).Value.WeightedChurn;

        fileAChurn.Should().BeGreaterThan(fileBChurn);
    }

    [Fact]
    public void Analyze_NormalizesChurn_HighestFileScoresOne()
    {
        SetupGitOutput(TestFixtures.GitNumstatOutput);

        var result = _sut.Analyze(GitRoot, SolutionDir, ProjectDirs, Since, []);

        result.Files.Values.Max(f => f.ChurnNorm).Should().Be(1.0);
    }

    [Fact]
    public void Analyze_FileNotInGitOutput_GetsZeroChurn()
    {
        SetupGitOutput(TestFixtures.GitNumstatOutput);

        var result = _sut.Analyze(GitRoot, SolutionDir, ProjectDirs, Since, []);

        // A file not mentioned in git output should not be in the result at all
        result.Files.Should().NotContainKey("MyApp/Services/NonExistent.cs");
    }

    [Fact]
    public void Analyze_ExcludesDefaultPatterns()
    {
        var output = """
            10	5	backend/MyApp/Services/UserService.cs
            10	5	backend/MyApp/Services/UserService.Designer.cs
            10	5	backend/MyApp/Migrations/Init.cs
            """;
        SetupGitOutput(output);

        var result = _sut.Analyze(GitRoot, SolutionDir, ProjectDirs, Since, []);

        result.Files.Keys.Should().NotContain(k => k.Contains("Designer.cs"));
        result.Files.Keys.Should().NotContain(k => k.Contains("Migrations"));
    }

    [Fact]
    public void Analyze_HandlesFilesWithSpaces()
    {
        SetupGitOutput(TestFixtures.GitNumstatWithSpaces);

        var result = _sut.Analyze(GitRoot, SolutionDir, ProjectDirs, Since, []);

        result.Files.Should().ContainKey("MyApp/Services/My Service.cs");
    }

    [Fact]
    public void Analyze_SkipsBinaryFiles()
    {
        SetupGitOutput(TestFixtures.GitNumstatWithBinaryFiles);

        var result = _sut.Analyze(GitRoot, SolutionDir, ProjectDirs, Since, []);

        result.Files.Keys.Should().NotContain(k => k.Contains("logo.png"));
        result.Files.Should().HaveCount(2);
    }

    [Fact]
    public void Analyze_PathsRelativeToSolutionDirectory()
    {
        SetupGitOutput(TestFixtures.GitNumstatOutput);

        var result = _sut.Analyze(GitRoot, SolutionDir, ProjectDirs, Since, []);

        // Paths should be relative to SolutionDir (/repo/backend), not GitRoot (/repo)
        result.Files.Keys.Should().AllSatisfy(k => k.Should().NotStartWith("backend/"));
    }
}
