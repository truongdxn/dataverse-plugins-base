using Dataverse.Plugins.Tooling.Configuration;
using Dataverse.Plugins.Tooling.Deployment;
using Dataverse.Plugins.Tooling.Infrastructure;
using Dataverse.Plugins.Tooling.Packaging;
using Dataverse.Plugins.Tooling.Scanning;

namespace Dataverse.Plugins.Tooling.Commands;

public static class CommandHandlers
{
    /// <summary>Lists the solutions in the repo, and which one this directory would resolve to.</summary>
    public static int Solutions(CommandLine cli)
    {
        var repo = RepoPaths.Discover();
        var set = SolutionSet.Discover(repo);

        var current = set.FromDirectory(Directory.GetCurrentDirectory());

        if (set.All.Count > 0)
        {
            Log.Heading($"Solutions in {repo.Root}");

            foreach (var solution in set.All)
            {
                var config = SolutionConfig.Load(solution);
                var marker = ReferenceEquals(solution, current) ? " *" : string.Empty;

                Log.Item(
                    $"{solution.Name}{marker}  ->  {config.Solution.UniqueName} " +
                    $"v{config.Solution.Version}  ({config.Publisher.Prefix})");
            }

            if (current is not null)
            {
                Log.Detail("* the working directory is inside this one, so commands default to it.");
            }
        }

        // Reported rather than passed over. A folder created through Visual Studio has projects
        // but no solution.json, and listing nothing at all is how somebody spends an afternoon
        // wondering why their new solution does not exist.
        if (set.Candidates.Count > 0)
        {
            Log.Heading("Not configured yet");

            foreach (var candidate in set.Candidates)
            {
                Log.Item($"{candidate}  ->  run: dv new solution {candidate}");
            }

            Log.Warn(
                $"{set.Candidates.Count} folder(s) hold plugin projects but no " +
                "solution.json, so nothing deploys them. Visual Studio can create the projects " +
                "but not that file.");
        }

        if (set.All.Count == 0 && set.Candidates.Count == 0)
        {
            Log.Warn(
                $"No solutions under {Path.GetRelativePath(repo.Root, repo.PluginsDirectory).Replace('\\', '/')}. " +
                "Create one with 'dv new solution <Name>'.");
        }

        return 0;
    }

