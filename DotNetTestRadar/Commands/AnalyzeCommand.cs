using System.CommandLine;
using System.Text.Json;
using DotNetTestRadar.Abstractions;
using DotNetTestRadar.Models;
using DotNetTestRadar.Output;
using DotNetTestRadar.Services;
using Spectre.Console;

namespace DotNetTestRadar.Commands;

public class AnalyzeCommand
{
    public static Command Create(IFileSystem fileSystem, IProcessRunner processRunner)
    {
        var solutionOption = new Option<string>("--solution")
        {
            Description = "Path to a .sln or .slnx file",
            Required = true
        };

        var coverageOption = new Option<string>("--coverage")
        {
            Description = "Path to a Cobertura XML coverage file",
            Required = true
        };

        var sinceOption = new Option<DateTime?>("--since")
        {
            Description = "Limit git history to commits after this date (ISO format)"
        };

        var topOption = new Option<int>("--top")
        {
            Description = "Number of top files to display",
            DefaultValueFactory = _ => 20
        };

        var excludeOption = new Option<string[]>("--exclude")
        {
            Description = "Glob pattern(s) to exclude files. Repeatable.",
            AllowMultipleArgumentsPerToken = true
        };

        var outputOption = new Option<string?>("--output")
        {
            Description = "Export results to a JSON or CSV file"
        };

        var baselineOption = new Option<string?>("--baseline")
        {
            Description = "Path to a previous JSON export to compare against"
        };

        var noColorOption = new Option<bool>("--no-color")
        {
            Description = "Disable colored output"
        };

        var formatOption = new Option<string>("--format")
        {
            Description = "Output format for stdout: table, json, or csv",
            DefaultValueFactory = _ => "table"
        };
        formatOption.AcceptOnlyFromAmong("table", "json", "csv");

        var command = new Command("analyze", "Analyze .NET solution for high-risk files and starting priority")
        {
            solutionOption,
            coverageOption,
            sinceOption,
            topOption,
            excludeOption,
            outputOption,
            baselineOption,
            noColorOption,
            formatOption
        };

        command.SetAction(parseResult =>
        {
            var options = new AnalysisOptions
            {
                SolutionPath = parseResult.GetValue(solutionOption)!,
                CoveragePath = parseResult.GetValue(coverageOption)!,
                Since = parseResult.GetValue(sinceOption) ?? DateTime.Today.AddYears(-1),
                Top = parseResult.GetValue(topOption),
                ExcludePatterns = parseResult.GetValue(excludeOption)?.ToList() ?? [],
                OutputPath = parseResult.GetValue(outputOption),
                BaselinePath = parseResult.GetValue(baselineOption),
                NoColor = parseResult.GetValue(noColorOption),
                Format = parseResult.GetValue(formatOption)!
            };

            return Execute(options, fileSystem, processRunner);
        });

        return command;
    }

    private static async Task<int> Execute(AnalysisOptions options, IFileSystem fileSystem, IProcessRunner processRunner)
    {
        // Validate solution file
        if (!fileSystem.FileExists(options.SolutionPath))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Solution file not found: " + options.SolutionPath);
            return 1;
        }

