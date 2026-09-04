using Dataverse.Plugins.Tooling.Configuration;
using Dataverse.Plugins.Tooling.Infrastructure;

namespace Dataverse.Plugins.Tooling.Commands;

/// <summary>
/// <c>dv new</c> - creating the things a developer creates.
/// <para>
/// Solutions are written here directly rather than by a template, because a solution is one small
/// JSON file and because Visual Studio cannot create one at all: its New Project dialog produces
/// projects, and a solution folder is config. That gap is what this command exists to close, and
/// it is why creating and <em>adopting</em> are the same command - the common case is a folder
/// somebody already made in the IDE.
/// </para>
/// <para>
/// Projects go through <c>dotnet new</c> and the repo's own templates, so there is one definition
/// of what a plugin project looks like rather than two.
/// </para>
/// </summary>
public static class ScaffoldCommands
{
    public static int New(CommandLine cli) =>
        cli.Verb(1).ToLowerInvariant() switch
        {
            "solution" => NewSolution(cli),
            "assembly" => FromTemplate(cli, "dv-plugin-assembly"),
            "tests" => FromTemplate(cli, "dv-plugin-tests"),
            "plugin" => FromTemplate(cli, "dv-plugin"),
            var other => throw new ToolException(
                string.IsNullOrWhiteSpace(other)
                    ? "What to create? One of: solution, assembly, tests, plugin."
                    : $"Cannot create '{other}'. One of: solution, assembly, tests, plugin."),
        };

    /// <summary>
    /// Creates or adopts a solution folder. Never overwrites an existing solution.json: it carries
    /// uniqueName, which seeds every component id, so replacing it would make the next sync create
    /// duplicates instead of updating what is already deployed.
    /// </summary>
    /// <param name="repo">Injected by tests; discovered from the working directory otherwise.</param>
    /// <param name="workingDirectory">
    /// Where "which solution?" is inferred from when no name is given. Injected rather than read
    /// from the process so tests do not have to mutate the current directory, which xunit runs
    /// test classes in parallel against.
    /// </param>
    public static int NewSolution(
        CommandLine cli,
        RepoPaths repo = null,
        string workingDirectory = null)
    {
        repo ??= RepoPaths.Discover();
        var name = SolutionName(cli, repo, workingDirectory ?? Directory.GetCurrentDirectory());

        var directory = Path.Combine(repo.PluginsDirectory, name);
        var configFile = Path.Combine(directory, SolutionPaths.MarkerFileName);

        if (File.Exists(configFile))
        {
            throw new ToolException(
                $"'{name}' is already a solution. {Relative(repo, configFile)} exists and will not " +
                "be overwritten - it holds solution.uniqueName, which every component id is " +
                "derived from, so replacing it would make the next sync duplicate everything " +
                "rather than update it. Edit the file directly to change names or the version.");
        }

        var uniqueName = cli.Option("unique-name", Sanitise(name));
        var prefix = cli.Option("prefix", Sanitise(name).ToLowerInvariant());

        var config = new SolutionConfig
        {
            Publisher = new SolutionConfig.PublisherSection
            {
                UniqueName = prefix,
                FriendlyName = name,
                Description = $"Publisher for {name}.",
                Prefix = prefix,
                OptionValuePrefix = cli.OptionAsInt("option-value-prefix", 10000),
            },
            Solution = new SolutionConfig.SolutionSection
            {
                UniqueName = uniqueName,
                FriendlyName = name,
                Description = $"Plugin assemblies and registered steps for {name}.",
                Version = cli.Option("version", "1.0.0.0"),
            },
        };

        // Validated before anything is written, so an invalid prefix or unique name fails without
        // leaving a half-made solution behind.
        config.Validate(configFile);

        var adopting = Directory.Exists(directory);

        Directory.CreateDirectory(directory);
        File.WriteAllText(configFile, Render(config));

        Log.Success(
            adopting
                ? $"Configured the existing folder {Relative(repo, directory)} as solution '{name}'."
                : $"Created solution '{name}' at {Relative(repo, directory)}.");

        Log.Item($"unique name  {uniqueName}");
        Log.Item($"prefix       {prefix}");

        if (!adopting)
        {
            Log.Detail($"Next: dv new assembly {name}.Plugins   (run it from inside the folder)");
        }

        return 0;
    }

    /// <summary>
    /// Resolves the folder name. Running inside an unconfigured folder means that one, which is
    /// exactly the situation somebody is in after creating a project in Visual Studio.
    /// </summary>
    private static string SolutionName(CommandLine cli, RepoPaths repo, string workingDirectory)
    {
        var explicitName = cli.Verb(2);

        if (!string.IsNullOrWhiteSpace(explicitName))
        {
            return explicitName;
        }

        var inferred = SolutionSet.Discover(repo).CandidateFromDirectory(workingDirectory);

        if (!string.IsNullOrWhiteSpace(inferred))
        {
            Log.Detail($"Configuring '{inferred}' (the folder this was run from).");
            return inferred;
        }

        throw new ToolException(
            "Which solution? Pass a name - 'dv new solution CRMCore' - or run this from inside the " +
            "folder you want to configure.");
    }

