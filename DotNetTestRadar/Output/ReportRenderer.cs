using System.Globalization;
using System.Text;
using System.Text.Json;
using DotNetTestRadar.Abstractions;
using DotNetTestRadar.Models;
using Spectre.Console;

namespace DotNetTestRadar.Output;

public class ReportRenderer
{
    private readonly IFileSystem _fileSystem;

    public ReportRenderer(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public void Render(List<FileRiskReport> reports, int top, bool noColor, string? outputPath, int skippedFiles)
    {
        var topReports = reports.Take(top).ToList();

        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.AddColumn("Rank");
        table.AddColumn("File");
        table.AddColumn("Commits");
        table.AddColumn("Coverage");
        table.AddColumn("Complexity");
        table.AddColumn("Risk Score");
        table.AddColumn("Level");

        for (var i = 0; i < topReports.Count; i++)
        {
            var r = topReports[i];
            var coverageStr = $"{r.CoverageRate * 100:F0}%";
            var scoreStr = $"{r.RiskScore:F4}";
            var style = r.RiskLevel switch
            {
                "High" when !noColor => "red",
                "Medium" when !noColor => "yellow",
                _ => "default"
            };

            table.AddRow(
                new Markup($"[{style}]{i + 1}[/]"),
                new Markup($"[{style}]{r.File.EscapeMarkup()}[/]"),
                new Markup($"[{style}]{r.Commits}[/]"),
                new Markup($"[{style}]{coverageStr}[/]"),
                new Markup($"[{style}]{r.CyclomaticComplexity}[/]"),
                new Markup($"[{style}]{scoreStr}[/]"),
                new Markup($"[{style}]{r.RiskLevel}[/]")
            );
        }

        AnsiConsole.Write(table);

        var highCount = reports.Count(r => r.RiskLevel == "High");
        var mediumCount = reports.Count(r => r.RiskLevel == "Medium");

        var summary = $"{reports.Count} files analyzed. {highCount} high-risk, {mediumCount} medium-risk.";
        if (skippedFiles > 0)
            summary += $" {skippedFiles} files skipped.";

        AnsiConsole.MarkupLine(summary);

        if (outputPath != null)
        {
            ExportResults(reports, outputPath);
        }
    }

    private void ExportResults(List<FileRiskReport> reports, string outputPath)
    {
        var extension = Path.GetExtension(outputPath).ToLowerInvariant();
        var content = extension switch
        {
            ".json" => ExportJson(reports),
            ".csv" => ExportCsv(reports),
            _ => throw new ArgumentException($"Unsupported output format: {extension}. Use .json or .csv.")
        };

        File.WriteAllText(outputPath, content);
        AnsiConsole.MarkupLine($"Results exported to [bold]{outputPath.EscapeMarkup()}[/]");
    }

    private static string ExportJson(List<FileRiskReport> reports)
    {
        var exportData = reports.Select(r => new
        {
            file = r.File,
            commits = r.Commits,
            weightedChurn = r.WeightedChurn,
            coverageRate = r.CoverageRate,
            cyclomaticComplexity = r.CyclomaticComplexity,
            riskScore = r.RiskScore,
            riskLevel = r.RiskLevel
        });

        return JsonSerializer.Serialize(exportData, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    private static string ExportCsv(List<FileRiskReport> reports)
    {
        var sb = new StringBuilder();
        sb.AppendLine("file,commits,weightedChurn,coverageRate,cyclomaticComplexity,riskScore,riskLevel");

        foreach (var r in reports)
        {
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5},{6}",
                EscapeCsvField(r.File),
                r.Commits,
                r.WeightedChurn,
                r.CoverageRate,
                r.CyclomaticComplexity,
                r.RiskScore,
                r.RiskLevel));
        }

        return sb.ToString();
    }

    private static string EscapeCsvField(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
            return $"\"{field.Replace("\"", "\"\"")}\"";
        return field;
    }
}
