using System.CommandLine;
using DotNetTestRadar.Abstractions;
using DotNetTestRadar.Models;
using DotNetTestRadar.Services;
using Spectre.Console;

namespace DotNetTestRadar.Commands;

public class ScanCommand
{
    public static Command Create(IFileSystem fileSystem, IProcessRunner processRunner)
    {
        var solutionOption = new Option<string>("--solution")
        {
            Description = "Path to a .sln or .slnx file",
            Required = true
        };

        var testsDirOption = new Option<string?>("--tests-dir")
        {
            Description = "Directory or project file to run tests from. " +
                          "Defaults to the solution file, which runs all test projects in the solution."
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

        var noColorOption = new Option<bool>("--no-color")
        {
            Description = "Disable colored output"
        };

        var command = new Command(
            "scan",
            "Run dotnet test, collect code coverage, and analyze the solution in one step")
        {
            solutionOption,
            testsDirOption,
            sinceOption,
            topOption,
            excludeOption,
            outputOption,
            noColorOption
        };

        command.SetAction(parseResult =>
        {
            var options = new AnalysisOptions
            {
                SolutionPath = parseResult.GetValue(solutionOption)!,
                CoveragePath = string.Empty,  // filled in at runtime after dotnet test
                Since = parseResult.GetValue(sinceOption) ?? DateTime.Today.AddYears(-1),
                Top = parseResult.GetValue(topOption),
                ExcludePatterns = parseResult.GetValue(excludeOption)?.ToList() ?? [],
                OutputPath = parseResult.GetValue(outputOption),
                NoColor = parseResult.GetValue(noColorOption)
            };

            var testsDir = parseResult.GetValue(testsDirOption);
            return Execute(options, testsDir, fileSystem, processRunner);
        });

        return command;
    }

    private static int Execute(
        AnalysisOptions options,
        string? testsDir,
        IFileSystem fileSystem,
        IProcessRunner processRunner)
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

        // Validate --tests-dir if provided
        if (testsDir != null && !fileSystem.FileExists(testsDir) && !fileSystem.DirectoryExists(testsDir))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Tests path not found: {testsDir}");
            return 1;
        }

        // Check dotnet availability
        try
        {
            processRunner.Run("dotnet", "--version", ".");
        }
        catch
        {
            AnsiConsole.MarkupLine("[red]Error:[/] dotnet SDK must be installed and available on PATH.");
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

        var solutionDir = Path.GetDirectoryName(Path.GetFullPath(options.SolutionPath)) ?? ".";
        var testTarget = testsDir ?? options.SolutionPath;
        var tempDir = Path.Combine(Path.GetTempPath(), $"testradar-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDir);

            // Step 1: Run dotnet test with coverage collection
            AnsiConsole.MarkupLine("[bold]Step 1/2:[/] Running tests and collecting coverage...");

            var testArgs = $"test \"{testTarget}\" " +
                           $"--collect:\"XPlat Code Coverage\" " +
                           $"--results-directory \"{tempDir}\"";

            try
            {
                processRunner.Run("dotnet", testArgs, solutionDir);
                AnsiConsole.MarkupLine("[green]Tests passed.[/]");
            }
            catch (InvalidOperationException)
            {
                // Tests failing is not a fatal error — coverage files may still be present
                AnsiConsole.MarkupLine("[yellow]Warning:[/] Some tests failed. Coverage data may be incomplete.");
            }

            // Discover all coverage.cobertura.xml files produced by the test run
            var coverageFiles = fileSystem
                .GetFiles(tempDir, "coverage.cobertura.xml", recursive: true)
                .ToList();

            if (coverageFiles.Count == 0)
            {
                AnsiConsole.MarkupLine(
                    "[red]Error:[/] No coverage.cobertura.xml files were generated.\n" +
                    "Make sure your test project(s) reference the coverlet.collector package:\n" +
                    "  dotnet add <test-project> package coverlet.collector");
                return 1;
            }

            // Parse each coverage file and merge — taking the highest rate per source file
            // so that a class covered by any test project counts as covered
            var coverageParser = new CoverageParser(fileSystem);
            CoverageResult coverageResult;
            try
            {
                var allResults = coverageFiles.Select(f => coverageParser.Parse(f)).ToList();
                coverageResult = CoverageParser.Merge(allResults);

                if (coverageFiles.Count > 1)
                    AnsiConsole.MarkupLine($"[dim]Merged coverage from {coverageFiles.Count} test project(s).[/]");
            }
            catch (InvalidOperationException ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                return 1;
            }

            // Step 2: Run the full analysis pipeline
            AnsiConsole.MarkupLine("[bold]Step 2/2:[/] Analyzing solution...");
            return AnalyzeCommand.RunAnalysis(options, coverageResult, fileSystem, processRunner);
        }
        finally
        {
            // Best-effort cleanup of the temporary results directory
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // Don't fail the command if cleanup fails
            }
        }
    }
}
