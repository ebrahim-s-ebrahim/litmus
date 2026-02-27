using System.Globalization;
using System.Xml.Linq;
using DotNetTestRadar.Abstractions;

namespace DotNetTestRadar.Services;

public class CoverageResult
{
    public Dictionary<string, double> FileCoverage { get; set; } = new();
}

public class CoverageParser
{
    private readonly IFileSystem _fileSystem;

    public CoverageParser(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public CoverageResult Parse(string coveragePath)
    {
        var content = _fileSystem.ReadAllText(coveragePath);

        XDocument doc;
        try
        {
            doc = XDocument.Parse(content);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new InvalidOperationException($"The coverage file contains invalid XML: {ex.Message}", ex);
        }

        var result = new CoverageResult();

        // Cobertura format: <coverage> -> <packages> -> <package> -> <classes> -> <class filename="...">
        var classes = doc.Descendants("class");

        foreach (var cls in classes)
        {
            var filename = cls.Attribute("filename")?.Value;
            var lineRateStr = cls.Attribute("line-rate")?.Value;

            if (filename == null || lineRateStr == null)
                continue;

            if (!double.TryParse(lineRateStr, CultureInfo.InvariantCulture, out var lineRate))
                continue;

            // Normalize path separators
            var normalizedPath = filename.Replace('\\', '/');

            // If multiple class entries exist for same file, take the last one
            result.FileCoverage[normalizedPath] = lineRate;
        }

        return result;
    }

    /// <summary>
    /// Merges multiple coverage results into one by taking the highest coverage rate
    /// seen for each file across all results. Used when multiple test projects each
    /// produce their own coverage.cobertura.xml.
    /// </summary>
    public static CoverageResult Merge(IEnumerable<CoverageResult> results)
    {
        var merged = new CoverageResult();
        foreach (var result in results)
        {
            foreach (var (file, rate) in result.FileCoverage)
            {
                if (!merged.FileCoverage.TryGetValue(file, out var existing) || rate > existing)
                    merged.FileCoverage[file] = rate;
            }
        }
        return merged;
    }

    public static double GetCoverageForFile(CoverageResult coverage, string fileRelativePath)
    {
        var normalizedTarget = fileRelativePath.Replace('\\', '/');

        // Try exact match first
        if (coverage.FileCoverage.TryGetValue(normalizedTarget, out var rate))
            return rate;

        // Try suffix matching (coverage paths may have different prefixes)
        foreach (var (path, coverageRate) in coverage.FileCoverage)
        {
            if (path.EndsWith(normalizedTarget, StringComparison.OrdinalIgnoreCase) ||
                normalizedTarget.EndsWith(path, StringComparison.OrdinalIgnoreCase))
            {
                return coverageRate;
            }
        }

        return 0.0;
    }
}
