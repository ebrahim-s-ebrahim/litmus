namespace DotNetTestRadar.Models;

public class AnalysisOptions
{
    public required string SolutionPath { get; set; }
    public required string CoveragePath { get; set; }
    public DateTime Since { get; set; } = DateTime.Today.AddYears(-1);
    public int Top { get; set; } = 20;
    public List<string> ExcludePatterns { get; set; } = [];
    public string? OutputPath { get; set; }
    public bool NoColor { get; set; }
    public bool Deep { get; set; }

    public static readonly string[] DefaultExclusions =
    [
        // Build output
        "**/obj/**",
        "**/bin/**",
        // Auto-generated
        "*.Designer.cs",
        "*.g.cs",
        "*.g.i.cs",
        "*.generated.cs",
        "*.xaml.cs",
        "*AssemblyInfo.cs",
        "*GlobalUsings.g.cs",
        // Migrations
        "**/Migrations/*.cs",
        "*ModelSnapshot.cs",
        // Entry points / bootstrapping
        "Program.cs",
        "Startup.cs",
        // Static web assets
        "**/wwwroot/**"
    ];
}
