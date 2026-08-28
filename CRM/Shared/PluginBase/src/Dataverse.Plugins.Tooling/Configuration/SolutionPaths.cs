using Dataverse.Plugins.Tooling.Infrastructure;

namespace Dataverse.Plugins.Tooling.Configuration;

/// <summary>
/// The folders and files belonging to one PowerApps solution. Everything here sits beside that
/// solution's code, so two solutions never share a schema snapshot, a message cache or a generated
/// file - pulling a table for one cannot churn the other.
/// </summary>
public sealed class SolutionPaths
{
    /// <summary>The file whose presence marks a folder as a solution.</summary>
    public const string MarkerFileName = "solution.json";

    public SolutionPaths(RepoPaths repo, string name, string directory)
    {
        Repo = repo;
        Name = name;
        Directory = directory;
    }

    public RepoPaths Repo { get; }

    /// <summary>Folder name, e.g. "Sample". What -s takes.</summary>
    public string Name { get; }

    /// <summary>Absolute path to the solution folder, e.g. CRM/Plugins/Sample.</summary>
    public string Directory { get; }

    /// <summary>Publisher and solution identity.</summary>
    public string SolutionConfigFile => Path.Combine(Directory, MarkerFileName);

    /// <summary>Committed metadata snapshot the schema constants are generated from.</summary>
    public string SchemaFile => Path.Combine(Directory, "schema.json");

    public string SdkMessageCacheFile => Path.Combine(Directory, "sdkmessages.json");

    /// <summary>
    /// Generated constants. Abstractions.Sources.props links this folder into every plugin
    /// assembly beneath the solution, which is what makes the constants compile-checked.
    /// </summary>
    public string GeneratedSchemaFile => Path.Combine(Directory, "Generated", "Schema.g.cs");

    /// <summary>Scratch space, one folder per solution so parallel commands never collide.</summary>
    public string ArtifactsDirectory => Path.Combine(Repo.ArtifactsDirectory, Name);

    public string ManifestFile => Path.Combine(ArtifactsDirectory, "manifest.json");

    /// <summary>Generated SolutionPackager source tree. Transient - regenerated on every pack.</summary>
    public string SolutionSourceDirectory => Path.Combine(ArtifactsDirectory, "solution-src");

    public void EnsureArtifactsDirectory() => System.IO.Directory.CreateDirectory(ArtifactsDirectory);

    /// <summary>
    /// Where the packed .zip goes, resolved highest precedence first: the --out option, the
    /// solution's own packageOutput, dv.json's packageOutputDirectory, then the built-in default.
    /// A relative path is relative to the repo root, so the same value means the same folder
    /// whatever directory the command ran from.
    /// </summary>
    /// <param name="outOption">The --out value, which may be a directory or a full .zip path.</param>
    /// <param name="fileName">Conventional file name, used unless --out named a file.</param>
    public string ResolvePackagePath(SolutionConfig config, string outOption, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(outOption))
        {
            var resolved = Absolute(outOption);

            // A value ending in .zip is a file; anything else is the folder to put the file in.
            // Guessing either way round would silently write to a path nobody expected.
            return resolved.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                ? resolved
                : Path.Combine(resolved, fileName);
        }

        var configured = !string.IsNullOrWhiteSpace(config?.Solution?.PackageOutput)
            ? config.Solution.PackageOutput
            : Repo.Settings.PackageOutputDirectory;

        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = "CRM/Solutions/{solution}";
        }

        return Path.Combine(Absolute(configured.Replace("{solution}", Name)), fileName);
    }

    private string Absolute(string path)
    {
        var normalised = RepoPaths.Normalise(path);

        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(Repo.Root, normalised));
    }

    public override string ToString() => Name;
}
