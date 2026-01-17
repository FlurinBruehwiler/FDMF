using System.CommandLine;
using Cli.Utils;

namespace Cli.Commands;

public static class TypesCommand
{
    public static Command Build()
    {
        var cmd = new Command("types", "List entity types");

        cmd.SetHandler(() =>
        {
            var model = ModelLoader.Load();
            foreach (var e in model.EntityDefinitions.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"{e.Key}\t{e.Id}");
            }
        });

        return cmd;
    }
}
