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

    public void Render(List<FileRiskReport> reports, int top, bool noColor, string? outputPath, int skippedFiles,
        Dictionary<string, double>? baseline = null, string format = "table",
        bool verbose = false, bool quiet = false)
    {
        // Structured stdout formats: write JSON or CSV to Console.Out and skip the table
        if (format is "json" or "csv")
        {
            var content = format == "json"
                ? ExportJson(reports, baseline)
                : ExportCsv(reports, baseline);
            Console.Out.Write(content);

            // Still export to file if --output was also provided
            if (outputPath != null)
                ExportResults(reports, outputPath, baseline);

            return;
        }

        // Quiet mode: skip all table/summary output, only do file export
        if (quiet)
        {
            if (outputPath != null)
                ExportResults(reports, outputPath, baseline);
            return;
        }

        var topReports = reports.Take(top).ToList();
        var hasBaseline = baseline != null;

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
        if (hasBaseline)
            table.AddColumn("Delta");
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
                "High" when !noColor => "red",
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

            var columns = new List<Markup>
            {
                new($"[{rowStyle}]{i + 1}[/]"),
                new($"[{rowStyle}]{r.File.EscapeMarkup()}[/]"),
                new($"[{rowStyle}]{r.Commits}[/]"),
                new($"[{rowStyle}]{coverageStr}[/]"),
                new($"[{rowStyle}]{r.CyclomaticComplexity}[/]"),
                new($"[{depStyle}]{r.DependencyLevel}[/]"),
                new($"[{riskStyle}]{riskStr}[/]"),
                new($"[{rowStyle}]{priorityStr}[/]")
            };

            if (hasBaseline)
            {
                var deltaMarkup = FormatDelta(r.File, r.StartingPriority, baseline!, noColor);
                columns.Add(deltaMarkup);
            }

            columns.Add(new Markup($"[{rowStyle}]{r.PriorityLevel}[/]"));

            table.AddRow(columns);
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

        // Baseline comparison summary
        if (hasBaseline)
        {
            RenderBaselineSummary(reports, baseline!);
        }

        // Verbose: show detailed intermediate scores
        if (verbose)
        {
            AnsiConsole.WriteLine();
            var detailTable = new Table();
            detailTable.Border(TableBorder.Simple);
            detailTable.AddColumn("File");
            detailTable.AddColumn("ChurnNorm");
            detailTable.AddColumn("Coverage");
            detailTable.AddColumn("CplxNorm");
            detailTable.AddColumn("Infra");
            detailTable.AddColumn("New");
            detailTable.AddColumn("Params");
            detailTable.AddColumn("Static");
            detailTable.AddColumn("Async");
            detailTable.AddColumn("Casts");
            detailTable.AddColumn("DepScore");
            detailTable.AddColumn("DepNorm");
            detailTable.AddColumn("RegFile");

            foreach (var r in topReports)
            {
                detailTable.AddRow(
                    r.File.EscapeMarkup(),
                    $"{r.ChurnNorm:F4}",
                    $"{r.CoverageRate:F4}",
                    $"{r.ComplexityNorm:F4}",
                    r.InfrastructureCalls.ToString(),
                    r.DirectInstantiations.ToString(),
                    r.ConcreteConstructorParams.ToString(),
                    r.StaticCalls.ToString(),
                    r.AsyncSeamCalls.ToString(),
                    r.ConcreteCasts.ToString(),
                    $"{r.RawDependencyScore:F2}",
                    $"{r.DependencyNorm:F4}",
                    r.IsRegistrationFile ? "Yes" : "");
            }

            AnsiConsole.MarkupLine("[bold]Detailed Scores[/]");
            AnsiConsole.Write(detailTable);
        }

        if (outputPath != null)
        {
            ExportResults(reports, outputPath, baseline);
        }
    }

    internal static Markup FormatDelta(string file, double currentPriority, Dictionary<string, double> baseline, bool noColor)
    {
        if (!baseline.TryGetValue(file, out var previousPriority))
            return new Markup(noColor ? "NEW" : "[dim]NEW[/]");

        var delta = currentPriority - previousPriority;

        if (Math.Abs(delta) < 0.005)
            return new Markup("\u2014"); // em dash

        var sign = delta > 0 ? "+" : "";
        var deltaStr = $"{sign}{delta:F2}";

        if (noColor)
            return new Markup(deltaStr);

        // Positive delta = priority went up = file got worse = red
        // Negative delta = priority went down = file improved = green
        var style = delta > 0 ? "red" : "green";
        return new Markup($"[{style}]{deltaStr}[/]");
    }

    internal static (int Improved, int Degraded, int New, int Removed) ComputeBaselineStats(
        List<FileRiskReport> reports, Dictionary<string, double> baseline)
    {
        var improved = 0;
        var degraded = 0;
        var newFiles = 0;

        foreach (var r in reports)
        {
            if (!baseline.TryGetValue(r.File, out var prev))
            {
                newFiles++;
                continue;
            }

            var delta = r.StartingPriority - prev;
            if (delta < -0.005) improved++;
            else if (delta > 0.005) degraded++;
        }

        var removedFiles = baseline.Keys.Count(k => reports.All(r => r.File != k));

        return (improved, degraded, newFiles, removedFiles);
    }

    private static void RenderBaselineSummary(List<FileRiskReport> reports, Dictionary<string, double> baseline)
    {
        var (improved, degraded, newFiles, removed) = ComputeBaselineStats(reports, baseline);
        AnsiConsole.MarkupLine($"vs baseline: {improved} improved, {degraded} degraded, {newFiles} new, {removed} removed.");
    }

    private void ExportResults(List<FileRiskReport> reports, string outputPath, Dictionary<string, double>? baseline)
    {
        var extension = Path.GetExtension(outputPath).ToLowerInvariant();
        var content = extension switch
        {
            ".json" => ExportJson(reports, baseline),
            ".csv" => ExportCsv(reports, baseline),
            _ => throw new ArgumentException($"Unsupported output format: {extension}. Use .json or .csv.")
        };

        File.WriteAllText(outputPath, content);
        AnsiConsole.MarkupLine($"Results exported to [bold]{outputPath.EscapeMarkup()}[/]");
    }

    internal static string ExportJson(List<FileRiskReport> reports, Dictionary<string, double>? baseline = null)
    {
        var exportData = reports.Select(r =>
        {
            double? delta = baseline != null
                ? (baseline.TryGetValue(r.File, out var prev) ? r.StartingPriority - prev : null)
                : null;

            return new
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
                asyncSeamCalls = r.AsyncSeamCalls,
                concreteCasts = r.ConcreteCasts,
                isRegistrationFile = r.IsRegistrationFile,
                rawDependencyScore = r.RawDependencyScore,
                dependencyNorm = r.DependencyNorm,
                dependencyLevel = r.DependencyLevel,
                startingPriority = r.StartingPriority,
                priorityLevel = r.PriorityLevel,
                delta
            };
        });

        return JsonSerializer.Serialize(exportData, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }

    internal static string ExportCsv(List<FileRiskReport> reports, Dictionary<string, double>? baseline = null)
    {
        var sb = new StringBuilder();
        var header = "file,commits,weightedChurn,coverageRate,cyclomaticComplexity,riskScore,riskLevel," +
                     "infrastructureCalls,directInstantiations,concreteConstructorParams,staticCalls," +
                     "asyncSeamCalls,concreteCasts,isRegistrationFile," +
                     "rawDependencyScore,dependencyNorm,dependencyLevel,startingPriority,priorityLevel";
        if (baseline != null)
            header += ",delta";
        sb.AppendLine(header);

        foreach (var r in reports)
        {
            var line = string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18}",
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
                r.AsyncSeamCalls,
                r.ConcreteCasts,
                r.IsRegistrationFile.ToString().ToLowerInvariant(),
                r.RawDependencyScore,
                r.DependencyNorm,
                EscapeCsvField(r.DependencyLevel),
                r.StartingPriority,
                EscapeCsvField(r.PriorityLevel));

            if (baseline != null)
            {
                var delta = baseline.TryGetValue(r.File, out var prev)
                    ? (r.StartingPriority - prev).ToString("F4", CultureInfo.InvariantCulture)
                    : "NEW";
                line += $",{delta}";
            }

            sb.AppendLine(line);
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