        var ext = Path.GetExtension(options.SolutionPath).ToLowerInvariant();
        if (ext is not ".sln" and not ".slnx")
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Solution file must be a .sln or .slnx file.");
            return 1;
        }

        // Validate coverage file
        if (!fileSystem.FileExists(options.CoveragePath))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Coverage file not found: " + options.CoveragePath);
            return 1;
        }

        // Validate baseline file if provided
        if (options.BaselinePath != null && !fileSystem.FileExists(options.BaselinePath))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Baseline file not found: " + options.BaselinePath);
            return 1;
        }

        // Check git availability
        try
        {
            processRunner.Run("git", "--version", ".");
        }
        catch
        {
            AnsiConsole.MarkupLine("[red]Error:[/] git must be installed and available on PATH.");
            return 1;
        }

        // Parse coverage from the provided file
        var coverageParser = new CoverageParser(fileSystem);
        CoverageResult coverageResult;
        try
        {
            coverageResult = coverageParser.Parse(options.CoveragePath);
        }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }

        return await RunAnalysis(options, coverageResult, fileSystem, processRunner);
    }

    /// <summary>
    /// Runs the core analysis pipeline (solution parsing → churn | complexity | dependency →
    /// scoring → rendering) using an already-parsed coverage result. Called by both
    /// <see cref="AnalyzeCommand"/> (coverage from file) and <see cref="ScanCommand"/>
    /// (coverage auto-generated from dotnet test).
    /// Analyzers run in parallel for performance.
    /// </summary>
    internal static async Task<int> RunAnalysis(
        AnalysisOptions options,
        CoverageResult coverageResult,
        IFileSystem fileSystem,
        IProcessRunner processRunner)
    {
        try
        {
            // Step 0: Parse solution
            var solutionParser = new SolutionParser(fileSystem, processRunner);
            SolutionParseResult parseResult;
            try
            {
                parseResult = solutionParser.Parse(options.SolutionPath);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("rev-parse") || ex.Message.Contains("git repository"))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] The solution path must be inside a git repository.");
                return 1;
            }
            catch (InvalidOperationException ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                return 1;
            }

            if (parseResult.ProjectDirectories.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]Warning:[/] No project directories found in the solution.");
                return 0;
            }

            // Compute effective exclusion patterns once for all analyzers
            var effectivePatterns = FileFilterHelper.GetEffectivePatterns(options.ExcludePatterns);

            // Steps 1-3: Run churn, complexity, and dependency analyzers in parallel
            var churnAnalyzer = new GitChurnAnalyzer(processRunner);
            var complexityAnalyzer = new ComplexityAnalyzer(fileSystem);
            var dependencyAnalyzer = new DependencyAnalyzer(fileSystem);

            var churnTask = Task.Run(() => churnAnalyzer.Analyze(
                parseResult.GitRoot,
                parseResult.SolutionDirectory,
                parseResult.ProjectDirectories,
                options.Since,
                options.ExcludePatterns));

            var complexityTask = Task.Run(() => complexityAnalyzer.Analyze(
                parseResult.GitRoot,
                parseResult.ProjectDirectories,
                effectivePatterns));

            var dependencyTask = Task.Run(() => dependencyAnalyzer.Analyze(
                parseResult.GitRoot,
                parseResult.ProjectDirectories,
                effectivePatterns));

            await Task.WhenAll(churnTask, complexityTask, dependencyTask);

            var churnResult = churnTask.Result;
            var complexityResult = complexityTask.Result;
            var dependencyResult = dependencyTask.Result;

            // Step 4: Risk scoring + starting priority
            var riskScorer = new RiskScorer();
            var reports = riskScorer.Score(
                churnResult,
                coverageResult,
                complexityResult,
                dependencyResult,
                parseResult.SolutionDirectory,
                parseResult.GitRoot);

            if (reports.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]Warning:[/] No .cs files found after applying all filters.");
                return 0;
            }

            // Load baseline if provided
            Dictionary<string, double>? baseline = null;
            if (options.BaselinePath != null)
            {
                try
                {
                    var baselineJson = fileSystem.ReadAllText(options.BaselinePath);
                    var items = JsonSerializer.Deserialize<List<JsonElement>>(baselineJson)!;
                    baseline = items.ToDictionary(
                        e => e.GetProperty("file").GetString()!,
                        e => e.GetProperty("startingPriority").GetDouble());
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Error:[/] Failed to load baseline: {ex.Message.EscapeMarkup()}");
                    return 1;
                }
            }

            // Step 5: Render
            var renderer = new ReportRenderer(fileSystem);
            var totalSkippedFiles = complexityResult.SkippedFiles + dependencyResult.SkippedFiles;
            renderer.Render(reports, options.Top, options.NoColor, options.OutputPath, totalSkippedFiles, baseline, options.Format);

            // Warn about files that had no entry in the coverage report
            var filesWithNoCoverageEntry = reports
                .Where(r => r.CoverageRate == 0.0 &&
                       !coverageResult.FileCoverage.Keys.Any(k =>
                           k.EndsWith(r.File.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase) ||
                           r.File.Replace('\\', '/').EndsWith(k, StringComparison.OrdinalIgnoreCase)))
                .Select(r => r.File)
                .ToList();

            if (filesWithNoCoverageEntry.Count > 0)
            {
                AnsiConsole.MarkupLine($"\n[yellow]Warning:[/] {filesWithNoCoverageEntry.Count} file(s) had no entry in the coverage report (treated as 0% coverage):");
                foreach (var f in filesWithNoCoverageEntry.Take(10))
                {
                    AnsiConsole.MarkupLine($"  - {f.EscapeMarkup()}");
                }
                if (filesWithNoCoverageEntry.Count > 10)
                    AnsiConsole.MarkupLine($"  ... and {filesWithNoCoverageEntry.Count - 10} more.");
            }

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message.EscapeMarkup()}");
            return 1;
        }
    }
}
