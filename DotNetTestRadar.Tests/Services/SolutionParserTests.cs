using DotNetTestRadar.Abstractions;
using DotNetTestRadar.Services;
using DotNetTestRadar.Tests.Helpers;
using FluentAssertions;
using NSubstitute;

namespace DotNetTestRadar.Tests.Services;

public class SolutionParserTests
{
    private readonly IFileSystem _fileSystem = Substitute.For<IFileSystem>();
    private readonly IProcessRunner _processRunner = Substitute.For<IProcessRunner>();
    private readonly SolutionParser _sut;

    public SolutionParserTests()
    {
        _sut = new SolutionParser(_fileSystem, _processRunner);
    }

    [Fact]
    public void Parse_ValidSln_ExtractsOnlySourceProjects()
    {
        // Arrange
        var slnPath = "/repo/MyApp.sln";
        _fileSystem.FileExists(slnPath).Returns(true);
        _fileSystem.ReadAllText(slnPath).Returns(TestFixtures.ValidSlnContent);
        _processRunner.Run("git", "rev-parse --show-toplevel", Arg.Any<string>()).Returns("/repo");
        SetupTestProjectCsproj("/repo");

        // Act
        var result = _sut.Parse(slnPath);

        // Assert — test project is filtered out
        result.ProjectDirectories.Should().HaveCount(1);
        result.ProjectDirectories.Should().Contain(d => d.Contains("MyApp"));
        result.ProjectDirectories.Should().NotContain(d => d.Contains("MyApp.Tests"));
    }

    [Fact]
    public void Parse_ValidSlnx_ExtractsOnlySourceProjects()
    {
        // Arrange
        var slnxPath = "/repo/MyApp.slnx";
        _fileSystem.FileExists(slnxPath).Returns(true);
        _fileSystem.ReadAllText(slnxPath).Returns(TestFixtures.ValidSlnxContent);
        _processRunner.Run("git", "rev-parse --show-toplevel", Arg.Any<string>()).Returns("/repo");
        SetupTestProjectCsproj("/repo");

        // Act
        var result = _sut.Parse(slnxPath);

        // Assert — test project is filtered out
        result.ProjectDirectories.Should().HaveCount(1);
        result.ProjectDirectories.Should().Contain(d => d.Contains("MyApp"));
        result.ProjectDirectories.Should().NotContain(d => d.Contains("MyApp.Tests"));
    }

