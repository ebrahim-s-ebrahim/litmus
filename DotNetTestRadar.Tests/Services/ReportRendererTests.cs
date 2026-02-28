using DotNetTestRadar.Models;
using DotNetTestRadar.Output;
using FluentAssertions;

namespace DotNetTestRadar.Tests.Services;

public class ReportRendererTests
{
    private static FileRiskReport MakeReport(string file, double startingPriority) => new()
    {
        File = file,
        StartingPriority = startingPriority,
        PriorityLevel = startingPriority >= 0.6 ? "High" : startingPriority >= 0.2 ? "Medium" : "Low"
    };

    [Fact]
    public void ComputeBaselineStats_IdentifiesImprovedFiles()
    {
        var reports = new List<FileRiskReport> { MakeReport("A.cs", 0.3) };
        var baseline = new Dictionary<string, double> { ["A.cs"] = 0.5 };

        var (improved, degraded, newFiles, removed) = ReportRenderer.ComputeBaselineStats(reports, baseline);

        improved.Should().Be(1);
        degraded.Should().Be(0);
        newFiles.Should().Be(0);
        removed.Should().Be(0);
    }

    [Fact]
    public void ComputeBaselineStats_IdentifiesDegradedFiles()
    {
        var reports = new List<FileRiskReport> { MakeReport("A.cs", 0.8) };
        var baseline = new Dictionary<string, double> { ["A.cs"] = 0.3 };

        var (improved, degraded, newFiles, removed) = ReportRenderer.ComputeBaselineStats(reports, baseline);

        improved.Should().Be(0);
        degraded.Should().Be(1);
    }

    [Fact]
    public void ComputeBaselineStats_IdentifiesNewFiles()
    {
        var reports = new List<FileRiskReport> { MakeReport("New.cs", 0.5) };
        var baseline = new Dictionary<string, double> { ["Old.cs"] = 0.3 };

        var (improved, degraded, newFiles, removed) = ReportRenderer.ComputeBaselineStats(reports, baseline);

        newFiles.Should().Be(1);
        removed.Should().Be(1);
    }

    [Fact]
    public void ComputeBaselineStats_IdentifiesRemovedFiles()
    {
        var reports = new List<FileRiskReport> { MakeReport("A.cs", 0.5) };
        var baseline = new Dictionary<string, double> { ["A.cs"] = 0.5, ["Removed.cs"] = 0.3 };

        var (_, _, _, removed) = ReportRenderer.ComputeBaselineStats(reports, baseline);

        removed.Should().Be(1);
    }

    [Fact]
    public void ComputeBaselineStats_UnchangedFilesNotCounted()
    {
        var reports = new List<FileRiskReport> { MakeReport("A.cs", 0.500) };
        var baseline = new Dictionary<string, double> { ["A.cs"] = 0.502 }; // within 0.005 threshold

        var (improved, degraded, _, _) = ReportRenderer.ComputeBaselineStats(reports, baseline);

        improved.Should().Be(0);
        degraded.Should().Be(0);
    }

    [Fact]
    public void ComputeBaselineStats_MixedChanges()
    {
        var reports = new List<FileRiskReport>
        {
            MakeReport("Improved.cs", 0.2),
            MakeReport("Degraded.cs", 0.8),
            MakeReport("Same.cs", 0.5),
            MakeReport("New.cs", 0.3)
        };
        var baseline = new Dictionary<string, double>
        {
            ["Improved.cs"] = 0.5,
            ["Degraded.cs"] = 0.3,
            ["Same.cs"] = 0.5,
            ["Gone.cs"] = 0.4
        };

        var (improved, degraded, newFiles, removed) = ReportRenderer.ComputeBaselineStats(reports, baseline);

        improved.Should().Be(1);
        degraded.Should().Be(1);
        newFiles.Should().Be(1);
        removed.Should().Be(1);
    }

    [Fact]
    public void ExportJson_WithBaseline_IncludesDeltaField()
    {
        var reports = new List<FileRiskReport> { MakeReport("A.cs", 0.5) };
        var baseline = new Dictionary<string, double> { ["A.cs"] = 0.3 };

        var json = ReportRenderer.ExportJson(reports, baseline);

        json.Should().Contain("\"delta\"");
    }

    [Fact]
    public void ExportJson_WithoutBaseline_OmitsDeltaField()
    {
        var reports = new List<FileRiskReport> { MakeReport("A.cs", 0.5) };

        var json = ReportRenderer.ExportJson(reports);

        json.Should().NotContain("\"delta\"");
    }

    [Fact]
    public void ExportCsv_WithBaseline_AddsDeltaColumn()
    {
        var reports = new List<FileRiskReport> { MakeReport("A.cs", 0.5) };
        var baseline = new Dictionary<string, double> { ["A.cs"] = 0.3 };

        var csv = ReportRenderer.ExportCsv(reports, baseline);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        lines[0].Should().EndWith(",delta");
        lines[1].Split(',').Length.Should().Be(17);
    }

    [Fact]
    public void ExportCsv_WithoutBaseline_NoDeltaColumn()
    {
        var reports = new List<FileRiskReport> { MakeReport("A.cs", 0.5) };

        var csv = ReportRenderer.ExportCsv(reports);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        lines[0].Should().NotContain("delta");
        lines[1].Split(',').Length.Should().Be(16);
    }

    [Fact]
    public void ExportCsv_NewFileInBaseline_ShowsNewInDeltaColumn()
    {
        var reports = new List<FileRiskReport> { MakeReport("New.cs", 0.5) };
        var baseline = new Dictionary<string, double> { ["Old.cs"] = 0.3 };

        var csv = ReportRenderer.ExportCsv(reports, baseline);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        lines[1].Should().EndWith(",NEW");
    }
}
