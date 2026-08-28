using Dataverse.Plugins.Tooling.Configuration;
using Dataverse.Plugins.Tooling.Deployment;
using Dataverse.Plugins.Tooling.Infrastructure;
using Dataverse.Plugins.Tooling.Packaging;

namespace Dataverse.Plugins.Tooling.Commands;

public static class CommandHandlers
{
    /// <summary>Builds the plugin assemblies and validates every declaration. The pre-push check.</summary>
    public static int Build(CommandLine cli)
    {
        var paths = RepoPaths.Discover();
        var config = SolutionConfig.Load(paths);

        ManifestPipeline.Build(paths, config, Configuration(cli), cli.Option("assembly"), cli.HasFlag("no-build"));

        Log.Success("Build and validation passed.");
        return 0;
    }

    /// <summary>Builds the manifest and writes it to artifacts/ for inspection.</summary>
    public static int Manifest(CommandLine cli)
    {
        var paths = RepoPaths.Discover();
        var config = SolutionConfig.Load(paths);
        var manifest = ManifestPipeline.Build(paths, config, Configuration(cli), cli.Option("assembly"), cli.HasFlag("no-build"));

        paths.EnsureArtifactsDirectory();
        JsonConfig.Write(paths.ManifestFile, manifest);

        Log.Success($"Wrote {paths.ManifestFile}");
        return 0;
    }

    /// <summary>Builds and validates without writing anything. Intended for pull-request checks.</summary>
    public static int Validate(CommandLine cli)
    {
        var paths = RepoPaths.Discover();
        var config = SolutionConfig.Load(paths);
        var manifest = ManifestPipeline.Build(paths, config, Configuration(cli), cli.Option("assembly"), cli.HasFlag("no-build"));

        // Packing is the other thing that can fail on a missing message, and it fails in CI where
        // no environment is available - so surface it during validation instead.
        var cache = SdkMessageCache.Load(paths);
        var missing = cache.FindMissing(manifest.AllSteps().Select(step => step.Message));

        if (missing.Count > 0)
        {
            Log.Warn(
                "No cached sdkmessageid for: " + string.Join(", ", missing) +
                ". 'dv pack' will fail until you run 'dv messages pull --env <name>' and commit " +
                "config/sdkmessages.json.");
        }

        Log.Success("Step configuration is valid.");
        return 0;
    }

    /// <summary>Builds the solution zip from the assemblies' configuration. Needs no connection.</summary>
    public static int Pack(CommandLine cli)
    {
        var paths = RepoPaths.Discover();
        var config = SolutionConfig.Load(paths);
        var manifest = ManifestPipeline.Build(paths, config, Configuration(cli), cli.Option("assembly"), cli.HasFlag("no-build"));

        var version = cli.Option("version", config.Solution.Version);

        if (!Version.TryParse(version, out _))
        {
            throw new ToolException($"--version '{version}' is not a valid version (expected e.g. 1.0.0.0).");
        }

        var managed = cli.HasFlag("managed");
        var cache = SdkMessageCache.Load(paths);

        var missing = cache.FindMissing(manifest.AllSteps().Select(step => step.Message));

        if (missing.Count > 0)
        {
            throw new ToolException(
                "Cannot pack: a packed step references its message by id, and these have none " +
                $"cached: {string.Join(", ", missing)}.{Environment.NewLine}" +
                "Run 'dv messages pull --env <name>' once, then commit config/sdkmessages.json.");
        }

        // A packed step has no state element in the schema, so a disabled step would silently
        // arrive enabled. Say so rather than let it surprise someone in production.
        var disabled = manifest.AllSteps().Where(step => step.State == Model.StepState.Disabled).ToList();

        if (disabled.Count > 0)
        {
            Log.Warn(
                "The solution format cannot carry a disabled step, so these will import as " +
                $"ENABLED: {string.Join(", ", disabled.Select(s => s.Name))}. Use 'dv sync' if the " +
                "disabled state matters.");
        }

        Log.Heading("Generating solution source");
        manifest.SolutionVersion = version;
        new SolutionSourceWriter(config, cache).Write(manifest, paths.SolutionSourceDirectory, version);
        Log.Item(paths.SolutionSourceDirectory);

        Log.Heading("Packing");
        var zipName = $"{config.Solution.UniqueName}_{version.Replace('.', '_')}{(managed ? "_managed" : string.Empty)}.zip";
        var zipPath = cli.Option("out", Path.Combine(paths.ArtifactsDirectory, zipName));

        SolutionPacker.Pack(paths.SolutionSourceDirectory, zipPath, managed);

        Log.Success($"Built {zipPath}");
        return 0;
    }

