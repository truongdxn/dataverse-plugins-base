using System.Text.Json;
using Dataverse.Plugins.Tooling.Configuration;
using Dataverse.Plugins.Tooling.Infrastructure;
using Dataverse.Plugins.Tooling.Model;

namespace Dataverse.Plugins.Tooling.Scanning;

/// <summary>
/// Finds the plugin assembly projects in the repo. Projects declare themselves - there is no
/// central list to keep in step - so adding a project from a template needs no config edit.
/// </summary>
public sealed class ProjectDiscovery
{
    private readonly RepoPaths _paths;

    public ProjectDiscovery(RepoPaths paths)
    {
        _paths = paths;
    }

    public IReadOnlyList<PluginProject> Discover(string configuration)
    {
        if (!Directory.Exists(_paths.SourceDirectory))
        {
            throw new ToolException($"Source directory not found: {_paths.SourceDirectory}");
        }

        var candidates = Directory
            .EnumerateFiles(_paths.SourceDirectory, "*.csproj", SearchOption.AllDirectories)
            .Where(LooksLikePluginProject)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Log.Detail($"Found {candidates.Count} candidate project(s) under src/.");

        WarnAboutProjectsOutsideSource();

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
                "No plugin assembly projects found. A plugin project is one that imports " +
                "Abstractions.Sources.props (which sets <DataversePluginAssembly>true</DataversePluginAssembly>).");
        }

        return projects;
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

    /// <summary>
    /// Only src/ is scanned, so a plugin project created elsewhere - which an IDE's New Project
    /// dialog does easily - would never be deployed and nothing would say so. Naming it is the
    /// difference between a five second fix and a confusing afternoon.
    /// </summary>
    private void WarnAboutProjectsOutsideSource()
    {
        string[] ignored = ["bin", "obj", "artifacts", "templates", ".git", ".vs"];

        var strays = Directory
            .EnumerateFiles(_paths.Root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(_paths.SourceDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Where(path => !Path.GetRelativePath(_paths.Root, path)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => ignored.Contains(segment, StringComparer.OrdinalIgnoreCase)))
            .Where(LooksLikePluginProject)
            .ToList();

        foreach (var stray in strays)
        {
            Log.Warn(
                $"'{Path.GetRelativePath(_paths.Root, stray)}' looks like a plugin assembly but is " +
                "not under src/, so it is NOT being deployed. Move it under src/ to include it.");
        }
    }

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

    /// <summary>
    /// Asks MSBuild for the evaluated property values. This is authoritative - it accounts for
    /// imports, conditions and custom output paths - and TargetPath removes any need to guess
    /// where the build put the DLL.
    /// </summary>
    private static PluginProject Evaluate(string projectPath, string configuration)
    {
        var result = ProcessRunner.Run(
            DotNetHost.Path,
            new[]
            {
                "msbuild",
                projectPath,
                $"-p:Configuration={configuration}",
                "-getProperty:DataversePluginAssembly",
                "-getProperty:DataverseIsolationMode",
                "-getProperty:AssemblyName",
                "-getProperty:TargetPath",
                "-nologo",
            });

        if (!result.Succeeded)
        {
            throw new ToolException(
                $"Could not evaluate '{projectPath}' using '{DotNetHost.Path}'. This needs a .NET " +
                $"SDK, not just a runtime.{Environment.NewLine}{result.CombinedOutput}");
        }

        var properties = ParseProperties(result.StandardOutput, projectPath);

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
}
