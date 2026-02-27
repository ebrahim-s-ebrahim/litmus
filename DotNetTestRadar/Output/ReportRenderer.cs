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
        table.AddColumn("Dependency");
        table.AddColumn("Risk");
        table.AddColumn("Priority");
        table.AddColumn("Level");

        for (var i = 0; i < topReports.Count; i++)
        {
            var r = topReports[i];
            var coverageStr = $"{r.CoverageRate * 100:F0}%";
            var riskStr = $"{r.RiskScore:F2}";
            var priorityStr = $"{r.StartingPriority:F2}";

            // Row color is driven by PriorityLevel — the actionable starting point
            var rowStyle = r.PriorityLevel switch
            {
                "High" when !noColor => "green",
                "Medium" when !noColor => "yellow",
                _ => "default"
            };

            // Risk column gets independent coloring to highlight dangerous-but-entangled files
            var riskStyle = r.RiskLevel switch
            {
                "High" when !noColor => "red",
                "Medium" when !noColor => "yellow",
                _ => rowStyle
            };

            // Dependency level styled to indicate cost of introducing seams
            var depStyle = r.DependencyLevel switch
            {
                "Very High" when !noColor => "red",
                "High" when !noColor => "yellow",
                _ => rowStyle
            };

            table.AddRow(
                new Markup($"[{rowStyle}]{i + 1}[/]"),
                new Markup($"[{rowStyle}]{r.File.EscapeMarkup()}[/]"),
                new Markup($"[{rowStyle}]{r.Commits}[/]"),
                new Markup($"[{rowStyle}]{coverageStr}[/]"),
                new Markup($"[{rowStyle}]{r.CyclomaticComplexity}[/]"),
                new Markup($"[{depStyle}]{r.DependencyLevel}[/]"),
                new Markup($"[{riskStyle}]{riskStr}[/]"),
                new Markup($"[{rowStyle}]{priorityStr}[/]"),
                new Markup($"[{rowStyle}]{r.PriorityLevel}[/]")
            );
        }

        AnsiConsole.Write(table);

        var highPriorityCount = reports.Count(r => r.PriorityLevel == "High");
        var mediumPriorityCount = reports.Count(r => r.PriorityLevel == "Medium");
        var highRiskNeedSeams = reports.Count(r => r.RiskLevel == "High" && r.PriorityLevel != "High");

        var summary = $"{reports.Count} files analyzed. {highPriorityCount} high-priority (start today), {mediumPriorityCount} medium-priority (next sprint).";
        if (highRiskNeedSeams > 0)
            summary += $" {highRiskNeedSeams} high-risk file(s) need seam introduction before testing.";
        if (skippedFiles > 0)
            summary += $" {skippedFiles} file(s) skipped.";

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
            riskLevel = r.RiskLevel,
            infrastructureCalls = r.InfrastructureCalls,
            directInstantiations = r.DirectInstantiations,
            concreteConstructorParams = r.ConcreteConstructorParams,
            staticCalls = r.StaticCalls,
            rawDependencyScore = r.RawDependencyScore,
            dependencyNorm = r.DependencyNorm,
            dependencyLevel = r.DependencyLevel,
            startingPriority = r.StartingPriority,
            priorityLevel = r.PriorityLevel
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
        sb.AppendLine("file,commits,weightedChurn,coverageRate,cyclomaticComplexity,riskScore,riskLevel," +
                      "infrastructureCalls,directInstantiations,concreteConstructorParams,staticCalls," +
                      "rawDependencyScore,dependencyNorm,dependencyLevel,startingPriority,priorityLevel");

        foreach (var r in reports)
        {
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15}",
                EscapeCsvField(r.File),
                r.Commits,
                r.WeightedChurn,
                r.CoverageRate,
                r.CyclomaticComplexity,
                r.RiskScore,
                r.RiskLevel,
                r.InfrastructureCalls,
                r.DirectInstantiations,
                r.ConcreteConstructorParams,
                r.StaticCalls,
                r.RawDependencyScore,
                r.DependencyNorm,
                EscapeCsvField(r.DependencyLevel),
                r.StartingPriority,
                EscapeCsvField(r.PriorityLevel)));
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
