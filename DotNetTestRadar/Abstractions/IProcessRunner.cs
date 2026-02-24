namespace DotNetTestRadar.Abstractions;

public interface IProcessRunner
{
    string Run(string executable, string arguments, string workingDirectory);
}
