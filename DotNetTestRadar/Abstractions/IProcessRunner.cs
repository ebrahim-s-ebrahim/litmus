namespace DotNetTestRadar.Abstractions;

public interface IProcessRunner
{
    string Run(string executable, string arguments, string workingDirectory);

    /// <summary>
    /// Runs a process while streaming stdout/stderr lines to <paramref name="onOutput"/> in real time.
    /// Returns the process exit code instead of throwing on failure.
    /// </summary>
    int RunWithLiveOutput(string executable, string arguments, string workingDirectory,
        Action<string>? onOutput = null);
}
