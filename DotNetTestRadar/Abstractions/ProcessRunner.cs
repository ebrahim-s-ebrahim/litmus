using System.Diagnostics;

namespace DotNetTestRadar.Abstractions;

public class ProcessRunner : IProcessRunner
{
    public string Run(string executable, string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start process: {executable}");

        // Read both streams asynchronously to prevent pipe buffer deadlock
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var stdout = stdoutTask.Result;
        var stderr = stderrTask.Result;

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Process '{executable} {arguments}' exited with code {process.ExitCode}: {stderr}");

        return stdout;
    }

    public int RunWithLiveOutput(string executable, string arguments, string workingDirectory,
        Action<string>? onOutput = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start process: {executable}");

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) onOutput?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) onOutput?.Invoke(e.Data);
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        // Use timeout overload (-1 = infinite) so we only wait for the process
        // to exit, not for child-process pipe handles to close.
        process.WaitForExit(-1);

        return process.ExitCode;
    }
}
