using Dataverse.Plugins.Tooling.Infrastructure;

namespace Dataverse.Plugins.Tooling.Configuration;

/// <summary>
/// Repo-wide folders and settings, deserialised from dv.json. The repo root is found by walking up
/// from the working directory looking for that file, so every command works from any subfolder.
/// <para>
/// Everything that belongs to one PowerApps solution lives in <see cref="SolutionPaths"/> instead.
/// </para>
/// </summary>
public sealed class RepoPaths
{
    /// <summary>The file whose presence marks the repo root.</summary>
    public const string MarkerFileName = "dv.json";

    private RepoPaths(string root, RepoSettings settings)
    {
        Root = root;
        Settings = settings;
    }

    public string Root { get; }

    public RepoSettings Settings { get; }

    public string MarkerFile => Path.Combine(Root, MarkerFileName);

    /// <summary>Holds one folder per PowerApps solution. Default CRM/Plugins.</summary>
    public string PluginsDirectory => Path.Combine(Root, Normalise(Settings.PluginsDirectory));

    public string ConfigDirectory => Path.Combine(Root, "config");

    public string EnvironmentsConfigFile => Path.Combine(ConfigDirectory, "environments.json");

    /// <summary>Git-ignored overrides merged over environments.json when present.</summary>
    public string LocalEnvironmentsConfigFile => Path.Combine(ConfigDirectory, "environments.local.json");

    /// <summary>
    /// Committed metadata snapshot the schema constants are generated from. Repo-wide, not per
    /// solution: it describes the org, and every solution in this repo targets the same one.
    /// </summary>
    public string SchemaFile => Path.Combine(ConfigDirectory, "schema.json");

    /// <summary>Message name to id cache. Repo-wide for the same reason as the schema.</summary>
    public string SdkMessageCacheFile => Path.Combine(ConfigDirectory, "sdkmessages.json");

    /// <summary>
    /// Generated constants, linked into every plugin assembly in every solution by
    /// Abstractions.Sources.props.
    /// <para>
    /// Beside the snapshot it is generated from, and firmly on the consumer's side of the line:
    /// when the abstractions arrive as a NuGet package their own folder is inside the package
    /// cache - read-only, and erased by 'dotnet nuget locals --clear'. Generated output cannot
    /// live there.
    /// </para>
    /// </summary>
    public string GeneratedSchemaFile => Path.Combine(GeneratedDirectory, "Schema.g.cs");

    /// <summary>Where 'dv schema codegen' writes. Matches DataverseSchemaDirectory in the props.</summary>
    public string GeneratedDirectory => Path.Combine(ConfigDirectory, "Generated");

    /// <summary>Transient build output. Regenerated freely; never committed.</summary>
    public string ArtifactsDirectory => Path.Combine(Root, "artifacts");

    public static RepoPaths Discover(string startingDirectory = null)
    {
        var current = new DirectoryInfo(startingDirectory ?? Directory.GetCurrentDirectory());

        while (current is not null)
        {
            var marker = Path.Combine(current.FullName, MarkerFileName);

            if (File.Exists(marker))
            {
                return new RepoPaths(current.FullName, RepoSettings.Load(marker));
            }

            current = current.Parent;
        }

        throw new ToolException(
            $"Could not find the repository root. Expected {MarkerFileName} in this directory or " +
            "one above it.");
    }

    public void EnsureArtifactsDirectory() => Directory.CreateDirectory(ArtifactsDirectory);

    /// <summary>Accepts forward slashes in config on every platform, which is what people write.</summary>
    internal static string Normalise(string relativePath) =>
        (relativePath ?? string.Empty)
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .Trim(Path.DirectorySeparatorChar);
}

/// <summary>Deserialised dv.json.</summary>
public sealed class RepoSettings
{
    /// <summary>Where solution folders live, relative to the repo root.</summary>
    public string PluginsDirectory { get; set; } = "CRM/Plugins";

    /// <summary>
    /// Where 'dv pack' writes the .zip. "{solution}" is replaced with the solution name. A
    /// solution can override it, and --out overrides both.
    /// </summary>
    public string PackageOutputDirectory { get; set; } = "CRM/Solutions/{solution}";

    /// <summary>
    /// Used when a command names no solution and the working directory is not inside one. Absent
    /// means -s is required as soon as the repo holds more than one solution.
    /// </summary>
    public string DefaultSolution { get; set; }

    public static RepoSettings Load(string path) => JsonConfig.Read<RepoSettings>(path);
}
