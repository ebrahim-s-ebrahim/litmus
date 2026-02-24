using System.CommandLine;
using DotNetTestRadar.Abstractions;
using DotNetTestRadar.Commands;

var fileSystem = new FileSystemWrapper();
var processRunner = new ProcessRunner();

var rootCommand = new RootCommand("DotNetTestRadar - Identify high-risk .NET source files");
rootCommand.Subcommands.Add(AnalyzeCommand.Create(fileSystem, processRunner));

return rootCommand.Parse(args).Invoke();