    /// <summary>Registers the manifest directly into an environment.</summary>
    public static int Sync(CommandLine cli)
    {
        var paths = RepoPaths.Discover();
        var config = SolutionConfig.Load(paths);
        var environmentName = cli.RequiredOption("env");
        var environment = EnvironmentConfig.Load(paths).Get(environmentName);

        var manifest = ManifestPipeline.Build(paths, config, Configuration(cli), cli.Option("assembly"), cli.HasFlag("no-build"));

        var prune = cli.HasFlag("prune");

        if (prune)
        {
            Log.Warn("--prune is set: steps in the environment that are not declared will be DELETED.");
        }

        using var client = DataverseConnection.Connect(environmentName, environment);

        var registrar = new StepRegistrar(client, config.Solution.UniqueName, prune);
        var summary = registrar.Sync(manifest);

        Log.Heading("Summary");
        Log.Item($"assemblies  {summary.AssembliesCreated} created, {summary.AssembliesUpdated} updated");
        Log.Item($"types       {summary.TypesCreated} registered");
        Log.Item($"steps       {summary.StepsCreated} created, {summary.StepsUpdated} updated");
        Log.Item($"images      {summary.ImagesCreated} created, {summary.ImagesUpdated} updated");

        if (summary.Orphans.Count > 0)
        {
            Log.Heading("Not declared locally (left untouched)");

            foreach (var orphan in summary.Orphans)
            {
                Log.Item(orphan);
            }

            Log.Warn(
                $"{summary.Orphans.Count} registration(s) exist in '{environmentName}' but are not " +
                "declared in source. Delete them deliberately with --prune, or declare them with " +
                "a [PluginStep] attribute.");
        }

        Log.Success($"Synced to '{environmentName}'.");
        return 0;
    }

    /// <summary>Imports an already-built package into an environment.</summary>
    public static int Import(CommandLine cli)
    {
        var paths = RepoPaths.Discover();
        var config = SolutionConfig.Load(paths);
        var environmentName = cli.RequiredOption("env");
        var environment = EnvironmentConfig.Load(paths).Get(environmentName);

        var zipPath = cli.Option("package");

        if (string.IsNullOrWhiteSpace(zipPath))
        {
            var version = cli.Option("version", config.Solution.Version);
            zipPath = Path.Combine(
                paths.ArtifactsDirectory,
                $"{config.Solution.UniqueName}_{version.Replace('.', '_')}.zip");
        }

        using var client = DataverseConnection.Connect(environmentName, environment);

        new SolutionImporter(client).Import(
            zipPath,
            publish: !cli.HasFlag("no-publish"),
            holdingSolution: cli.HasFlag("holding"));

        return 0;
    }

    /// <summary>Populates config/sdkmessages.json so packing can run offline afterwards.</summary>
    public static int MessagesPull(CommandLine cli)
    {
        var paths = RepoPaths.Discover();
        var config = SolutionConfig.Load(paths);
        var environmentName = cli.RequiredOption("env");
        var environment = EnvironmentConfig.Load(paths).Get(environmentName);

        // Default to the messages actually used, so the committed file stays small and reviewable.
        IReadOnlyCollection<string> wanted = null;

        if (!cli.HasFlag("all"))
        {
            var manifest = ManifestPipeline.Build(paths, config, Configuration(cli), cli.Option("assembly"), cli.HasFlag("no-build"));
            wanted = manifest.AllSteps()
                .Select(step => step.Message)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        using var client = DataverseConnection.Connect(environmentName, environment);

        var pulled = new MessagePuller(client).Pull(wanted);
        var cache = SdkMessageCache.Load(paths);

        foreach (var (name, id) in pulled)
        {
            cache.Messages[name] = id;
        }

        cache.Save(paths);

        Log.Success(
            $"Cached {pulled.Count} message id(s) in {paths.SdkMessageCacheFile}. Commit this file.");
        return 0;
    }

    /// <summary>Refreshes the metadata snapshot for the named tables, then regenerates the constants.</summary>
    public static int SchemaPull(CommandLine cli)
    {
        var paths = RepoPaths.Discover();
        var environmentName = cli.RequiredOption("env");
        var environment = EnvironmentConfig.Load(paths).Get(environmentName);

        var tables = cli.RequiredOption("tables")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(table => table.ToLowerInvariant())
            .Distinct()
            .ToList();

        var snapshot = Model.SchemaSnapshot.Load(paths);

        using var client = DataverseConnection.Connect(environmentName, environment);
        var puller = new SchemaPuller(client);

        Log.Heading("Reading table metadata");

        foreach (var table in tables)
        {
            // Merged one table at a time, so refreshing one leaves the rest of the snapshot alone.
            snapshot.Merge(puller.PullTable(table));
        }

        Log.Heading("Reading messages valid for those tables");
        var messages = puller.PullMessages(tables);
        snapshot.MergeMessages(messages);
        Log.Item($"{messages.Count} message(s)");

        snapshot.Save(paths);
        Log.Success($"Updated {paths.SchemaFile}");

        return WriteGeneratedSchema(paths, snapshot);
    }

    /// <summary>Regenerates the constants from the committed snapshot. Needs no connection.</summary>
    public static int SchemaCodegen(CommandLine cli)
    {
        var paths = RepoPaths.Discover();
        var snapshot = Model.SchemaSnapshot.Load(paths);

        if (snapshot.Tables.Count == 0)
        {
            Log.Warn(
                $"{paths.SchemaFile} contains no tables. Run " +
                "'dv schema pull --env <name> --tables <logical names>' first.");
        }

        return WriteGeneratedSchema(paths, snapshot);
    }

    private static int WriteGeneratedSchema(RepoPaths paths, Model.SchemaSnapshot snapshot)
    {
        var code = Codegen.SchemaCodeGenerator.Generate(snapshot);

        Directory.CreateDirectory(Path.GetDirectoryName(paths.GeneratedSchemaFile));
        File.WriteAllText(paths.GeneratedSchemaFile, code);

        Log.Success(
            $"Generated {paths.GeneratedSchemaFile} " +
            $"({snapshot.Tables.Count} table(s), {snapshot.Messages.Count} message(s)).");

        return 0;
    }

    private static string Configuration(CommandLine cli) => cli.Option("configuration", "Debug");
}
