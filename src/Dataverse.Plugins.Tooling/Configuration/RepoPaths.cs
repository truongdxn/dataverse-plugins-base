using Dataverse.Plugins.Tooling.Infrastructure;

namespace Dataverse.Plugins.Tooling.Configuration;

/// <summary>
/// Resolves the well-known folders. The repo root is found by walking up from the working
/// directory looking for config/solution.json, so the tool works from any subfolder.
/// </summary>
public sealed class RepoPaths
{
    private RepoPaths(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public string ConfigDirectory => Path.Combine(Root, "config");

    public string SourceDirectory => Path.Combine(Root, "src");

    public string ArtifactsDirectory => Path.Combine(Root, "artifacts");

    public string SolutionConfigFile => Path.Combine(ConfigDirectory, "solution.json");

    public string EnvironmentsConfigFile => Path.Combine(ConfigDirectory, "environments.json");

    /// <summary>Git-ignored overrides merged over environments.json when present.</summary>
    public string LocalEnvironmentsConfigFile => Path.Combine(ConfigDirectory, "environments.local.json");

    public string SdkMessageCacheFile => Path.Combine(ConfigDirectory, "sdkmessages.json");

    /// <summary>Committed metadata snapshot the schema constants are generated from.</summary>
    public string SchemaFile => Path.Combine(ConfigDirectory, "schema.json");

    /// <summary>
    /// Generated constants. Lives beside the other shared sources so Abstractions.Sources.props
    /// links it into every plugin assembly.
    /// </summary>
    public string GeneratedSchemaFile => Path.Combine(
        SourceDirectory, "Dataverse.Plugins.Abstractions", "Schema", "Schema.g.cs");

    public string ManifestFile => Path.Combine(ArtifactsDirectory, "manifest.json");

    /// <summary>Generated SolutionPackager source tree. Transient - regenerated on every pack.</summary>
    public string SolutionSourceDirectory => Path.Combine(ArtifactsDirectory, "solution-src");

    public static RepoPaths Discover(string startingDirectory = null)
    {
        var current = new DirectoryInfo(startingDirectory ?? Directory.GetCurrentDirectory());

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "config", "solution.json")))
            {
                return new RepoPaths(current.FullName);
            }

            current = current.Parent;
        }

        throw new ToolException(
            "Could not find the repository root. Expected to find config/solution.json in this " +
            "directory or one above it.");
    }

    public void EnsureArtifactsDirectory() => Directory.CreateDirectory(ArtifactsDirectory);
}
