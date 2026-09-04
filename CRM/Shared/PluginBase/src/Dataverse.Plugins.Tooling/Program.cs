using Dataverse.Plugins.Tooling.Commands;
using Dataverse.Plugins.Tooling.Infrastructure;

namespace Dataverse.Plugins.Tooling;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var cli = CommandLine.Parse(args);
            Log.Verbose = cli.HasFlag("verbose");

            if (cli.HasFlag("help"))
            {
                return Help(0);
            }

            // With no verb, --version means the tool's own version, as it does in every other CLI.
            // On pack it is the solution version, which is why it takes a value there.
            if (cli.Verbs.Count == 0 && cli.HasFlag("version"))
            {
                Console.WriteLine(ToolVersion());
                return 0;
            }

            return cli.Verb(0).ToLowerInvariant() switch
            {
                "new" => ScaffoldCommands.New(cli),
                "build" => CommandHandlers.Build(cli),
                "test" => CommandHandlers.Test(cli),
                "manifest" => CommandHandlers.Manifest(cli),
                "validate" => CommandHandlers.Validate(cli),
                "pack" => CommandHandlers.Pack(cli),
                "sync" => CommandHandlers.Sync(cli),
                "solutions" => CommandHandlers.Solutions(cli),
                "messages" => Messages(cli),
                "schema" => Schema(cli),
                "" or "help" => Help(0),
                var unknown => Unknown(unknown),
            };
        }
        catch (ToolException ex)
        {
            // The user's to fix: print the message alone, since a stack trace here only buries it.
            Log.Error(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            // Ours to fix: print everything.
            Log.Error(ex.ToString());
            return 2;
        }
    }

    private static string ToolVersion() =>
        typeof(Program).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>()
            .Select(attribute => attribute.InformationalVersion)
            .FirstOrDefault()
            ?? typeof(Program).Assembly.GetName().Version?.ToString()
            ?? "unknown";

    private static int Schema(CommandLine cli) =>
        cli.Verb(1).ToLowerInvariant() switch
        {
            "pull" => CommandHandlers.SchemaPull(cli),
            "codegen" => CommandHandlers.SchemaCodegen(cli),
            var other => Unknown($"schema {other}".Trim()),
        };

    private static int Messages(CommandLine cli) =>
        cli.Verb(1).ToLowerInvariant() switch
        {
            "pull" => CommandHandlers.MessagesPull(cli),
            var other => Unknown($"messages {other}".Trim()),
        };

    private static int Unknown(string verb)
    {
        Log.Error($"Unknown command '{verb}'.");
        Help(1);
        return 1;
    }

    private static int Help(int exitCode)
    {
        Console.WriteLine(
            """
            dv - build, test and register Dataverse plugins during development

            Steps are declared with [PluginStep] attributes on plugin classes. Those feed a
            manifest, and both outputs - direct registration and the solution package - read only
            that manifest, so the two cannot disagree.

            One repo holds many PowerApps solutions, each under CRM/Plugins/<name> and each packing
            to its own .zip. Importing that .zip is another module's job; dv stops at building it.

            Plugin assemblies are built automatically; there is no separate build step to run.

            USAGE
              dv <command> [options]

            COMMANDS
              new solution <name>   Create a solution, or configure a folder you already made
              new assembly <name>   Add a plugin assembly project
              new tests <name>      Add a test project for one assembly
              new plugin <name>     Add a plugin class
              solutions             List the solutions in the repo
              build                 Build the plugin assemblies and validate every declaration
              test                  Run the solution's plugin tests
              manifest              Build the manifest and write it to artifacts/<solution>/
              validate              Validate without writing. For pull-request checks
              pack                  Build the solution .zip from the assemblies' registrations
              sync                  Register the manifest directly into a dev environment
              messages pull         Cache sdkmessage ids into config/sdkmessages.json
              schema pull           Refresh table metadata into config/schema.json and
                                    regenerate the schema constants
              schema codegen        Regenerate the constants from the committed snapshot

            The schema and message caches are repo-wide: they describe the org, and every solution
            here targets the same one. Only solution.json is per solution.

            NEW OPTIONS
                  --prefix <p>      Publisher prefix for a new solution. Default: the name
                  --unique-name <n> Solution unique name. Default: the name. Chosen ONCE - every
                                    component id derives from it
                  --for <assembly>  Which assembly a test project covers
                  --entity, --message, --stage    Prefill a new plugin's [PluginStep]

            COMMON OPTIONS
              -s, --solution <name> Which solution to act on. Defaults to the one the working
                                    directory is inside, then to defaultSolution in dv.json
              -a, --assembly <name> Restrict to one plugin assembly. Default: all of them
              -c, --configuration   Build configuration. Default: Debug
                  --no-build        Do not build first; use what is already compiled
              -v, --verbose         Show the detail behind each step
              -h, --help            Show this help
                  --version         Show the tool version

            PACK OPTIONS
                  --version <v>     Solution version to stamp, e.g. 1.0.0.5
              -m, --managed         Produce a managed package instead of unmanaged
              -o, --out <path>      A .zip path, or a folder to write the .zip into. Overrides
                                    packageOutput in solution.json and dv.json

            SYNC OPTIONS
              -e, --env <name>      Target environment from config/environments.json
                  --prune           DELETE registrations that are not declared in source.
                                    Off by default, and deliberately has no short form

            SCHEMA OPTIONS
              -t, --tables <names>  Comma separated table logical names. Required for schema pull.
                                    A pull merges, so refreshing one table leaves the others alone

            EXAMPLES
              dv new solution Contoso --prefix contoso
              dv new assembly Contoso.Plugins
              dv solutions
              dv build -a Contoso.Plugins
              dv test -a Contoso.Plugins
              dv sync -e dev -a Contoso.Plugins
              dv pack -s Contoso --version 1.0.0.42 -c Release
              dv schema pull -e dev -t contact,account

            Packing needs no Dataverse connection, which is what lets CI build the package.
            Neither does schema codegen, once config/schema.json is committed.
            """);

        return exitCode;
    }
}
