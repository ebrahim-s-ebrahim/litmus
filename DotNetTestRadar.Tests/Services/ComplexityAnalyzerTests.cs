using DotNetTestRadar.Abstractions;
using DotNetTestRadar.Services;
using DotNetTestRadar.Tests.Helpers;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace DotNetTestRadar.Tests.Services;

public class ComplexityAnalyzerTests
{
    private readonly IFileSystem _fileSystem = Substitute.For<IFileSystem>();
    private readonly ComplexityAnalyzer _sut;

    public ComplexityAnalyzerTests()
    {
        _sut = new ComplexityAnalyzer(_fileSystem);
    }

    [Fact]
    public void CalculateFileComplexity_NoBranches_ReturnsOne()
    {
        var complexity = ComplexityAnalyzer.CalculateFileComplexity(TestFixtures.NoBranchCode);

        complexity.Should().Be(1); // Base complexity of 1 for the single method
    }

    [Fact]
    public void CalculateFileComplexity_TwoIfStatements_ReturnsThree()
    {
        var complexity = ComplexityAnalyzer.CalculateFileComplexity(TestFixtures.SimpleComplexityCode);

        // 1 (base) + 2 (if statements) = 3
        complexity.Should().Be(3);
    }

    [Fact]
    public void CalculateFileComplexity_CountsAllBranchTypes()
    {
        var code = """
            public class Test
            {
                public void Method(int x, object obj)
                {
                    if (x > 0) { }                    // +1
                    for (int i = 0; i < x; i++) { }   // +1
                    foreach (var item in new[] {1}) { } // +1
                    while (x > 0) { x--; }             // +1
                    do { x--; } while (x > 0);         // +1
                    try { } catch (Exception) { }      // +1
                    var a = x > 0 ? 1 : 2;            // +1 (?:)
                    var b = obj ?? "default";           // +1 (??)
                    var c = true && false;              // +1 (&&)
                    var d = true || false;              // +1 (||)
                    switch (x)
                    {
                        case 1: break;                  // +1
                        case 2: break;                  // +1
                    }
                }
            }
            """;

        var complexity = ComplexityAnalyzer.CalculateFileComplexity(code);

        // 1 (base) + 12 branches = 13
        complexity.Should().Be(13);
    }

    [Fact]
    public void CalculateFileComplexity_MultipleMethods_SumsComplexity()
    {
        var code = """
            public class Test
            {
                public void Method1(int x)
                {
                    if (x > 0) { }
                }

                public void Method2(int x)
                {
                    if (x > 0) { }
                    if (x < 0) { }
                }
            }
            """;

        var complexity = ComplexityAnalyzer.CalculateFileComplexity(code);

        // Method1: 1 (base) + 1 (if) = 2
        // Method2: 1 (base) + 2 (if) = 3
        // Total: 5
        complexity.Should().Be(5);
    }

    [Fact]
    public void Analyze_NormalizesComplexity_HighestFileScoresOne()
    {
        var projectDirs = new List<string> { "MyApp" };
        var fullDir = Path.Combine("/repo", "MyApp");
        var simpleFile = Path.Combine("/repo", "MyApp", "Simple.cs");
        var complexFile = Path.Combine("/repo", "MyApp", "Complex.cs");

        _fileSystem.DirectoryExists(fullDir).Returns(true);
        _fileSystem.GetFiles(fullDir, "*.cs", true)
            .Returns([simpleFile, complexFile]);
        _fileSystem.ReadAllText(simpleFile).Returns(TestFixtures.NoBranchCode);
        _fileSystem.ReadAllText(complexFile).Returns(TestFixtures.ComplexCode);

        var result = _sut.Analyze("/repo", projectDirs, []);

        result.FileComplexityNorm.Values.Max().Should().Be(1.0);
    }

    [Fact]
    public void Analyze_SkippedFiles_ExcludedFromNormalization()
    {
        var projectDirs = new List<string> { "MyApp" };
        var fullDir = Path.Combine("/repo", "MyApp");
        var goodFile = Path.Combine("/repo", "MyApp", "Good.cs");
        var badFile = Path.Combine("/repo", "MyApp", "Bad.cs");

        _fileSystem.DirectoryExists(fullDir).Returns(true);
        _fileSystem.GetFiles(fullDir, "*.cs", true)
            .Returns([goodFile, badFile]);
        _fileSystem.ReadAllText(goodFile).Returns(TestFixtures.NoBranchCode);
        _fileSystem.ReadAllText(badFile).Throws(new IOException("encoding issue"));

        var result = _sut.Analyze("/repo", projectDirs, []);

        result.SkippedFiles.Should().Be(1);
        result.FileComplexity.Should().HaveCount(1);
        result.FileComplexityNorm.Should().HaveCount(1);
    }

    [Fact]
    public void Analyze_OnlyAnalyzesFilesUnderProjectDirectories()
    {
        var projectDirs = new List<string> { "backend/MyApp" };
        var fullDir = Path.Combine("/repo", "backend/MyApp");
        var serviceFile = Path.Combine("/repo", "backend/MyApp", "Service.cs");

        _fileSystem.DirectoryExists(fullDir).Returns(true);
        _fileSystem.GetFiles(fullDir, "*.cs", true)
            .Returns([serviceFile]);
        _fileSystem.ReadAllText(serviceFile).Returns(TestFixtures.NoBranchCode);

        var result = _sut.Analyze("/repo", projectDirs, []);

        result.FileComplexity.Should().HaveCount(1);
        _fileSystem.DidNotReceive().GetFiles("/repo", Arg.Any<string>(), Arg.Any<bool>());
    }
}
