using System.Text.Json;
using Dataverse.Plugins.Tooling.Configuration;
using Dataverse.Plugins.Tooling.Infrastructure;
using Dataverse.Plugins.Tooling.Model;

namespace Dataverse.Plugins.Tooling.Scanning;

/// <summary>
/// Finds the projects belonging to one solution. Projects declare themselves - there is no central
/// list to keep in step - so adding a project from a template needs no config edit.
/// </summary>
public sealed class ProjectDiscovery
{
    private readonly SolutionPaths _solution;

    public ProjectDiscovery(SolutionPaths solution)
    {
        _solution = solution;
    }

    public IReadOnlyList<PluginProject> Discover(string configuration)
    {
        if (!Directory.Exists(_solution.Directory))
        {
            throw new ToolException($"Solution directory not found: {_solution.Directory}");
        }

        var candidates = Candidates(LooksLikePluginProject);

        Log.Detail($"Found {candidates.Count} candidate project(s) under {Relative(_solution.Directory)}.");

        WarnAboutProjectsOutsideAnySolution();

        var projects = new List<PluginProject>();

        foreach (var candidate in candidates)
        {
            var project = Evaluate(candidate, configuration);

            if (project is not null)
            {
                projects.Add(project);
            }
        }

        if (projects.Count == 0)
        {
            throw new ToolException(
                $"No plugin assembly projects found in solution '{_solution.Name}'. A plugin " +
                "project is one that imports Abstractions.Sources.props (which sets " +
                "<DataversePluginAssembly>true</DataversePluginAssembly>). Add one with " +
                "'dotnet new dv-plugin-assembly -n <Name>'.");
        }

        return projects;
    }

    /// <summary>
    /// The test projects in this solution. Same self-declaring mechanism as plugin assemblies, so
    /// a test project is found by importing Testing.Sources.props and nothing else.
    /// </summary>
    /// <param name="assemblyName">
    /// When set, only the projects declaring they test that assembly. A test project that tests
    /// nothing named is a configuration error the props file already refuses to build.
    /// </param>
    public IReadOnlyList<TestProject> DiscoverTests(string configuration, string assemblyName = null)
    {
        var projects = new List<TestProject>();

        foreach (var candidate in Candidates(LooksLikeTestProject))
        {
            var properties = Properties(
                candidate,
                configuration,
                "DataversePluginTests",
                "DataverseTestsFor",
                "AssemblyName");

            if (!string.Equals(Value(properties, "DataversePluginTests"), "true", StringComparison.OrdinalIgnoreCase))
            {
                Log.Detail($"Skipping {Path.GetFileName(candidate)}: not a plugin test project.");
                continue;
            }

            projects.Add(new TestProject
            {
                ProjectPath = candidate,
                ProjectName = Value(properties, "AssemblyName"),
                TestsFor = Value(properties, "DataverseTestsFor"),
            });
        }

        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return projects;
        }

        var matches = projects
            .Where(project => string.Equals(project.TestsFor, assemblyName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0 && projects.Count > 0)
        {
            // Reporting "0 tests, all passed" here would be the worst possible answer.
            Log.Warn(
                $"No test project declares <DataverseTestsFor>{assemblyName}</DataverseTestsFor>. " +
                "Test projects in this solution cover: " +
                string.Join(", ", projects.Select(project => project.TestsFor).Distinct()) + ".");
        }

        return matches;
    }

    /// <summary>
    /// Confirms each discovered project produced its assembly. Run after the build step, since
    /// discovery itself has to work on an unbuilt tree.
    /// </summary>
    public static void VerifyBuilt(IEnumerable<PluginProject> projects, string configuration)
    {
        foreach (var project in projects)
        {
            if (string.IsNullOrWhiteSpace(project.TargetPath) || !File.Exists(project.TargetPath))
            {
                throw new ToolException(
                    $"{Path.GetFileName(project.ProjectPath)} has not been built for configuration " +
                    $"'{configuration}'. Expected an assembly at '{project.TargetPath}'. " +
                    "Remove --no-build, or run a build first.");
            }
        }
    }

    private List<string> Candidates(Func<string, bool> predicate) =>
        Directory
            .EnumerateFiles(_solution.Directory, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(predicate)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Only solution folders are scanned, so a plugin project created elsewhere - which an IDE's
    /// New Project dialog does easily - would never be deployed and nothing would say so. Naming
    /// it is the difference between a five second fix and a confusing afternoon.
    /// </summary>
    private void WarnAboutProjectsOutsideAnySolution()
    {
        var solutions = SolutionSet.Discover(_solution.Repo);
        var root = _solution.Repo.Root;

        string[] ignored = ["bin", "obj", "artifacts", "templates", ".git", ".vs", "PluginBase"];

        var strays = Directory
            .EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => !Path.GetRelativePath(root, path)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => ignored.Contains(segment, StringComparer.OrdinalIgnoreCase)))
            .Where(path => solutions.FromDirectory(Path.GetDirectoryName(path)) is null)
            .Where(LooksLikePluginProject)
            .ToList();

