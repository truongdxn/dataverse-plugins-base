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
            // On pack and import it is the solution version, which is why it takes a value there.
            if (cli.Verbs.Count == 0 && cli.HasFlag("version"))
            {
                Console.WriteLine(ToolVersion());
                return 0;
            }

            return cli.Verb(0).ToLowerInvariant() switch
            {
                "build" => CommandHandlers.Build(cli),
                "manifest" => CommandHandlers.Manifest(cli),
                "validate" => CommandHandlers.Validate(cli),
                "pack" => CommandHandlers.Pack(cli),
                "sync" => CommandHandlers.Sync(cli),
                "import" => CommandHandlers.Import(cli),
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
            dv - build and deploy Dataverse plugin registrations

            Steps are declared with [PluginStep] attributes on plugin classes. Those feed a
            manifest, and both deployment paths - direct registration and the solution package -
            read only that manifest, so the two cannot disagree.

            Plugin assemblies are built automatically; there is no separate build step to run.

            USAGE
              dv <command> [options]

            COMMANDS
              build                 Build the plugin assemblies and validate every declaration
              manifest              Build the manifest and write it to artifacts/manifest.json
              validate              Validate without writing. For pull-request checks
              pack                  Build the solution .zip from the assemblies' registrations
              sync                  Register the manifest directly into an environment
              import                Import an already-built .zip into an environment
              messages pull         Cache sdkmessage ids into config/sdkmessages.json (commit it)
              schema pull           Refresh table metadata into config/schema.json (commit it)
                                    and regenerate the schema constants
              schema codegen        Regenerate the constants from the committed snapshot

            COMMON OPTIONS
              -e, --env <name>      Target environment from config/environments.json
              -a, --assembly <name> Restrict to one plugin assembly. Default: all of them
              -c, --configuration   Build configuration. Default: Debug
                  --no-build        Do not build first; use what is already compiled
              -v, --verbose         Show the detail behind each step
              -h, --help            Show this help
                  --version         Show the tool version

            PACK OPTIONS
                  --version <v>     Solution version to stamp, e.g. 1.0.0.5
              -m, --managed         Produce a managed package instead of unmanaged
              -o, --out <path>      Write the .zip somewhere other than artifacts/

            SYNC OPTIONS
                  --prune           DELETE registrations that are not declared in source.
                                    Off by default, and deliberately has no short form

            IMPORT OPTIONS
              -p, --package <path>  Package to import. Default: the one in artifacts/
                  --no-publish      Skip publishing customisations after import
                  --holding         Import as a holding solution, for a staged upgrade

            SCHEMA OPTIONS
              -t, --tables <names>  Comma separated table logical names. Required for schema pull.
                                    A pull merges, so refreshing one table leaves the others alone

            EXAMPLES
              dv build
              dv sync -e dev -a Contoso.Plugins
              dv pack --version 1.0.0.42 -c Release
              dv import -e test -p artifacts/SamplePlugins_1_0_0_42.zip
              dv schema pull -e dev -t contact,account

            Packing needs no Dataverse connection, which is what lets CI build the package.
            Neither does schema codegen, once config/schema.json is committed.
            """);

        return exitCode;
    }
}
