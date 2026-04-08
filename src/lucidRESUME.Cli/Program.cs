using System.CommandLine;
using lucidRESUME.Cli.Commands;

var root = new RootCommand("lucidRESUME - AI-powered resume & job hunting assistant");
root.Subcommands.Add(ParseCommand.Build());
root.Subcommands.Add(AnalyseCommand.Build());
root.Subcommands.Add(ExportCommand.Build());
root.Subcommands.Add(MatchCommand.Build());
root.Subcommands.Add(CompoundMatchCommand.Build());
root.Subcommands.Add(TrainCommand.Build());
root.Subcommands.Add(TuneCommand.Build());
root.Subcommands.Add(BatchTestCommand.Build());
root.Subcommands.Add(GitHubImportCommand.Build());
root.Subcommands.Add(TailorCommand.Build());
root.Subcommands.Add(DriftCommand.Build());

return await root.Parse(args).InvokeAsync();