        foreach (var stray in strays)
        {
            Log.Warn(
                $"'{Path.GetRelativePath(root, stray)}' looks like a plugin assembly but is not " +
                $"inside any solution folder, so it is NOT being deployed. Move it under " +
                $"{Relative(_solution.Repo.PluginsDirectory)}/<Solution>/ to include it.");
        }
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Cheap text pre-filter. The property is usually set by the imported .props rather than in
    /// the project file, so this looks for either, and MSBuild decides for real in Evaluate.
    /// </summary>
    private static bool LooksLikePluginProject(string projectPath)
    {
        var text = File.ReadAllText(projectPath);

        // An explicit opt-out settles it, so the tooling's own projects are never candidates.
        if (text.Contains("<DataversePluginAssembly>false</DataversePluginAssembly>", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Any of the three ways a project can end up importing the shared props.
        string[] markers =
        [
            "DataversePluginAssembly",
            "AbstractionsSourcesProps",
            "Abstractions.Sources.props",
        ];

        return markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeTestProject(string projectPath)
    {
        var text = File.ReadAllText(projectPath);

        string[] markers =
        [
            "DataversePluginTests",
            "PluginTestingProps",
            "Testing.Sources.props",
        ];

        return markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Asks MSBuild for the evaluated property values. This is authoritative - it accounts for
    /// imports, conditions and custom output paths - and TargetPath removes any need to guess
    /// where the build put the DLL.
    /// </summary>
    private static PluginProject Evaluate(string projectPath, string configuration)
    {
        var properties = Properties(
            projectPath,
            configuration,
            "DataversePluginAssembly",
            "DataverseIsolationMode",
            "AssemblyName",
            "TargetPath");

        if (!string.Equals(Value(properties, "DataversePluginAssembly"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Log.Detail($"Skipping {Path.GetFileName(projectPath)}: not a plugin assembly.");
            return null;
        }

        var isolationText = Value(properties, "DataverseIsolationMode");
        if (!Enum.TryParse<IsolationMode>(isolationText, ignoreCase: true, out var isolationMode))
        {
            if (!string.IsNullOrWhiteSpace(isolationText))
            {
                throw new ToolException(
                    $"{Path.GetFileName(projectPath)} sets DataverseIsolationMode to '{isolationText}'. " +
                    $"Valid values are: {string.Join(", ", Enum.GetNames<IsolationMode>())}.");
            }

            isolationMode = IsolationMode.Sandbox;
        }

        // Deliberately NOT checked for existence here. MSBuild can evaluate TargetPath on an
        // unbuilt project, and discovery has to succeed before the build step so that it knows
        // which projects to build. VerifyBuilt does the check afterwards.
        var targetPath = Value(properties, "TargetPath");

        Log.Detail($"Discovered plugin assembly {Value(properties, "AssemblyName")} -> {targetPath}");

        return new PluginProject
        {
            ProjectPath = projectPath,
            AssemblyName = Value(properties, "AssemblyName"),
            TargetPath = targetPath,
            IsolationMode = isolationMode,
        };
    }

    private static Dictionary<string, string> Properties(
        string projectPath,
        string configuration,
        params string[] names)
    {
        var arguments = new List<string>
        {
            "msbuild",
            projectPath,
            $"-p:Configuration={configuration}",
        };

        arguments.AddRange(names.Select(name => $"-getProperty:{name}"));
        arguments.Add("-nologo");

        var result = ProcessRunner.Run(DotNetHost.Path, arguments);

        if (!result.Succeeded)
        {
            throw new ToolException(
                $"Could not evaluate '{projectPath}' using '{DotNetHost.Path}'. This needs a .NET " +
                $"SDK, not just a runtime.{Environment.NewLine}{result.CombinedOutput}");
        }

        return ParseProperties(result.StandardOutput, projectPath);
    }

    private static Dictionary<string, string> ParseProperties(string output, string projectPath)
    {
        // 'dotnet msbuild -getProperty:x -getProperty:y' emits {"Properties":{"x":"...","y":"..."}}.
        try
        {
            using var document = JsonDocument.Parse(output);

            if (!document.RootElement.TryGetProperty("Properties", out var properties))
            {
                throw new ToolException($"Unexpected MSBuild output evaluating '{projectPath}'.");
            }

            return properties
                .EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException ex)
        {
            throw new ToolException(
                $"Could not read MSBuild output for '{projectPath}'. This needs the .NET 8 SDK or " +
                $"newer (for -getProperty).{Environment.NewLine}{output}",
                ex);
        }
    }

    private static string Value(IReadOnlyDictionary<string, string> properties, string name) =>
        properties.TryGetValue(name, out var value) ? value?.Trim() ?? string.Empty : string.Empty;

    private string Relative(string path) =>
        Path.GetRelativePath(_solution.Repo.Root, path).Replace('\\', '/');
}
