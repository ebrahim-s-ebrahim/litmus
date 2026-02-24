namespace DotNetTestRadar.Models;

public class FileRiskReport
{
    public required string File { get; set; }
    public int Commits { get; set; }
    public int WeightedChurn { get; set; }
    public double ChurnNorm { get; set; }
    public double CoverageRate { get; set; }
    public int CyclomaticComplexity { get; set; }
    public double ComplexityNorm { get; set; }
    public double RiskScore { get; set; }
    public string RiskLevel { get; set; } = "Low";
}
