# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Progress bars during analysis using `AnsiConsole.Progress()` for complexity and dependency analyzers
- `--format json|csv|table` option for structured stdout output, enabling piping to tools like `jq`
- `--baseline <path.json>` option for comparing against a previous analysis run (Delta column, summary line)
- Integration tests exercising the full `analyze` pipeline with a real temp git repo
- Pipeline parallelism: churn, complexity, and dependency analyzers now run concurrently via `Task.WhenAll`
- This CHANGELOG file

## [0.1.0] - 2026-02-27

### Added

- `analyze` command: cross-references git churn, code coverage, cyclomatic complexity, and dependency entanglement to produce Risk Score and Starting Priority
- `scan` command: runs `dotnet test`, collects coverage automatically, merges multi-project results, then runs the full analysis pipeline
- Phase 2 dependency/seam analysis: detects unseamed infrastructure calls, direct instantiations, concrete constructor params, and static calls on non-utility types
- `--solution`, `--coverage`, `--since`, `--top`, `--exclude`, `--output`, `--no-color` CLI options
- `--tests-dir` option for `scan` command
- JSON and CSV file export via `--output`
- Color-coded Spectre.Console table output with per-cell styling for priority, risk, and dependency levels
- Default exclusion patterns for auto-generated files (Designer.cs, Migrations, etc.)
- Support for both `.sln` and `.slnx` solution formats
- MinVer-based semantic versioning from git tags
- NuGet global tool packaging

### Fixed

- Test projects are now filtered out from solution parsing results
- File exclusion patterns applied consistently across all analyzers
