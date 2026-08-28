namespace Dataverse.Plugins.Tooling.Infrastructure;

/// <summary>
/// Builds the plugin assembly projects, so no separate build step has to be remembered before a
/// command that reads compiled assemblies.
/// <para>
/// Only the projects asked for are built, never the whole solution. The tool runs from
/// <c>artifacts/tool</c> while plugin projects output to their own <c>bin</c>, so it can never end
/// up trying to overwrite itself while running.
/// </para>
/// </summary>
public static class PluginBuilder
{
    public static void Build(IReadOnlyCollection<string> projectPaths, string configuration)
    {
        if (projectPaths.Count == 0)
        {
            return;
        }

        Log.Heading($"Building plugin assemblies ({configuration})");

        foreach (var projectPath in projectPaths)
        {
            var result = ProcessRunner.Run(
                DotNetHost.Path,
                new[]
                {
                    "build",
                    projectPath,
                    "--configuration", configuration,
                    "--nologo",
                    "-v", "quiet",
                });

            if (!result.Succeeded)
            {
                throw new ToolException(
                    $"Building '{Path.GetFileName(projectPath)}' failed." +
                    $"{Environment.NewLine}{result.CombinedOutput}");
            }

            Log.Item($"built {Path.GetFileNameWithoutExtension(projectPath)}");
        }
    }
}