    private static int FromTemplate(CommandLine cli, string templateName)
    {
        var repo = RepoPaths.Discover();
        var name = cli.Verb(2);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ToolException($"What should it be called? e.g. 'dv new {cli.Verb(1)} Contoso.Plugins'.");
        }

        var arguments = new List<string> { "new", templateName, "-n", name };

        foreach (var (option, parameter) in TemplateOptions(templateName))
        {
            var value = cli.Option(option);

            if (!string.IsNullOrWhiteSpace(value))
            {
                arguments.Add(parameter);
                arguments.Add(value);
            }
        }

        var result = ProcessRunner.Run(DotNetHost.Path, arguments);

        // A first run has the templates uninstalled, and telling somebody to go and install them
        // is a step that did not need to exist.
        if (!result.Succeeded && LooksLikeMissingTemplate(result.CombinedOutput))
        {
            InstallTemplates(repo);
            result = ProcessRunner.Run(DotNetHost.Path, arguments);
        }

        if (!result.Succeeded)
        {
            throw new ToolException(
                $"Creating '{name}' failed.{Environment.NewLine}{result.CombinedOutput}");
        }

        Console.WriteLine(result.StandardOutput.TrimEnd());
        Log.Success($"Created {name}.");

        WarnIfOutsideASolution(repo);

        return 0;
    }

    private static IEnumerable<(string Option, string Parameter)> TemplateOptions(string templateName) =>
        templateName switch
        {
            "dv-plugin-assembly" => [("isolation", "--isolationMode")],
            "dv-plugin-tests" => [("for", "--testsFor")],
            "dv-plugin" => [("entity", "--entity"), ("message", "--message"), ("stage", "--stage")],
            _ => [],
        };

    /// <summary>
    /// The build would catch this later, but only once somebody builds. Saying it at creation time
    /// is the difference between moving a folder now and debugging a mystery later.
    /// </summary>
    private static void WarnIfOutsideASolution(RepoPaths repo)
    {
        var current = Directory.GetCurrentDirectory();
        var set = SolutionSet.Discover(repo);

        if (set.FromDirectory(current) is not null)
        {
            return;
        }

        var candidate = set.CandidateFromDirectory(current);

        Log.Warn(
            candidate is not null
                ? $"'{candidate}' is not configured as a solution yet, so nothing will deploy this. " +
                  $"Run: dv new solution {candidate}"
                : "This is not inside a solution folder, so nothing will deploy it. Move it under " +
                  $"{Path.GetRelativePath(repo.Root, repo.PluginsDirectory).Replace('\\', '/')}/<Solution>/.");
    }

    private static bool LooksLikeMissingTemplate(string output) =>
        output.Contains("No templates found", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("didn't match any available", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("No template", StringComparison.OrdinalIgnoreCase);

    private static void InstallTemplates(RepoPaths repo)
    {
        var templates = Path.Combine(repo.Root, "templates", "dotnet");

        if (!Directory.Exists(templates))
        {
            throw new ToolException($"Templates not found at {templates}.");
        }

        Log.Detail("Installing the repo's dotnet templates (first use).");

        // The whole folder in one call: 'dotnet new install' scans it recursively. Installing each
        // subfolder separately registers the same template twice under two package ids, and then
        // every 'dotnet new' reports an ambiguous match and refuses to run.
        var result = ProcessRunner.Run(DotNetHost.Path, ["new", "install", templates, "--force"]);

        if (!result.Succeeded)
        {
            throw new ToolException(
                $"Installing the templates in {templates} failed." +
                $"{Environment.NewLine}{result.CombinedOutput}");
        }
    }

    /// <summary>
    /// Written by hand rather than serialised, so the file a developer opens carries the comments
    /// explaining what must not change. A serialiser would produce the same values with none of
    /// the reasons.
    /// </summary>
    private static string Render(SolutionConfig config) =>
        $$"""
        {
          "$comment": [
            "Publisher and solution identity for this PowerApps solution.",
            "",
            "There is deliberately no 'assemblies' list: plugin projects declare themselves by",
            "importing Abstractions.Sources.props, and dv discovers every one under this folder.",
            "",
            "solution.uniqueName seeds every component id dv generates. Changing it later",
            "re-identifies everything, so the next sync creates duplicates instead of updating.",
            "Choose it once.",
            "",
            "The schema snapshot and message id cache are NOT here - they describe the org, not",
            "this solution, and live in config/ for the whole repo to share."
          ],

          "publisher": {
            "uniqueName": "{{config.Publisher.UniqueName}}",
            "friendlyName": "{{config.Publisher.FriendlyName}}",
            "description": "{{config.Publisher.Description}}",
            "prefix": "{{config.Publisher.Prefix}}",
            "optionValuePrefix": {{config.Publisher.OptionValuePrefix}}
          },

          "solution": {
            "uniqueName": "{{config.Solution.UniqueName}}",
            "friendlyName": "{{config.Solution.FriendlyName}}",
            "description": "{{config.Solution.Description}}",
            "version": "{{config.Solution.Version}}"
          }
        }

        """;

    /// <summary>Folder names allow more than Dataverse does, so a name like "CRM Core" still works.</summary>
    internal static string Sanitise(string name) =>
        new(name.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());

    private static string Relative(RepoPaths repo, string path) =>
        Path.GetRelativePath(repo.Root, path).Replace('\\', '/');
}
