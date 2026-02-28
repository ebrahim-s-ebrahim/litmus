using System.Text.Json;
using DotNetTestRadar.Abstractions;
using DotNetTestRadar.Models;
using DotNetTestRadar.Output;
using FluentAssertions;
using NSubstitute;

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
        lines[1].Split(',').Length.Should().Be(20);
    }

    [Fact]
    public void ExportCsv_WithoutBaseline_NoDeltaColumn()
    {
        var reports = new List<FileRiskReport> { MakeReport("A.cs", 0.5) };

        var csv = ReportRenderer.ExportCsv(reports);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        lines[0].Should().NotContain("delta");
        lines[1].Split(',').Length.Should().Be(19);
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

    [Fact]
    public void Render_FormatJson_WritesJsonToStdout()
    {
        var reports = new List<FileRiskReport>
        {
            MakeReport("A.cs", 0.8),
            MakeReport("B.cs", 0.3)
        };

        var renderer = new ReportRenderer(Substitute.For<IFileSystem>());
        var captured = CaptureConsoleOut(() =>
            renderer.Render(reports, 20, noColor: true, outputPath: null, skippedFiles: 0, format: "json"));

        // Should be valid JSON array
        var parsed = JsonSerializer.Deserialize<List<JsonElement>>(captured);
        parsed.Should().HaveCount(2);
        parsed![0].GetProperty("file").GetString().Should().Be("A.cs");
    }

    [Fact]
    public void Render_FormatCsv_WritesCsvToStdout()
    {
        var reports = new List<FileRiskReport>
        {
            MakeReport("A.cs", 0.8),
            MakeReport("B.cs", 0.3)
        };

        var renderer = new ReportRenderer(Substitute.For<IFileSystem>());
        var captured = CaptureConsoleOut(() =>
            renderer.Render(reports, 20, noColor: true, outputPath: null, skippedFiles: 0, format: "csv"));

        var lines = captured.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Length.Should().Be(3); // header + 2 data rows
        lines[0].Should().StartWith("file,");
    }

    [Fact]
    public void Render_FormatJson_WithBaseline_IncludesDelta()
    {
        var reports = new List<FileRiskReport> { MakeReport("A.cs", 0.8) };
        var baseline = new Dictionary<string, double> { ["A.cs"] = 0.3 };

        var renderer = new ReportRenderer(Substitute.For<IFileSystem>());
        var captured = CaptureConsoleOut(() =>
            renderer.Render(reports, 20, noColor: true, outputPath: null, skippedFiles: 0,
                baseline: baseline, format: "json"));

        captured.Should().Contain("\"delta\"");
    }

    [Fact]
    public void Render_FormatJson_OutputsAllReports_NotJustTop()
    {
        var reports = new List<FileRiskReport>
        {
            MakeReport("A.cs", 0.9),
            MakeReport("B.cs", 0.7),
            MakeReport("C.cs", 0.5)
        };

        var renderer = new ReportRenderer(Substitute.For<IFileSystem>());
        var captured = CaptureConsoleOut(() =>
            renderer.Render(reports, top: 2, noColor: true, outputPath: null, skippedFiles: 0, format: "json"));

        // JSON format should include all reports, not just top N
        var parsed = JsonSerializer.Deserialize<List<JsonElement>>(captured);
        parsed.Should().HaveCount(3);
    }

    [Fact]
    public void Render_Quiet_SuppressesTableAndSummary()
    {
        var reports = new List<FileRiskReport> { MakeReport("A.cs", 0.8) };

        var renderer = new ReportRenderer(Substitute.For<IFileSystem>());
        var captured = CaptureConsoleOut(() =>
            renderer.Render(reports, 20, noColor: true, outputPath: null, skippedFiles: 0, quiet: true));

        // Quiet mode should produce no stdout output
        captured.Should().BeEmpty();
    }

    [Fact]
    public void Render_Verbose_ShowsDetailedScoresTable()
    {
        var reports = new List<FileRiskReport> { MakeReport("A.cs", 0.8) };

        // Verbose mode outputs to AnsiConsole (stderr-like), not Console.Out.
        // We just verify it doesn't throw and the method completes.
        var renderer = new ReportRenderer(Substitute.For<IFileSystem>());
        var act = () => renderer.Render(reports, 20, noColor: true, outputPath: null, skippedFiles: 0, verbose: true);
        act.Should().NotThrow();
    }

    [Fact]
    public void ExportJson_IncludesNewDependencyFields()
    {
        var reports = new List<FileRiskReport> { MakeReport("A.cs", 0.5) };

        var json = ReportRenderer.ExportJson(reports);

        json.Should().Contain("\"asyncSeamCalls\"");
        json.Should().Contain("\"concreteCasts\"");
        json.Should().Contain("\"isRegistrationFile\"");
    }

    [Fact]
    public void ExportCsv_IncludesNewDependencyColumns()
    {
        var reports = new List<FileRiskReport> { MakeReport("A.cs", 0.5) };

        var csv = ReportRenderer.ExportCsv(reports);
        var header = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[0];

        header.Should().Contain("asyncSeamCalls");
        header.Should().Contain("concreteCasts");
        header.Should().Contain("isRegistrationFile");
    }

    private static string CaptureConsoleOut(Action action)
    {
        var original = Console.Out;
        try
        {
            using var sw = new StringWriter();
            Console.SetOut(sw);
            action();
            return sw.ToString();
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
