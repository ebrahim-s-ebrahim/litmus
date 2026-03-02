namespace Litmus.Abstractions;

public interface IProcessRunner
{
    string Run(string executable, string arguments, string workingDirectory);

    /// <summary>
    /// Runs a process while streaming stdout/stderr lines to <paramref name="onOutput"/> in real time.
    /// Returns the process exit code instead of throwing on failure.
    /// If <paramref name="timeoutMs"/> is exceeded the process tree is killed and
    /// <see cref="TimeoutException"/> is thrown.
    /// </summary>
    int RunWithLiveOutput(string executable, string arguments, string workingDirectory,
        Action<string>? onOutput = null, int timeoutMs = 0);
}
