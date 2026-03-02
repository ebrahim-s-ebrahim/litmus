using System.CommandLine;
using Litmus.Abstractions;
using Litmus.Commands;

var fileSystem = new FileSystemWrapper();
var processRunner = new ProcessRunner();

var rootCommand = new RootCommand("Litmus - Identify high-risk .NET source files and where to start testing today");
rootCommand.Subcommands.Add(AnalyzeCommand.Create(fileSystem, processRunner));
rootCommand.Subcommands.Add(ScanCommand.Create(fileSystem, processRunner));

return rootCommand.Parse(args).Invoke();
