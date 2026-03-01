using System.CommandLine;
using DotNetTestRadar.Abstractions;
using DotNetTestRadar.Commands;
using DotNetTestRadar.Tests.Helpers;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Spectre.Console;

namespace DotNetTestRadar.Tests.Commands;

public class ScanCommandTests
{
    private readonly IFileSystem _fileSystem = Substitute.For<IFileSystem>();
    private readonly IProcessRunner _processRunner = Substitute.For<IProcessRunner>();

    public ScanCommandTests()
    {
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(TextWriter.Null)
        });
    }

    private int Invoke(params string[] args)
    {
        var command = ScanCommand.Create(_fileSystem, _processRunner);
        var root = new RootCommand { command };
        return root.Parse(["scan", .. args]).Invoke();
    }

    private void SetupToolsAvailable()
    {
        _processRunner.Run("dotnet", "--version", ".").Returns("8.0.100");
        _processRunner.Run("git", "--version", ".").Returns("git version 2.42.0");
    }

    // ── Validation tests ────────────────────────────────────────────

    [Fact]
    public void VerboseAndQuiet_ReturnsError()
    {
        _fileSystem.FileExists("test.sln").Returns(true);
        SetupToolsAvailable();

        var result = Invoke("--solution", "test.sln", "--verbose", "--quiet");

        result.Should().Be(1);
    }

    [Fact]
    public void SolutionNotFound_ReturnsError()
    {
        _fileSystem.FileExists("missing.sln").Returns(false);

        var result = Invoke("--solution", "missing.sln");

        result.Should().Be(1);
    }

    [Fact]
    public void InvalidSolutionExtension_ReturnsError()
    {
        _fileSystem.FileExists("test.txt").Returns(true);

        var result = Invoke("--solution", "test.txt");

        result.Should().Be(1);
    }

    [Fact]
    public void SlnExtension_IsAccepted()
    {
        _fileSystem.FileExists("test.sln").Returns(true);
        SetupToolsAvailable();
        _fileSystem.GetFiles(Arg.Any<string>(), "coverage.cobertura.xml", true)
            .Returns(Enumerable.Empty<string>());

        Invoke("--solution", "test.sln");

        // Verify we got past validation and actually ran dotnet test
        _processRunner.Received().Run("dotnet",
            Arg.Is<string>(a => a.StartsWith("test ")),
            Arg.Any<string>());
    }

    [Fact]
    public void SlnxExtension_IsAccepted()
    {
        _fileSystem.FileExists("test.slnx").Returns(true);
        SetupToolsAvailable();
        _fileSystem.GetFiles(Arg.Any<string>(), "coverage.cobertura.xml", true)
            .Returns(Enumerable.Empty<string>());

        Invoke("--solution", "test.slnx");

        _processRunner.Received().Run("dotnet",
            Arg.Is<string>(a => a.StartsWith("test ")),
            Arg.Any<string>());
    }

    [Fact]
    public void TestsDirNotFound_ReturnsError()
    {
        _fileSystem.FileExists("test.sln").Returns(true);
        _fileSystem.FileExists("missing-dir").Returns(false);
        _fileSystem.DirectoryExists("missing-dir").Returns(false);

        var result = Invoke("--solution", "test.sln", "--tests-dir", "missing-dir");

        result.Should().Be(1);
    }

    [Fact]
    public void TestsDirAsFile_IsAccepted()
    {
        _fileSystem.FileExists("test.sln").Returns(true);
        _fileSystem.FileExists("tests/MyTests.csproj").Returns(true);
        SetupToolsAvailable();
        _fileSystem.GetFiles(Arg.Any<string>(), "coverage.cobertura.xml", true)
            .Returns(Enumerable.Empty<string>());

        Invoke("--solution", "test.sln", "--tests-dir", "tests/MyTests.csproj");

        _processRunner.Received().Run("dotnet",
            Arg.Is<string>(a => a.Contains("MyTests.csproj")),
            Arg.Any<string>());
    }

    [Fact]
    public void TestsDirAsDirectory_IsAccepted()
    {
        _fileSystem.FileExists("test.sln").Returns(true);
        _fileSystem.DirectoryExists("tests/UnitTests").Returns(true);
        SetupToolsAvailable();
        _fileSystem.GetFiles(Arg.Any<string>(), "coverage.cobertura.xml", true)
            .Returns(Enumerable.Empty<string>());

        Invoke("--solution", "test.sln", "--tests-dir", "tests/UnitTests");

        _processRunner.Received().Run("dotnet",
            Arg.Is<string>(a => a.Contains("tests/UnitTests")),
            Arg.Any<string>());
    }

    [Fact]
    public void BaselineFileNotFound_ReturnsError()
    {
        _fileSystem.FileExists("test.sln").Returns(true);
        _fileSystem.FileExists("missing.json").Returns(false);
        SetupToolsAvailable();

        var result = Invoke("--solution", "test.sln", "--baseline", "missing.json");

        result.Should().Be(1);
    }

    [Fact]
    public void DotnetNotAvailable_ReturnsError()
    {
        _fileSystem.FileExists("test.sln").Returns(true);
        _processRunner.Run("dotnet", "--version", ".")
            .Throws(new InvalidOperationException("not found"));

        var result = Invoke("--solution", "test.sln");

        result.Should().Be(1);
    }

    [Fact]
    public void GitNotAvailable_ReturnsError()
    {
        _fileSystem.FileExists("test.sln").Returns(true);
        _processRunner.Run("dotnet", "--version", ".").Returns("8.0.100");
        _processRunner.Run("git", "--version", ".")
            .Throws(new InvalidOperationException("not found"));

        var result = Invoke("--solution", "test.sln");

        result.Should().Be(1);
    }

    // ── Path handling tests (trailing separator bug fix) ────────────

    [Fact]
    public void TestsDir_TrailingBackslash_IsTrimmedInDotnetTestArgs()
    {
        // Backslash is only a directory separator on Windows; on Linux/macOS
        // it's a valid filename character and TrimEnd correctly ignores it.
        if (Path.DirectorySeparatorChar != '\\')
            return;

        _fileSystem.FileExists("test.sln").Returns(true);
        _fileSystem.DirectoryExists(@"tests\UnitTests\").Returns(true);
        SetupToolsAvailable();
        _fileSystem.GetFiles(Arg.Any<string>(), "coverage.cobertura.xml", true)
            .Returns(Enumerable.Empty<string>());

        Invoke("--solution", "test.sln", "--tests-dir", @"tests\UnitTests\");

        _processRunner.Received().Run("dotnet",
            Arg.Is<string>(args => ExtractQuotedPath(args, "test ").EndsWith("UnitTests")),
            Arg.Any<string>());
    }

    [Fact]
    public void TestsDir_TrailingForwardSlash_IsTrimmedInDotnetTestArgs()
    {
        _fileSystem.FileExists("test.sln").Returns(true);
        _fileSystem.DirectoryExists("tests/UnitTests/").Returns(true);
        SetupToolsAvailable();
        _fileSystem.GetFiles(Arg.Any<string>(), "coverage.cobertura.xml", true)
            .Returns(Enumerable.Empty<string>());

        Invoke("--solution", "test.sln", "--tests-dir", "tests/UnitTests/");

        _processRunner.Received().Run("dotnet",
            Arg.Is<string>(args => ExtractQuotedPath(args, "test ").EndsWith("UnitTests")),
            Arg.Any<string>());
    }

    [Fact]
    public void TestsDir_NoTrailingSeparator_IsUnchanged()
    {
        _fileSystem.FileExists("test.sln").Returns(true);
        _fileSystem.DirectoryExists("tests/UnitTests").Returns(true);
        SetupToolsAvailable();
        _fileSystem.GetFiles(Arg.Any<string>(), "coverage.cobertura.xml", true)
            .Returns(Enumerable.Empty<string>());

        Invoke("--solution", "test.sln", "--tests-dir", "tests/UnitTests");

        _processRunner.Received().Run("dotnet",
            Arg.Is<string>(args => ExtractQuotedPath(args, "test ").EndsWith("UnitTests")),
            Arg.Any<string>());
    }

    // ── Test execution and coverage tests ───────────────────────────

    [Fact]
    public void WithoutTestsDir_UsesSolutionAsTestTarget()
    {
        _fileSystem.FileExists("test.sln").Returns(true);
        SetupToolsAvailable();
        _fileSystem.GetFiles(Arg.Any<string>(), "coverage.cobertura.xml", true)
            .Returns(Enumerable.Empty<string>());

        Invoke("--solution", "test.sln");

        _processRunner.Received().Run("dotnet",
            Arg.Is<string>(a => a.Contains("test.sln")),
            Arg.Any<string>());
    }

    [Fact]
    public void DotnetTest_IncludesXPlatCodeCoverage()
    {
        _fileSystem.FileExists("test.sln").Returns(true);
        SetupToolsAvailable();
        _fileSystem.GetFiles(Arg.Any<string>(), "coverage.cobertura.xml", true)
            .Returns(Enumerable.Empty<string>());

        Invoke("--solution", "test.sln");

        _processRunner.Received().Run("dotnet",
            Arg.Is<string>(a => a.Contains("XPlat Code Coverage")),
            Arg.Any<string>());
    }

    [Fact]
    public void DotnetTest_IncludesResultsDirectory()
    {
        _fileSystem.FileExists("test.sln").Returns(true);
        SetupToolsAvailable();
        _fileSystem.GetFiles(Arg.Any<string>(), "coverage.cobertura.xml", true)
            .Returns(Enumerable.Empty<string>());

        Invoke("--solution", "test.sln");

        _processRunner.Received().Run("dotnet",
            Arg.Is<string>(a => a.Contains("--results-directory")),
            Arg.Any<string>());
    }

    [Fact]
    public void NoCoverageFiles_ReturnsError()
    {
        _fileSystem.FileExists("test.sln").Returns(true);
        SetupToolsAvailable();
        _fileSystem.GetFiles(Arg.Any<string>(), "coverage.cobertura.xml", true)
            .Returns(Enumerable.Empty<string>());

        var result = Invoke("--solution", "test.sln");

        result.Should().Be(1);
    }

    [Fact]
    public void TestsFail_NoCoverageFiles_ReturnsError()
    {
        _fileSystem.FileExists("test.sln").Returns(true);
        SetupToolsAvailable();
        _processRunner.Run("dotnet",
            Arg.Is<string>(s => s.StartsWith("test ")),
            Arg.Any<string>())
            .Throws(new InvalidOperationException("Build failed: error CS1234"));
        _fileSystem.GetFiles(Arg.Any<string>(), "coverage.cobertura.xml", true)
            .Returns(Enumerable.Empty<string>());

        var result = Invoke("--solution", "test.sln");

        result.Should().Be(1);
    }

    [Fact]
    public void TestsFail_WithCoverageFiles_ContinuesToParseCoverage()
    {
        _fileSystem.FileExists("test.sln").Returns(true);
        SetupToolsAvailable();
        _processRunner.Run("dotnet",
            Arg.Is<string>(s => s.StartsWith("test ")),
            Arg.Any<string>())
            .Throws(new InvalidOperationException("Some tests failed"));
        _fileSystem.GetFiles(Arg.Any<string>(), "coverage.cobertura.xml", true)
            .Returns(new[] { "coverage.cobertura.xml" });
        _fileSystem.ReadAllText("coverage.cobertura.xml")
            .Returns(TestFixtures.ValidCoberturaXml);

        // Will fail deeper in RunAnalysis, but we verify execution continued past
        // the "no coverage files" checkpoint
        Invoke("--solution", "test.sln");

        _fileSystem.Received().ReadAllText("coverage.cobertura.xml");
    }

    [Fact]
    public void MultipleCoverageFiles_AllAreParsed()
    {
        _fileSystem.FileExists("test.sln").Returns(true);
        SetupToolsAvailable();
        _fileSystem.GetFiles(Arg.Any<string>(), "coverage.cobertura.xml", true)
            .Returns(new[] { "proj1/coverage.cobertura.xml", "proj2/coverage.cobertura.xml" });
        _fileSystem.ReadAllText(Arg.Any<string>())
            .Returns(TestFixtures.ValidCoberturaXml);

        Invoke("--solution", "test.sln");

        _fileSystem.Received().ReadAllText("proj1/coverage.cobertura.xml");
        _fileSystem.Received().ReadAllText("proj2/coverage.cobertura.xml");
    }

    // ── Helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the first quoted value after the given prefix from an argument string.
    /// E.g., for args = <c>test "my\path" --collect:...</c> and prefix = "test ",
    /// returns "my\path".
    /// </summary>
    private static string ExtractQuotedPath(string args, string prefix)
    {
        var start = args.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
        if (args[start] == '"') start++;
        var end = args.IndexOf('"', start);
        return args[start..end];
    }
}
