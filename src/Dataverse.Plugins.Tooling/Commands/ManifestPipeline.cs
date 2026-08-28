using Dataverse.Plugins.Tooling.Configuration;
using Dataverse.Plugins.Tooling.Infrastructure;
using Dataverse.Plugins.Tooling.Model;
using Dataverse.Plugins.Tooling.Scanning;

namespace Dataverse.Plugins.Tooling.Commands;

/// <summary>
/// Discover, scan, validate - the path every command takes to get a manifest. Keeping it in one
/// place is what guarantees packing and syncing work from identical input.
/// </summary>
public static class ManifestPipeline
{
    /// <param name="assemblyName">
    /// When set, restricts the manifest to that one assembly. Filtering happens after discovery, so
    /// orphan reporting stays scoped to the plugin types actually being deployed and a filtered
    /// sync never mistakes another assembly's steps for orphans.
    /// </param>
    /// <param name="noBuild">
    /// Skips building the plugin assemblies. For CI, where a build has already run and a second
    /// one is waste.
    /// </param>
    public static PluginManifest Build(
        RepoPaths paths,
        SolutionConfig config,
        string configuration,
        string assemblyName = null,
        bool noBuild = false)
    {
        Log.Heading("Discovering plugin assemblies");
        var projects = new ProjectDiscovery(paths).Discover(configuration);

        if (!string.IsNullOrWhiteSpace(assemblyName))
        {
            projects = Restrict(projects, assemblyName);
        }

        foreach (var project in projects)
        {
            Log.Item($"{project.AssemblyName}  ({Relative(paths, project.ProjectPath)})");
        }

        // Build before scanning, so no separate build step has to be remembered. Discovery had to
        // come first: it is what determines which projects to build.
        if (noBuild)
        {
            Log.Detail("--no-build: using whatever is already compiled.");
        }
        else
        {
            PluginBuilder.Build(projects.Select(project => project.ProjectPath).ToList(), configuration);
        }

        ProjectDiscovery.VerifyBuilt(projects, configuration);

        Log.Heading("Reading registrations");
        var scanner = new AssemblyScanner();
        var inputs = new List<AssemblyInput>();

        foreach (var project in projects)
        {
            inputs.Add(new AssemblyInput(project, scanner.Scan(project.TargetPath)));
        }

        var manifest = new ManifestBuilder(config).Build(inputs);

        ManifestValidator.Validate(manifest).Report();

        Log.Info(
            $"{manifest.Assemblies.Count} assembly(s), {manifest.AllTypes().Count()} plugin type(s), " +
            $"{manifest.AllSteps().Count()} step(s).");

        return manifest;
    }

    private static IReadOnlyList<PluginProject> Restrict(
        IReadOnlyList<PluginProject> projects,
        string assemblyName)
    {
        var matches = projects
            .Where(project => string.Equals(project.AssemblyName, assemblyName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
        {
            throw new ToolException(
                $"No plugin assembly called '{assemblyName}'. Found: " +
                string.Join(", ", projects.Select(project => project.AssemblyName).OrderBy(name => name)) +
                ".");
        }

        return matches;
    }

    private static string Relative(RepoPaths paths, string path) =>
        Path.GetRelativePath(paths.Root, path).Replace('\\', '/');
}