    [Fact]
    public void Parse_Sln_IncludesTestProjectWhenNoCsprojFileFound()
    {
        // When the .csproj file can't be read, the project is NOT treated as a test project
        var slnPath = "/repo/MyApp.sln";
        _fileSystem.FileExists(slnPath).Returns(true);
        _fileSystem.ReadAllText(slnPath).Returns(TestFixtures.ValidSlnContent);
        _processRunner.Run("git", "rev-parse --show-toplevel", Arg.Any<string>()).Returns("/repo");
        // Don't set up .csproj files — FileExists defaults to false

        var result = _sut.Parse(slnPath);

        result.ProjectDirectories.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_Sln_IgnoresSolutionFolderEntries()
    {
        // Arrange
        var slnPath = "/repo/MyApp.sln";
        _fileSystem.FileExists(slnPath).Returns(true);
        _fileSystem.ReadAllText(slnPath).Returns(TestFixtures.ValidSlnContent);
        _processRunner.Run("git", "rev-parse --show-toplevel", Arg.Any<string>()).Returns("/repo");

        // Act
        var result = _sut.Parse(slnPath);

        // Assert — "Solution Items" folder should not appear
        result.ProjectDirectories.Should().NotContain(d => d.Contains("Solution Items"));
    }

    [Fact]
    public void Parse_Slnx_IgnoresFolderElements()
    {
        // Arrange
        var slnxPath = "/repo/MyApp.slnx";
        _fileSystem.FileExists(slnxPath).Returns(true);
        _fileSystem.ReadAllText(slnxPath).Returns(TestFixtures.ValidSlnxContent);
        _processRunner.Run("git", "rev-parse --show-toplevel", Arg.Any<string>()).Returns("/repo");

        // Act
        var result = _sut.Parse(slnxPath);

        // Assert
        result.ProjectDirectories.Should().NotContain(d => d.Contains("Solution Items"));
        result.ProjectDirectories.Should().NotContain(d => d.Contains("README"));
    }

    [Fact]
    public void Parse_Sln_HandlesWindowsAndUnixPaths()
    {
        // The ValidSlnContent uses backslashes (Windows-style) which should be normalized
        var slnPath = "/repo/MyApp.sln";
        _fileSystem.FileExists(slnPath).Returns(true);
        _fileSystem.ReadAllText(slnPath).Returns(TestFixtures.ValidSlnContent);
        _processRunner.Run("git", "rev-parse --show-toplevel", Arg.Any<string>()).Returns("/repo");

        var result = _sut.Parse(slnPath);

        // Project directories should use forward slashes in the output
        result.ProjectDirectories.Should().AllSatisfy(d => d.Should().NotContain("\\"));
    }

    [Fact]
    public void Parse_Slnx_HandlesForwardSlashPaths()
    {
        var slnxPath = "/repo/MyApp.slnx";
        _fileSystem.FileExists(slnxPath).Returns(true);
        _fileSystem.ReadAllText(slnxPath).Returns(TestFixtures.ValidSlnxContent);
        _processRunner.Run("git", "rev-parse --show-toplevel", Arg.Any<string>()).Returns("/repo");

        var result = _sut.Parse(slnxPath);

        result.ProjectDirectories.Should().AllSatisfy(d => d.Should().NotContain("\\"));
    }

    [Fact]
    public void Parse_SingleProjectSln_ReturnsSingleDirectory()
    {
        var slnPath = "/repo/MyApp.sln";
        _fileSystem.FileExists(slnPath).Returns(true);
        _fileSystem.ReadAllText(slnPath).Returns(TestFixtures.SlnWithSingleProject);
        _processRunner.Run("git", "rev-parse --show-toplevel", Arg.Any<string>()).Returns("/repo");

        var result = _sut.Parse(slnPath);

        result.ProjectDirectories.Should().HaveCount(1);
    }

    [Fact]
    public void Parse_Sln_ExcludesProjectWithIsTestProjectTrue()
    {
        var slnPath = "/repo/MyApp.sln";
        _fileSystem.FileExists(slnPath).Returns(true);
        _fileSystem.ReadAllText(slnPath).Returns(TestFixtures.ValidSlnContent);
        _processRunner.Run("git", "rev-parse --show-toplevel", Arg.Any<string>()).Returns("/repo");

        // MyApp.Tests.csproj has <IsTestProject>true</IsTestProject>
        SetupCsproj("/repo", "backend/MyApp.Tests/MyApp.Tests.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>");

        var result = _sut.Parse(slnPath);

        result.ProjectDirectories.Should().NotContain(d => d.Contains("MyApp.Tests"));
        result.ProjectDirectories.Should().Contain(d => d.Contains("MyApp"));
    }

    [Fact]
    public void Parse_EmptySln_ThrowsClearException()
    {
        var slnPath = "/repo/MyApp.sln";
        _fileSystem.FileExists(slnPath).Returns(true);
        _fileSystem.ReadAllText(slnPath).Returns(TestFixtures.EmptySlnContent);
        _processRunner.Run("git", "rev-parse --show-toplevel", Arg.Any<string>()).Returns("/repo");

        var act = () => _sut.Parse(slnPath);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No .csproj*");
    }

    [Fact]
    public void Parse_InvalidXmlSlnx_ThrowsClearException()
    {
        var slnxPath = "/repo/MyApp.slnx";
        _fileSystem.FileExists(slnxPath).Returns(true);
        _fileSystem.ReadAllText(slnxPath).Returns("this is not valid xml <><>");
        _processRunner.Run("git", "rev-parse --show-toplevel", Arg.Any<string>()).Returns("/repo");

        var act = () => _sut.Parse(slnxPath);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*invalid XML*");
    }

    [Fact]
    public void Parse_SlnxWithNoSolutionRoot_ThrowsClearException()
    {
        var slnxPath = "/repo/MyApp.slnx";
        _fileSystem.FileExists(slnxPath).Returns(true);
        _fileSystem.ReadAllText(slnxPath).Returns("<Root><Project Path=\"a.csproj\" /></Root>");
        _processRunner.Run("git", "rev-parse --show-toplevel", Arg.Any<string>()).Returns("/repo");

        var act = () => _sut.Parse(slnxPath);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*<Solution> root*");
    }

    /// <summary>
    /// Sets up the MyApp.Tests.csproj mock to contain Microsoft.NET.Test.Sdk,
    /// making it detectable as a test project.
    /// </summary>
    private void SetupTestProjectCsproj(string repoRoot)
    {
        var testCsproj = "<Project Sdk=\"Microsoft.NET.Sdk\">" +
            "<ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.*\" /></ItemGroup>" +
            "</Project>";

        // .sln uses backslash paths, .slnx uses forward slash — handle both
        SetupCsproj(repoRoot, "backend/MyApp.Tests/MyApp.Tests.csproj", testCsproj);
    }

    private void SetupTestProjectCsproxSlnxVariant(string repoRoot)
    {
        // .slnx uses forward-slash paths — set up separately if needed
        var testCsproj = "<Project Sdk=\"Microsoft.NET.Sdk\">" +
            "<ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.*\" /></ItemGroup>" +
            "</Project>";
        SetupCsproj(repoRoot, "backend/MyApp.Tests/MyApp.Tests.csproj", testCsproj);
    }

    private void SetupCsproj(string repoRoot, string relativePath, string content)
    {
        // SolutionParser resolves solutionDir via Path.GetFullPath, so on Windows
        // "/repo/MyApp.sln" becomes "C:\repo\MyApp.sln" and solutionDir = "C:\repo".
        // We must match the exact path that Path.Combine(solutionDir, NormalizePath(p)) produces.
        var solutionDir = Path.GetDirectoryName(Path.GetFullPath(
            Path.Combine(repoRoot, "Dummy.sln")))!;
        var platformPath = relativePath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(solutionDir, platformPath);
        _fileSystem.FileExists(fullPath).Returns(true);
        _fileSystem.ReadAllText(fullPath).Returns(content);
    }
}
