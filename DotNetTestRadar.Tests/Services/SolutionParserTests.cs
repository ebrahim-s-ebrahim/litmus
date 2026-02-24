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
    public void Parse_ValidSln_ExtractsCsprojPaths()
    {
        // Arrange
        var slnPath = "/repo/MyApp.sln";
        _fileSystem.FileExists(slnPath).Returns(true);
        _fileSystem.ReadAllText(slnPath).Returns(TestFixtures.ValidSlnContent);
        _processRunner.Run("git", "rev-parse --show-toplevel", Arg.Any<string>()).Returns("/repo");

        // Act
        var result = _sut.Parse(slnPath);

        // Assert
        result.ProjectDirectories.Should().HaveCount(2);
        result.ProjectDirectories.Should().Contain(d => d.Contains("MyApp"));
        result.ProjectDirectories.Should().Contain(d => d.Contains("MyApp.Tests"));
    }

    [Fact]
    public void Parse_ValidSlnx_ExtractsCsprojPaths()
    {
        // Arrange
        var slnxPath = "/repo/MyApp.slnx";
        _fileSystem.FileExists(slnxPath).Returns(true);
        _fileSystem.ReadAllText(slnxPath).Returns(TestFixtures.ValidSlnxContent);
        _processRunner.Run("git", "rev-parse --show-toplevel", Arg.Any<string>()).Returns("/repo");

        // Act
        var result = _sut.Parse(slnxPath);

        // Assert
        result.ProjectDirectories.Should().HaveCount(2);
        result.ProjectDirectories.Should().Contain(d => d.Contains("MyApp"));
        result.ProjectDirectories.Should().Contain(d => d.Contains("MyApp.Tests"));
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

        // Assert
        // "Solution Items" folder should not appear — only 2 real project dirs
        result.ProjectDirectories.Should().HaveCount(2);
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
}
