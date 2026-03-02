using Litmus.Models;
using Litmus.Services;
using FluentAssertions;

namespace Litmus.Tests.Services;

public class FileFilterHelperTests
{
    // -------------------------------------------------------------------------
    // MatchGlob
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("Test.Designer.cs", "*.Designer.cs", true)]
    [InlineData("Test.g.cs", "*.g.cs", true)]
    [InlineData("Test.g.i.cs", "*.g.i.cs", true)]
    [InlineData("Test.generated.cs", "*.generated.cs", true)]
    [InlineData("AssemblyInfo.cs", "*AssemblyInfo.cs", true)]
    [InlineData("GlobalUsings.g.cs", "*GlobalUsings.g.cs", true)]
    [InlineData("MyModelSnapshot.cs", "*ModelSnapshot.cs", true)]
    [InlineData("UserService.cs", "*.Designer.cs", false)]
    [InlineData("UserService.cs", "Program.cs", false)]
    public void MatchGlob_FileNamePatterns(string input, string pattern, bool expected)
    {
        FileFilterHelper.MatchGlob(input, pattern).Should().Be(expected);
    }

    [Theory]
    [InlineData("MyApp/obj/Debug/net10.0/Generated.cs", "**/obj/**", true)]
    [InlineData("MyApp/bin/Release/net10.0/App.cs", "**/bin/**", true)]
    [InlineData("MyApp/Migrations/20240101_Init.cs", "**/Migrations/*.cs", true)]
    [InlineData("MyApp/wwwroot/scripts/app.cs", "**/wwwroot/**", true)]
    [InlineData("MyApp/Services/UserService.cs", "**/obj/**", false)]
    [InlineData("MyApp/Services/UserService.cs", "**/bin/**", false)]
    public void MatchGlob_DirectoryPatterns(string input, string pattern, bool expected)
    {
        FileFilterHelper.MatchGlob(input, pattern).Should().Be(expected);
    }

    [Fact]
    public void MatchGlob_IsCaseInsensitive()
    {
        FileFilterHelper.MatchGlob("Test.DESIGNER.cs", "*.Designer.cs").Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // MatchesAnyPattern
    // -------------------------------------------------------------------------

    [Fact]
    public void MatchesAnyPattern_ObjFolder_Excluded()
    {
        var patterns = FileFilterHelper.GetEffectivePatterns([]);
        FileFilterHelper.MatchesAnyPattern("MyApp/obj/Debug/net10.0/SomeGenerated.cs", patterns)
            .Should().BeTrue();
    }

    [Fact]
    public void MatchesAnyPattern_BinFolder_Excluded()
    {
        var patterns = FileFilterHelper.GetEffectivePatterns([]);
        FileFilterHelper.MatchesAnyPattern("MyApp/bin/Release/net10.0/App.cs", patterns)
            .Should().BeTrue();
    }

    [Fact]
    public void MatchesAnyPattern_ProgramCs_Excluded()
    {
        var patterns = FileFilterHelper.GetEffectivePatterns([]);
        FileFilterHelper.MatchesAnyPattern("MyApp/Program.cs", patterns)
            .Should().BeTrue();
    }

    [Fact]
    public void MatchesAnyPattern_StartupCs_Excluded()
    {
        var patterns = FileFilterHelper.GetEffectivePatterns([]);
        FileFilterHelper.MatchesAnyPattern("MyApp/Startup.cs", patterns)
            .Should().BeTrue();
    }

    [Fact]
    public void MatchesAnyPattern_RegularServiceFile_NotExcluded()
    {
        var patterns = FileFilterHelper.GetEffectivePatterns([]);
        FileFilterHelper.MatchesAnyPattern("MyApp/Services/UserService.cs", patterns)
            .Should().BeFalse();
    }

    [Fact]
    public void MatchesAnyPattern_MigrationFile_Excluded()
    {
        var patterns = FileFilterHelper.GetEffectivePatterns([]);
        FileFilterHelper.MatchesAnyPattern("MyApp/Migrations/20240101_Init.cs", patterns)
            .Should().BeTrue();
    }

    [Fact]
    public void MatchesAnyPattern_DesignerFile_Excluded()
    {
        var patterns = FileFilterHelper.GetEffectivePatterns([]);
        FileFilterHelper.MatchesAnyPattern("MyApp/Forms/MainForm.Designer.cs", patterns)
            .Should().BeTrue();
    }

    [Fact]
    public void MatchesAnyPattern_UserPatternMergedWithDefaults()
    {
        var patterns = FileFilterHelper.GetEffectivePatterns(["**/Experimental/**"]);

        FileFilterHelper.MatchesAnyPattern("MyApp/Experimental/Sandbox.cs", patterns)
            .Should().BeTrue("user pattern should be applied");
        FileFilterHelper.MatchesAnyPattern("MyApp/obj/Debug/net10.0/G.cs", patterns)
            .Should().BeTrue("default patterns should still apply");
    }

    // -------------------------------------------------------------------------
    // GetEffectivePatterns
    // -------------------------------------------------------------------------

    [Fact]
    public void GetEffectivePatterns_IncludesAllDefaults()
    {
        var patterns = FileFilterHelper.GetEffectivePatterns([]);
        patterns.Should().Contain(AnalysisOptions.DefaultExclusions);
    }

    [Fact]
    public void GetEffectivePatterns_AppendsUserPatterns()
    {
        var patterns = FileFilterHelper.GetEffectivePatterns(["**/Custom/**"]);
        patterns.Should().Contain("**/Custom/**");
        patterns.Count.Should().Be(AnalysisOptions.DefaultExclusions.Length + 1);
    }
}
