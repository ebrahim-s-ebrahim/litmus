# Litmus demo
# Requires: vhs (https://github.com/charmbracelet/vhs)
#
# Prerequisites before recording:
#   1. Install the tool:
#        dotnet tool install --global Litmus
#   2. Have a .sln / .slnx file and a Cobertura XML coverage report ready.
#      The commands below target the project's own solution.
#      Adjust --solution and --coverage paths for your own project.
#
# Record:
#   vhs demo.tape

Output docs/demo.gif

Set Shell "powershell"
Set FontSize 14
Set Width 112
Set Height 40
Set Theme "Dracula"
Set Padding 12
Set TypingSpeed 40ms

# ── 1. Show the help screen ───────────────────────────────────────────────────
Type "dotnet-litmus --help"
Sleep 200ms
Enter
Sleep 2s

# ── 2. Scan in one step (runs dotnet test + full analysis) ────────────────────
Sleep 500ms
Type "dotnet-litmus scan --solution Litmus.slnx --top 10"
Sleep 200ms
Enter
# Give dotnet test + analysis time to finish (adjust if your suite is slower)
Sleep 45s

# ── 3. Export results and compare against a baseline ─────────────────────────
Sleep 500ms
Type "dotnet-litmus scan --solution Litmus.slnx --output baseline.json --quiet"
Sleep 200ms
Enter
Sleep 45s

Sleep 500ms
Type "dotnet-litmus scan --solution Litmus.slnx --top 5 --baseline baseline.json"
Sleep 200ms
Enter
Sleep 45s

# ── 4. Analyze with an existing coverage file (two-step workflow) ─────────────
Sleep 500ms
Type "dotnet-litmus analyze --solution Litmus.slnx --coverage coverage.xml --top 5 --exclude `"*.Generated.cs`""
Sleep 200ms
Enter
Sleep 10s

Sleep 2s
