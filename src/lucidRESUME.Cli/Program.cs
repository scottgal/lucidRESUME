using System.CommandLine;
using lucidRESUME.Cli.Commands;

var root = new RootCommand("lucidRESUME — AI-powered resume & job hunting assistant");
root.Subcommands.Add(ParseCommand.Build());
root.Subcommands.Add(AnalyseCommand.Build());
root.Subcommands.Add(ExportCommand.Build());

return await root.Parse(args).InvokeAsync();
