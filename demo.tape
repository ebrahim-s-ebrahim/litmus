# Litmus demo — full animated recording
# Requires: vhs (https://github.com/charmbracelet/vhs)
# Record: vhs demo.tape
# Note: VHS requires ttyd, which works best on Linux/macOS.
#       The README uses docs/demo.svg (static) as the primary demo image.

Output docs/demo.gif

Set Shell "bash"
Set FontSize 15
Set Width 130
Set Height 30
Set Theme "Dracula"
Set Padding 16
Set TypingSpeed 50ms

Sleep 500ms
Type "dotnet-litmus scan --no-coverage --top 5"
Sleep 300ms
Enter
Sleep 10s

Sleep 2s