    /// <summary>
    /// Lists the developer sandboxes --env accepts.
    /// <para>
    /// Small, but it is the only place that knows environments.local.json is merged over the
    /// shared file. Anything that needs the list - the Visual Studio extension's environment
    /// picker, or somebody wondering what to pass - asks dv instead of reading the JSON itself and
    /// getting the overlay wrong.
    /// </para>
    /// <para>
    /// The line format is a contract: two spaces, the name, then "  ->  ". The extension parses
    /// it, so EnvironmentsCommandTests pins the shape.
    /// </para>
    /// </summary>
    public static int Environments(CommandLine cli, RepoPaths repo = null)
    {
        repo ??= RepoPaths.Discover();

        // Listing is not the command to fail over a missing file. Saying the file is not there,
        // and where it goes, is more use than a stack of "configuration file not found".
        if (!File.Exists(repo.EnvironmentsConfigFile))
        {
            Log.Warn(
                $"No environments file at {repo.EnvironmentsConfigFile}. Create it with a 'dev' " +
                "entry pointing at your sandbox, or add config/environments.local.json to " +
                "override one locally.");

            return 0;
        }

        var config = EnvironmentConfig.Load(repo);

        if (config.Environments.Count == 0)
        {
            Log.Warn($"No environments configured in {repo.EnvironmentsConfigFile}.");
            return 0;
        }

        Log.Heading($"Environments in {repo.Root}");

        foreach (var name in config.Environments.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
        {
            var environment = config.Environments[name];
            var description = string.IsNullOrWhiteSpace(environment.Description)
                ? string.Empty
                : $"  {environment.Description}";

            Log.Item($"{name}  ->  {environment.Url}{description}");
        }

        Log.Detail("Sign-in is interactive; the token is cached outside the repo.");
        return 0;
    }

    /// <summary>Builds the plugin assemblies and validates every declaration. The pre-push check.</summary>
    public static int Build(CommandLine cli)
    {
        var (paths, config) = Resolve(cli);

        ManifestPipeline.Build(paths, config, Configuration(cli), cli.Option("assembly"), cli.HasFlag("no-build"));

        Log.Success("Build and validation passed.");
        return 0;
    }

    /// <summary>
    /// Runs the solution's plugin tests. Restricted to one assembly's tests with -a, which is what
    /// makes the loop bearable when several people are working in the same repo.
    /// </summary>
    public static int Test(CommandLine cli)
    {
        var (paths, _) = Resolve(cli);
        var configuration = Configuration(cli);
        var assemblyName = cli.Option("assembly");

        Log.Heading($"Discovering test projects in solution '{paths.Name}'");
        var projects = new ProjectDiscovery(paths).DiscoverTests(configuration, assemblyName);

        if (projects.Count == 0)
        {
            // Exit non-zero: "no tests" must never be mistaken for "tests passed".
            throw new ToolException(
                string.IsNullOrWhiteSpace(assemblyName)
                    ? $"Solution '{paths.Name}' has no test projects. A test project is one that " +
                      "imports Testing.Sources.props. Add one with " +
                      "'dv new tests <Name> --for <Assembly>'."
                    : $"No test project covers '{assemblyName}'.");
        }

        foreach (var project in projects)
        {
            Log.Item($"{project.ProjectName}  (tests {project.TestsFor})");
        }

        var failed = new List<string>();

        foreach (var project in projects)
        {
            Log.Heading($"Testing {project.ProjectName}");

            var result = ProcessRunner.Run(
                DotNetHost.Path,
                ["test", project.ProjectPath, "--configuration", configuration, "--nologo"]);

            // dotnet test writes the run summary to stdout, and it is the only useful thing to
            // show whether the run passed or failed.
            Console.WriteLine(result.CombinedOutput.TrimEnd());

            if (!result.Succeeded)
            {
                failed.Add(project.ProjectName);
            }
        }

        if (failed.Count > 0)
        {
            throw new ToolException($"Tests failed in: {string.Join(", ", failed)}.");
        }

        Log.Success($"{projects.Count} test project(s) passed.");
        return 0;
    }

    /// <summary>Builds the manifest and writes it to artifacts/ for inspection.</summary>
    public static int Manifest(CommandLine cli)
    {
        var (paths, config) = Resolve(cli);
        var manifest = ManifestPipeline.Build(paths, config, Configuration(cli), cli.Option("assembly"), cli.HasFlag("no-build"));

        paths.EnsureArtifactsDirectory();
        JsonConfig.Write(paths.ManifestFile, manifest);

        Log.Success($"Wrote {paths.ManifestFile}");
        return 0;
    }

    /// <summary>Builds and validates without writing anything. Intended for pull-request checks.</summary>
    public static int Validate(CommandLine cli)
    {
        var (paths, config) = Resolve(cli);
        var manifest = ManifestPipeline.Build(paths, config, Configuration(cli), cli.Option("assembly"), cli.HasFlag("no-build"));

        // Packing is the other thing that can fail on a missing message, and it fails in CI where
        // no environment is available - so surface it during validation instead.
        var cache = SdkMessageCache.Load(paths.Repo);
        var missing = cache.FindMissing(manifest.AllSteps().Select(step => step.Message));

        if (missing.Count > 0)
        {
            Log.Warn(
                "No cached sdkmessageid for: " + string.Join(", ", missing) +
                ". 'dv pack' will fail until you run 'dv messages pull -e <name>' and commit " +
                "config/sdkmessages.json.");
        }

        Log.Success("Step configuration is valid.");
        return 0;
    }

    /// <summary>Builds the solution zip from the assemblies' configuration. Needs no connection.</summary>
    public static int Pack(CommandLine cli)
    {
        var (paths, config) = Resolve(cli);
        var manifest = ManifestPipeline.Build(paths, config, Configuration(cli), cli.Option("assembly"), cli.HasFlag("no-build"));

        var version = cli.Option("version", config.Solution.Version);

        if (!Version.TryParse(version, out _))
        {
            throw new ToolException($"--version '{version}' is not a valid version (expected e.g. 1.0.0.0).");
        }

        var managed = cli.HasFlag("managed");
        var cache = SdkMessageCache.Load(paths.Repo);

        var missing = cache.FindMissing(manifest.AllSteps().Select(step => step.Message));

        if (missing.Count > 0)
        {
            throw new ToolException(
                "Cannot pack: a packed step references its message by id, and these have none " +
                $"cached: {string.Join(", ", missing)}.{Environment.NewLine}" +
                "Run 'dv messages pull -e <name>' once, then commit config/sdkmessages.json.");
        }

        // A packed step has no state element in the schema, so a disabled step would silently
        // arrive enabled. Say so rather than let it surprise someone later.
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
        var zipPath = paths.ResolvePackagePath(config, cli.Option("out"), zipName);

        Directory.CreateDirectory(Path.GetDirectoryName(zipPath));
        SolutionPacker.Pack(paths.SolutionSourceDirectory, zipPath, managed);

        Log.Success($"Built {zipPath}");
        Log.Detail("Importing it is another module's job; this repo's part ends here.");
        return 0;
    }

    /// <summary>Registers the manifest directly into a developer sandbox.</summary>
    public static int Sync(CommandLine cli)
    {
        var (paths, config) = Resolve(cli);
        var environmentName = cli.RequiredOption("env");
        var environment = EnvironmentConfig.Load(paths.Repo).Get(environmentName);

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

    /// <summary>
    /// Populates config/sdkmessages.json so packing can run offline afterwards. Repo-wide: message
    /// ids are the org's, so every solution wants the same map.
    /// </summary>
    public static int MessagesPull(CommandLine cli)
    {
        var repo = RepoPaths.Discover();
        var environmentName = cli.RequiredOption("env");
        var environment = EnvironmentConfig.Load(repo).Get(environmentName);

        // Default to the messages actually used, so the committed file stays small and reviewable.
        IReadOnlyCollection<string> wanted = null;

        if (!cli.HasFlag("all"))
        {
            // Narrowed to the messages actually declared, so the committed file stays small and
            // reviewable. Across every solution, not just one: the cache is shared, and pulling
            // for one solution must not drop what another already relies on.
            var messages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var solution in SolutionSet.Discover(repo).All)
            {
                var manifest = ManifestPipeline.Build(
                    solution,
                    SolutionConfig.Load(solution),
                    Configuration(cli),
                    cli.Option("assembly"),
                    cli.HasFlag("no-build"));

                messages.UnionWith(manifest.AllSteps().Select(step => step.Message));
            }

            wanted = messages.ToList();
        }

        using var client = DataverseConnection.Connect(environmentName, environment);

        var pulled = new MessagePuller(client).Pull(wanted);
        var cache = SdkMessageCache.Load(repo);

        foreach (var (name, id) in pulled)
        {
            cache.Messages[name] = id;
        }

        cache.Save(repo);

        Log.Success(
            $"Cached {pulled.Count} message id(s) in {repo.SdkMessageCacheFile}. Commit this file.");
        return 0;
    }

    /// <summary>
    /// Refreshes the metadata snapshot for the named tables, then regenerates the constants.
    /// Repo-wide, and takes no --solution: the snapshot describes the org, and every solution here
    /// targets the same one, so a per-solution copy would only be the same file several times.
    /// </summary>
    public static int SchemaPull(CommandLine cli)
    {
        var repo = RepoPaths.Discover();
        var environmentName = cli.RequiredOption("env");
        var environment = EnvironmentConfig.Load(repo).Get(environmentName);

        var tables = cli.RequiredOption("tables")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(table => table.ToLowerInvariant())
            .Distinct()
            .ToList();

        var snapshot = Model.SchemaSnapshot.Load(repo);

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

        snapshot.Save(repo);
        Log.Success($"Updated {repo.SchemaFile}");

        return WriteGeneratedSchema(repo, snapshot);
    }

    /// <summary>Regenerates the constants from the committed snapshot. Needs no connection.</summary>
    public static int SchemaCodegen(CommandLine cli)
    {
        var repo = RepoPaths.Discover();
        var snapshot = Model.SchemaSnapshot.Load(repo);

        if (snapshot.Tables.Count == 0)
        {
            Log.Warn(
                $"{repo.SchemaFile} contains no tables. Run " +
                "'dv schema pull -e <name> -t <logical names>' first.");
        }

        return WriteGeneratedSchema(repo, snapshot);
    }

    /// <summary>
    /// Past this many constants the generated file is worth a second look. The constants inline at
    /// the call site so they cost nothing at RUNTIME, but they stay in the assembly's metadata and
    /// the file is linked into every plugin assembly - so the weight multiplies by assembly count
    /// and rides into the solution zip base64-encoded.
    /// </summary>
    private const int LargeSchemaConstants = 5000;

    /// <summary>Measured at ~64 bytes per constant: metadata rows, identifier, UTF-16 value.</summary>
    private const int BytesPerConstant = 64;

    private static int WriteGeneratedSchema(RepoPaths paths, Model.SchemaSnapshot snapshot)
    {
        var code = Codegen.SchemaCodeGenerator.Generate(snapshot);

        Directory.CreateDirectory(Path.GetDirectoryName(paths.GeneratedSchemaFile));
        File.WriteAllText(paths.GeneratedSchemaFile, code);

        var constants = snapshot.Tables.Sum(table => table.Columns.Count + 1) + snapshot.Messages.Count;

        Log.Success(
            $"Generated {paths.GeneratedSchemaFile} " +
            $"({snapshot.Tables.Count} table(s), {snapshot.Messages.Count} message(s), " +
            $"{constants} constant(s)).");

        if (constants >= LargeSchemaConstants)
        {
            Log.Warn(
                $"{constants} constants adds roughly {constants * BytesPerConstant / 1024} KB to " +
                "EVERY plugin assembly, in every solution, and that travels base64-encoded inside " +
                "each .zip. Nothing is slower at runtime - constants inline at the call site - but " +
                "builds and packages grow. Pull only the tables that are actually used; --tables " +
                "is the control.");
        }

        return 0;
    }

    /// <summary>
    /// Every command starts here: find the repo, decide which solution is meant, read its config.
    /// Keeping it in one place is what makes -s behave identically everywhere.
    /// </summary>
    private static (SolutionPaths Paths, SolutionConfig Config) Resolve(CommandLine cli, bool loadConfig = true)
    {
        var repo = RepoPaths.Discover();
        var paths = SolutionSet.Discover(repo).Resolve(cli.Option("solution"));

        return (paths, loadConfig ? SolutionConfig.Load(paths) : null);
    }

    private static string Configuration(CommandLine cli) => cli.Option("configuration", "Debug");
}
