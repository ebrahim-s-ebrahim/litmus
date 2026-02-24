# DotNetTestRadar

A .NET global CLI tool that identifies **high-risk source files** in your .NET solution by cross-referencing three signals:

- **Git churn** -- how frequently a file changes
- **Code coverage** -- how well a file is tested
- **Cyclomatic complexity** -- how complex the file's logic is

The result is a ranked list of files ordered by risk score, so you know **where to add tests first** in a legacy codebase.

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later
- **git** installed and available on PATH
- A **Cobertura XML** coverage report (produced by `coverlet`, `dotnet-coverage`, ReportGenerator, etc.)

### Install

```bash
# Pack the tool
dotnet pack DotNetTestRadar/DotNetTestRadar.csproj -c Release

# Install as a global tool from the local package
dotnet tool install --global --add-source DotNetTestRadar/bin/Release DotNetTestRadar

# Or install from nuget.org (after publishing)
dotnet tool install --global DotNetTestRadar

# Or run directly without installing
dotnet run --project DotNetTestRadar -- analyze --solution path/to/YourApp.sln --coverage coverage.xml
```

### Basic Usage

```bash
dotnet-testradar analyze --solution MyApp.sln --coverage coverage.cobertura.xml
```

This will:

1. Parse the solution to discover project directories
2. Query git history for file change frequency
3. Parse the Cobertura coverage report
4. Compute cyclomatic complexity via Roslyn
5. Produce a color-coded risk table in your terminal

## CLI Options

| Option | Required | Default | Description |
|---|---|---|---|
| `--solution` | Yes | -- | Path to a `.sln` or `.slnx` file |
| `--coverage` | Yes | -- | Path to a Cobertura XML coverage file |
| `--since` | No | 1 year ago | Limit git history to commits after this date (ISO format) |
| `--top` | No | 20 | Number of top risky files to display |
| `--exclude` | No | -- | Glob pattern(s) to exclude files (repeatable) |
| `--output` | No | -- | Export full results to a `.json` or `.csv` file |
| `--no-color` | No | false | Disable colored output |

## Examples

### Analyze the last 6 months, show top 10

```bash
dotnet-testradar analyze \
  --solution src/MyApp.sln \
  --coverage TestResults/coverage.cobertura.xml \
  --since 2025-08-01 \
  --top 10
```

### Exclude generated code and export to JSON

```bash
dotnet-testradar analyze \
  --solution MyApp.sln \
  --coverage coverage.xml \
  --exclude "*.Generated.cs" \
  --exclude "**/ViewModels/*.cs" \
  --output report.json
```

### Pipe-friendly plain output

```bash
dotnet-testradar analyze \
  --solution MyApp.sln \
  --coverage coverage.xml \
  --no-color \
  --output results.csv
```

## Understanding the Output

The tool produces a table like this:

```
╭──────┬────────────────────────────────┬─────────┬──────────┬────────────┬────────────┬────────╮
│ Rank │ File                           │ Commits │ Coverage │ Complexity │ Risk Score │ Level  │
├──────┼────────────────────────────────┼─────────┼──────────┼────────────┼────────────┼────────┤
│ 1    │ Services/PaymentService.cs     │ 47      │ 12%      │ 38         │ 1.7600     │ High   │
│ 2    │ Controllers/OrderController.cs │ 31      │ 0%       │ 25         │ 1.4200     │ High   │
│ 3    │ Data/Repository.cs             │ 22      │ 45%      │ 15         │ 0.3800     │ Medium │
│ 4    │ Helpers/StringUtils.cs         │ 8       │ 90%      │ 5          │ 0.0300     │ Low    │
╰──────┴────────────────────────────────┴─────────┴──────────┴────────────┴────────────┴────────╯
4 files analyzed. 2 high-risk, 1 medium-risk.
```

**Risk levels:**

| Level | Score Range | Meaning |
|---|---|---|
| **High** | >= 0.6 | Changes often, poorly tested, complex logic. Prioritize tests here. |
| **Medium** | >= 0.2 | Moderate risk. Worth investigating. |
| **Low** | < 0.2 | Low churn, well-tested, or simple code. Lower priority. |

## How Risk Is Calculated

```
RiskScore = ChurnNorm x (1 - CoverageRate) x (1 + ComplexityNorm)
```

Each factor is normalized to [0, 1]:

- **ChurnNorm**: weighted lines changed relative to the most-changed file
- **CoverageRate**: line coverage from the Cobertura report (0.0 to 1.0)
- **ComplexityNorm**: cyclomatic complexity relative to the most complex file

A file that changes constantly, has no tests, and is highly complex will score up to **2.0** (the theoretical maximum).

## Default Exclusions

The following patterns are always excluded to reduce noise from auto-generated files:

- `*.Designer.cs`
- `*.g.cs` / `*.g.i.cs`
- `*Migrations/*.cs`
- `*AssemblyInfo.cs`
- `*.xaml.cs`

Use `--exclude` to add additional patterns on top of these defaults.

## Generating a Coverage Report

If you don't have a Cobertura XML file yet, here's how to produce one:

```bash
dotnet test --collect:"XPlat Code Coverage"
```

This creates a `coverage.cobertura.xml` file inside `TestResults/`. You can also use [ReportGenerator](https://github.com/danielpalme/ReportGenerator) to merge multiple coverage files before feeding them to testradar.

## Solution Format Support

The tool supports both classic `.sln` files and the newer `.slnx` XML-based format introduced in .NET 10+. It automatically detects the format based on the file extension.

## Requirements

- .NET 10.0 or later
- Git must be installed and the solution must reside inside a git repository
- A Cobertura XML coverage report

## License

MIT
