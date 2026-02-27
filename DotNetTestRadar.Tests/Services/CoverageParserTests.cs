using DotNetTestRadar.Abstractions;
using DotNetTestRadar.Services;
using DotNetTestRadar.Tests.Helpers;
using FluentAssertions;
using NSubstitute;

namespace DotNetTestRadar.Tests.Services;

public class CoverageParserTests
{
    private readonly IFileSystem _fileSystem = Substitute.For<IFileSystem>();
    private readonly CoverageParser _sut;

    public CoverageParserTests()
    {
        _sut = new CoverageParser(_fileSystem);
    }

    [Fact]
    public void Parse_ValidCobertura_ReturnsCoveragePerFile()
    {
        _fileSystem.ReadAllText("coverage.xml").Returns(TestFixtures.ValidCoberturaXml);

        var result = _sut.Parse("coverage.xml");

        result.FileCoverage.Should().HaveCount(3);
        result.FileCoverage.Should().ContainKey("MyApp/Services/UserService.cs");
    }

    [Fact]
    public void GetCoverageForFile_FileNotInCoverage_ReturnsZero()
    {
        _fileSystem.ReadAllText("coverage.xml").Returns(TestFixtures.ValidCoberturaXml);

        var result = _sut.Parse("coverage.xml");
        var coverage = CoverageParser.GetCoverageForFile(result, "MyApp/Services/NotCovered.cs");

        coverage.Should().Be(0.0);
    }

    [Fact]
    public void GetCoverageForFile_DifferentPathSeparators_StillMatches()
    {
        _fileSystem.ReadAllText("coverage.xml").Returns(TestFixtures.ValidCoberturaXml);

        var result = _sut.Parse("coverage.xml");
        // Coverage XML has forward slashes; query with backslashes
        var coverage = CoverageParser.GetCoverageForFile(result, "MyApp\\Services\\UserService.cs");

        coverage.Should().BeApproximately(0.23, 0.01);
    }

    [Fact]
    public void Parse_MalformedXml_ThrowsClearException()
    {
        _fileSystem.ReadAllText("bad.xml").Returns("not valid xml <><>");

        var act = () => _sut.Parse("bad.xml");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*invalid XML*");
    }

    [Fact]
    public void Parse_FullyCoveredFile_ReturnsOne()
    {
        _fileSystem.ReadAllText("coverage.xml").Returns(TestFixtures.ValidCoberturaXml);

        var result = _sut.Parse("coverage.xml");
        var coverage = CoverageParser.GetCoverageForFile(result, "MyApp/Models/User.cs");

        coverage.Should().Be(1.0);
    }

    [Fact]
    public void Parse_ZeroCoverageFile_ReturnsZero()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <coverage line-rate="0" branch-rate="0" version="1.0">
              <packages>
                <package name="MyApp">
                  <classes>
                    <class name="MyApp.Uncovered" filename="MyApp/Uncovered.cs" line-rate="0">
                      <lines/>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;
        _fileSystem.ReadAllText("coverage.xml").Returns(xml);

        var result = _sut.Parse("coverage.xml");
        var coverage = CoverageParser.GetCoverageForFile(result, "MyApp/Uncovered.cs");

        coverage.Should().Be(0.0);
    }

    // -------------------------------------------------------------------------
    // Merge — used by scan command when multiple test projects each produce a
    // coverage.cobertura.xml
    // -------------------------------------------------------------------------

    [Fact]
    public void Merge_EmptyInput_ReturnsEmptyResult()
    {
        var merged = CoverageParser.Merge([]);
        merged.FileCoverage.Should().BeEmpty();
    }

    [Fact]
    public void Merge_SingleResult_ReturnsItUnchanged()
    {
        var single = new CoverageResult
        {
            FileCoverage = new Dictionary<string, double>
            {
                ["Foo.cs"] = 0.75,
                ["Bar.cs"] = 0.0
            }
        };

        var merged = CoverageParser.Merge([single]);

        merged.FileCoverage.Should().BeEquivalentTo(single.FileCoverage);
    }

    [Fact]
    public void Merge_NonOverlappingResults_CombinesAll()
    {
        var a = new CoverageResult { FileCoverage = new() { ["A.cs"] = 0.5 } };
        var b = new CoverageResult { FileCoverage = new() { ["B.cs"] = 0.8 } };

        var merged = CoverageParser.Merge([a, b]);

        merged.FileCoverage.Should().HaveCount(2);
        merged.FileCoverage["A.cs"].Should().Be(0.5);
        merged.FileCoverage["B.cs"].Should().Be(0.8);
    }

    [Fact]
    public void Merge_OverlappingFiles_TakesHigherCoverage()
    {
        // TestProject1 covers Service.cs at 30%
        var proj1 = new CoverageResult { FileCoverage = new() { ["Service.cs"] = 0.30 } };
        // TestProject2 covers Service.cs at 80% — higher wins
        var proj2 = new CoverageResult { FileCoverage = new() { ["Service.cs"] = 0.80 } };

        var merged = CoverageParser.Merge([proj1, proj2]);

        merged.FileCoverage["Service.cs"].Should().Be(0.80,
            "the highest coverage across all test projects should win");
    }

    [Fact]
    public void Merge_OverlappingFiles_LowerCoverageDoesNotOverride()
    {
        var high = new CoverageResult { FileCoverage = new() { ["Service.cs"] = 0.90 } };
        var low  = new CoverageResult { FileCoverage = new() { ["Service.cs"] = 0.10 } };

        var merged = CoverageParser.Merge([high, low]);

        merged.FileCoverage["Service.cs"].Should().Be(0.90);
    }

    [Fact]
    public void Merge_MultipleResults_CombinesAndDeduplicatesCorrectly()
    {
        var proj1 = new CoverageResult
        {
            FileCoverage = new()
            {
                ["Services/OrderService.cs"] = 0.20,
                ["Models/Order.cs"] = 1.0
            }
        };
        var proj2 = new CoverageResult
        {
            FileCoverage = new()
            {
                ["Services/OrderService.cs"] = 0.85,  // higher → wins
                ["Controllers/OrderController.cs"] = 0.55
            }
        };

        var merged = CoverageParser.Merge([proj1, proj2]);

        merged.FileCoverage.Should().HaveCount(3);
        merged.FileCoverage["Services/OrderService.cs"].Should().Be(0.85);
        merged.FileCoverage["Models/Order.cs"].Should().Be(1.0);
        merged.FileCoverage["Controllers/OrderController.cs"].Should().Be(0.55);
    }
}
