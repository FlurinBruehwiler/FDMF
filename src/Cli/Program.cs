using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using Cli.Commands;

var root = new RootCommand("FDMF CLI")
{
    TreatUnmatchedTokensAsErrors = true
};

root.AddCommand(DbCommands.Build());
root.AddCommand(TypesCommand.Build());
root.AddCommand(ObjCommands.Build());

var parser = new CommandLineBuilder(root)
    .UseDefaults()
    .UseExceptionHandler((ex, ctx) =>
    {
        ctx.Console.Error.Write(ex.Message + Environment.NewLine);
        ctx.ExitCode = 1;
    })
    .Build();

return await parser.InvokeAsync(args);
