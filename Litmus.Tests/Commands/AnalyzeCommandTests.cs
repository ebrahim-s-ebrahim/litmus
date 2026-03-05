using System.CommandLine;
using Litmus.Abstractions;
using Litmus.Commands;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Spectre.Console;

namespace Litmus.Tests.Commands;

public class AnalyzeCommandTests
{
    private readonly IFileSystem _fileSystem = Substitute.For<IFileSystem>();
    private readonly IProcessRunner _processRunner = Substitute.For<IProcessRunner>();

    public AnalyzeCommandTests()
    {
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(TextWriter.Null)
        });
    }

    private int Invoke(params string[] args)
    {
        var command = AnalyzeCommand.Create(_fileSystem, _processRunner);
        var root = new RootCommand { command };
        return root.Parse(["analyze", .. args]).Invoke();
    }

    private void SetupSolutionDiscovery(string cwd, IEnumerable<string> slnFiles,
        IEnumerable<string>? slnxFiles = null)
    {
        _fileSystem.GetCurrentDirectory().Returns(cwd);
        _fileSystem.GetFiles(cwd, "*.sln", false).Returns(slnFiles);
        _fileSystem.GetFiles(cwd, "*.slnx", false).Returns(slnxFiles ?? Enumerable.Empty<string>());
    }

    // ── Validation tests ─────────────────────────────────────────────

    [Fact]
    public void VerboseAndQuiet_ReturnsError()
    {
        _fileSystem.FileExists("test.sln").Returns(true);
        _fileSystem.FileExists("coverage.xml").Returns(true);
        _processRunner.Run("git", "--version", ".").Returns("git version 2.42.0");

        var result = Invoke("--solution", "test.sln", "--coverage", "coverage.xml",
            "--verbose", "--quiet");

        result.Should().Be(1);
    }

    [Fact]
    public void SolutionNotFound_ReturnsError()
    {
        _fileSystem.FileExists("missing.sln").Returns(false);

        var result = Invoke("--solution", "missing.sln", "--coverage", "coverage.xml");

        result.Should().Be(1);
    }

    [Fact]
    public void InvalidSolutionExtension_ReturnsError()
    {
        _fileSystem.FileExists("test.txt").Returns(true);

        var result = Invoke("--solution", "test.txt", "--coverage", "coverage.xml");

        result.Should().Be(1);
    }

    [Fact]
    public void CoverageFileNotFound_ReturnsError()
    {
        _fileSystem.FileExists("test.sln").Returns(true);
        _fileSystem.FileExists("missing.xml").Returns(false);
        _processRunner.Run("git", "--version", ".").Returns("git version 2.42.0");

        var result = Invoke("--solution", "test.sln", "--coverage", "missing.xml");

        result.Should().Be(1);
    }

    [Fact]
    public void GitNotAvailable_ReturnsError()
    {
        _fileSystem.FileExists("test.sln").Returns(true);
        _fileSystem.FileExists("coverage.xml").Returns(true);
        _processRunner.Run("git", "--version", ".")
            .Throws(new InvalidOperationException("not found"));

        var result = Invoke("--solution", "test.sln", "--coverage", "coverage.xml");

        result.Should().Be(1);
    }

    // ── Solution auto-discovery tests ────────────────────────────────

    [Fact]
    public void NoSolutionArg_SingleSlnInCwd_IsAutoDetected()
    {
        const string fakeCwd = "/fake/cwd";
        const string fakeSln = "/fake/cwd/MyApp.sln";
        SetupSolutionDiscovery(fakeCwd, [fakeSln]);
        _fileSystem.FileExists(fakeSln).Returns(true);
        _fileSystem.FileExists("coverage.xml").Returns(true);
        _processRunner.Run("git", "--version", ".").Returns("git version 2.42.0");

        // Will fail deeper in RunAnalysis (no git repo), but passes solution validation
        var result = Invoke("--coverage", "coverage.xml");

        // Solution was found and validated, failure is from git/analysis pipeline
        result.Should().Be(1);
        _fileSystem.Received().FileExists(fakeSln);
    }

    [Fact]
    public void NoSolutionArg_NoSlnInCwd_ReturnsError()
    {
        SetupSolutionDiscovery("/fake/cwd", []);

        var result = Invoke("--coverage", "coverage.xml");

        result.Should().Be(1);
    }

    [Fact]
    public void NoSolutionArg_MultipleSlnInCwd_ReturnsError()
    {
        SetupSolutionDiscovery("/fake/cwd",
            ["/fake/cwd/AppA.sln", "/fake/cwd/AppB.sln"]);

        var result = Invoke("--coverage", "coverage.xml");

        result.Should().Be(1);
    }

    [Fact]
    public void NoSolutionArg_SingleSlnxInCwd_IsAutoDetected()
    {
        const string fakeCwd = "/fake/cwd";
        const string fakeSlnx = "/fake/cwd/MyApp.slnx";
        SetupSolutionDiscovery(fakeCwd, [], [fakeSlnx]);
        _fileSystem.FileExists(fakeSlnx).Returns(true);
        _fileSystem.FileExists("coverage.xml").Returns(true);
        _processRunner.Run("git", "--version", ".").Returns("git version 2.42.0");

        Invoke("--coverage", "coverage.xml");

        _fileSystem.Received().FileExists(fakeSlnx);
    }

    [Fact]
    public void NoSolutionArg_MixedSlnAndSlnx_ReturnsError()
    {
        SetupSolutionDiscovery("/fake/cwd",
            ["/fake/cwd/AppA.sln"],
            ["/fake/cwd/AppB.slnx"]);

        var result = Invoke("--coverage", "coverage.xml");

        result.Should().Be(1);
    }
}
