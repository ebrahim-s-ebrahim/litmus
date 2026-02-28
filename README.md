# DotNetTestRadar

[![NuGet](https://img.shields.io/nuget/v/DotNetTestRadar.svg?include_prereleases)](https://www.nuget.org/packages/DotNetTestRadar)
[![NuGet Downloads](https://img.shields.io/nuget/dt/DotNetTestRadar.svg?include_prereleases)](https://www.nuget.org/packages/DotNetTestRadar)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/ebrahim-s-ebrahim/testradar/blob/main/LICENSE)

A .NET global CLI tool that answers two complementary questions when adding tests to a legacy codebase:

1. **Where is it dangerous to leave code untested?** — ranked by *Risk Score*
2. **Where can you actually start testing today?** — ranked by *Starting Priority*

It cross-references four signals to produce both scores:

- **Git churn** — how frequently a file changes
- **Code coverage** — how well a file is tested
- **Cyclomatic complexity** — how complex the file's logic is
- **Dependency entanglement** — how many unseamed dependencies the file has (seam analysis)

The result is a ranked table sorted by *Starting Priority*: files that are both dangerous **and** practically testable today appear at the top. Files that are dangerous but heavily entangled appear lower, with a clear signal to introduce seams before attempting to test them.

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

### Two ways to run

**Option A — One step with `scan` (recommended for getting started):**

```bash
dotnet-testradar scan --solution MyApp.sln
```

This runs `dotnet test`, collects coverage automatically, then runs the full analysis. No need to generate a coverage file first.

**Option B — Two steps with `analyze` (when you already have coverage):**

```bash
# Generate coverage separately (e.g. in CI)
dotnet test --collect:"XPlat Code Coverage"

# Then analyze
dotnet-testradar analyze --solution MyApp.sln --coverage TestResults/.../coverage.cobertura.xml
```

### What `scan` does

1. Runs `dotnet test` with the XPlat Code Coverage collector
2. Discovers all `coverage.cobertura.xml` files produced (one per test project)
3. Merges them (taking the highest coverage rate per source file)
4. Runs the full analysis pipeline (git churn → complexity → seam detection → scoring)
5. Cleans up the temporary test results directory

## CLI Options

### `scan` — runs tests and analyzes in one step

| Option | Required | Default | Description |
|---|---|---|---|
| `--solution` | Yes | -- | Path to a `.sln` or `.slnx` file |
| `--tests-dir` | No | solution file | Directory or project to run `dotnet test` against |
| `--since` | No | 1 year ago | Limit git history to commits after this date (ISO format) |
| `--top` | No | 20 | Number of top files to display |
| `--exclude` | No | -- | Glob pattern(s) to exclude files (repeatable) |
| `--output` | No | -- | Export full results to a `.json` or `.csv` file |
| `--baseline` | No | -- | Path to a previous JSON export to compare against |
| `--format` | No | table | Output format for stdout: `table`, `json`, or `csv` |
| `--no-color` | No | false | Disable colored output |

### `analyze` — use an existing coverage file

| Option | Required | Default | Description |
|---|---|---|---|
| `--solution` | Yes | -- | Path to a `.sln` or `.slnx` file |
| `--coverage` | Yes | -- | Path to a Cobertura XML coverage file |
| `--since` | No | 1 year ago | Limit git history to commits after this date (ISO format) |
| `--top` | No | 20 | Number of top files to display |
| `--exclude` | No | -- | Glob pattern(s) to exclude files (repeatable) |
| `--output` | No | -- | Export full results to a `.json` or `.csv` file |
| `--baseline` | No | -- | Path to a previous JSON export to compare against |
| `--format` | No | table | Output format for stdout: `table`, `json`, or `csv` |
| `--no-color` | No | false | Disable colored output |

## Examples

### Quickest start — run tests and analyze in one command

```bash
dotnet-testradar scan --solution MyApp.sln
```

### Scan with a specific test directory, export to JSON

```bash
dotnet-testradar scan \
  --solution MyApp.sln \
  --tests-dir tests/MyApp.Tests \
  --output report.json
```

### Scan the last 6 months, show top 10

```bash
dotnet-testradar scan \
  --solution src/MyApp.sln \
  --since 2025-08-01 \
  --top 10
```

### Analyze with an existing coverage file, exclude generated code

```bash
dotnet-testradar analyze \
  --solution MyApp.sln \
  --coverage coverage.xml \
  --exclude "*.Generated.cs" \
  --exclude "**/ViewModels/*.cs" \
  --output report.json
```

### Compare against a baseline (CI diff mode)

```bash
# First run: save a baseline
dotnet-testradar analyze \
  --solution MyApp.sln \
  --coverage coverage.xml \
  --output baseline.json

# Later: compare current state against the baseline
dotnet-testradar analyze \
  --solution MyApp.sln \
  --coverage coverage.xml \
  --baseline baseline.json
```

When `--baseline` is provided, a **Delta** column appears in the table showing how each file's Starting Priority changed (`+0.15` = degraded, `-0.10` = improved, `NEW` = not in baseline). A summary line reports: `vs baseline: N improved, N degraded, N new, N removed.`

### Pipe JSON to jq or other tools

```bash
# Get the top 5 files as JSON and filter with jq
dotnet-testradar analyze \
  --solution MyApp.sln \
  --coverage coverage.xml \
  --format json | jq '.[].file'

# Export CSV to stdout for further processing
dotnet-testradar analyze \
  --solution MyApp.sln \
  --coverage coverage.xml \
  --format csv > results.csv
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
╭──────┬────────────────────────────────┬─────────┬──────────┬────────────┬────────────┬──────┬──────────┬────────╮
│ Rank │ File                           │ Commits │ Coverage │ Complexity │ Dependency │ Risk │ Priority │ Level  │
├──────┼────────────────────────────────┼─────────┼──────────┼────────────┼────────────┼──────┼──────────┼────────┤
│ 1    │ Services/OrderService.cs       │ 47      │ 12%      │ 94         │ Low        │ 1.42 │ 1.42     │ High   │
│ 2    │ Services/ReportFormatter.cs    │ 22      │ 31%      │ 67         │ Low        │ 0.71 │ 0.71     │ High   │
│ 3    │ Controllers/PaymentGateway.cs  │ 31      │ 8%       │ 118        │ Very High  │ 1.61 │ 0.32     │ Medium │
│ 4    │ Data/LegacyDbSync.cs           │ 41      │ 0%       │ 201        │ Very High  │ 1.89 │ 0.19     │ Low    │
╰──────┴────────────────────────────────┴─────────┴──────────┴────────────┴────────────┴──────┴──────────┴────────╯
4 files analyzed. 2 high-priority (start today), 1 medium-priority (next sprint). 2 high-risk file(s) need seam introduction before testing.
```

**Reading the table:**

- **Dependency** — cost of adding seams: `Low` | `Medium` | `High` | `Very High`
- **Risk** — how dangerous it is to leave untested (Phase 1 score, range 0–2.0)
- **Priority** — where to start today (Phase 2 score, range 0–2.0)
- **Level** — actionable tier based on Starting Priority

`PaymentGateway.cs` has a *higher* Risk score than `OrderService.cs`, but its `Very High` dependency level pushes its Starting Priority down to Medium. The tool is telling you: *"This file is dangerous, but introduce seams before attempting to test it."*

`LegacyDbSync.cs` is the most dangerous file in the codebase — but its Starting Priority is Low because it is too entangled to test directly today.

**Color coding:**

| Row Color | Meaning |
|---|---|
| Green | High priority — risky and testable now |
| Yellow | Medium priority — plan for next sprint |
| Default | Low priority — backlog or too entangled |

The **Risk** column is independently highlighted in red/yellow when a file is high/medium risk regardless of its starting priority. This makes it easy to spot the "introduce seams first" files.

## Priority and Risk Levels

**Starting Priority (primary output — sort order)**

| Level | Score Range | Meaning |
|---|---|---|
| **High** | ≥ 0.6 | Start here — risky and practically testable now |
| **Medium** | ≥ 0.2 | Plan for the next sprint |
| **Low** | < 0.2 | Backlog — too costly relative to risk, or low risk overall |

**Risk Level (secondary output)**

| Level | Score Range | Meaning |
|---|---|---|
| **High** | ≥ 0.6 | Changes often, poorly tested, complex logic |
| **Medium** | ≥ 0.2 | Moderate risk — worth investigating |
| **Low** | < 0.2 | Low churn, well-tested, or simple code |

## How Scores Are Calculated

### Phase 1 — Risk Score

```
RiskScore = ChurnNorm × (1 - CoverageRate) × (1 + ComplexityNorm)
```

Each factor is normalized to [0, 1]:

- **ChurnNorm**: weighted lines changed relative to the most-changed file
- **CoverageRate**: line coverage from the Cobertura report (0.0 to 1.0)
- **ComplexityNorm**: cyclomatic complexity relative to the most complex file

Range: 0 to 2.0. A file that changes constantly, has no tests, and is highly complex scores near 2.0.

### Phase 2 — Dependency Score and Starting Priority

The dependency score measures how many **unseamed dependencies** a file has — things a test cannot substitute, control, or observe. Four signals are detected using Roslyn syntax analysis:

| Signal | Weight | What it detects |
|---|---|---|
| **Unseamed infrastructure calls** | 2.0 | `DateTime.Now`, `File.*`, `Environment.*`, `Guid.NewGuid()`, `new HttpClient()`, `new SomeDbContext()` |
| **Direct instantiation in methods** | 1.5 | `new ConcreteType()` inside a method body (excluding DTOs, exceptions, collections) |
| **Concrete constructor parameters** | 0.5 | Constructor parameters that do not follow the `ITypeName` interface convention |
| **Static calls on non-utility types** | 1.0 | `MyHelper.Transform()`, `CacheManager.Invalidate()` (excluding `Math`, `Convert`, `Enumerable`, etc.) |

```
RawDependencyScore = (InfrastructureCalls × 2.0) + (DirectInstantiations × 1.5)
                   + (ConcreteConstructorParams × 0.5) + (StaticCalls × 1.0)

DependencyNorm = RawDependencyScore / Max(RawDependencyScore across all files)

StartingPriority = RiskScore × (1 - DependencyNorm)
```

`DependencyNorm = 0` (fully seamed) → `StartingPriority = RiskScore`.
`DependencyNorm = 1` (maximally entangled) → `StartingPriority = 0`.

The two scores are kept separate intentionally: a file can be High Risk but Low Starting Priority. That combination is one of the most valuable signals in the output — it means *"this file is dangerous but needs seam introduction before you can test it."*

## Default Exclusions

The following patterns are always excluded to reduce noise from auto-generated files:

- `*.Designer.cs`
- `*.g.cs` / `*.g.i.cs`
- `*Migrations/*.cs`
- `*AssemblyInfo.cs`
- `*.xaml.cs`

Use `--exclude` to add additional patterns on top of these defaults.

## Coverage Prerequisites (for `analyze`)

The `scan` command handles coverage automatically. If you use `analyze` and need to generate a coverage file manually:

```bash
dotnet test --collect:"XPlat Code Coverage"
```

This creates a `coverage.cobertura.xml` file inside `TestResults/`. For multiple test projects, use [ReportGenerator](https://github.com/danielpalme/ReportGenerator) to merge:

```bash
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:"merged" -reporttypes:Cobertura
dotnet-testradar analyze --solution MyApp.sln --coverage merged/Cobertura.xml
```

The `scan` command does this merge automatically — it is generally the simpler option.

## Solution Format Support

The tool supports both classic `.sln` files and the newer `.slnx` XML-based format introduced in .NET 10+. It automatically detects the format based on the file extension.

## Requirements

- .NET 10.0 or later
- Git must be installed and the solution must reside inside a git repository
- `scan`: requires dotnet SDK with `coverlet.collector` in test projects
- `analyze`: requires a pre-generated Cobertura XML coverage report

## License

MIT
