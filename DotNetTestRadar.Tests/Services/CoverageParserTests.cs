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
}
