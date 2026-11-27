using System;
using System.Linq;
using Product.Azure.Server;

// The local server for local runs, demos and containers. The first argument picks a command; anything else is
// configuration (for example --urls http://127.0.0.1:5080 or --Product:Store:Provider=sqlite).
string command = args.Length > 0 && !args[0].StartsWith("-", StringComparison.Ordinal) ? args[0].ToLowerInvariant() : "run";
string[] rest = args.Length > 0 && !args[0].StartsWith("-", StringComparison.Ordinal) ? args.Skip(1).ToArray() : args;
switch (command)
{
    case "run":
        ProductServer.Build(rest).Run();
        return 0;
    case "seed":
        return ServerCommands.Seed(rest, Console.Out);
    case "migrate":
        return ServerCommands.Migrate(rest, Console.Out);
    case "schema-script":
        return ServerCommands.SchemaScript(rest, Console.Out);
    case "refresh-due-states":
        return ServerCommands.RefreshDueStates(rest, Console.Out);
    case "help":
        Console.WriteLine(ServerCommands.Usage);
        return 0;
    default:
        Console.Error.WriteLine("Unknown command " + command + ".");
        Console.Error.WriteLine(ServerCommands.Usage);
        return 2;
}
