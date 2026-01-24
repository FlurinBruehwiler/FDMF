using System.CommandLine;

namespace FDMF.Cli.Commands;

public static class ObjCommands
{
    public static Command Build()
    {
        var obj = new Command("obj", "Object commands");
        obj.AddCommand(ObjListCommand.Build());
        obj.AddCommand(ObjShowCommand.Build());
        obj.AddCommand(ObjCreateCommand.Build());
        obj.AddCommand(ObjUpdateCommand.Build());
        return obj;
    }
}
