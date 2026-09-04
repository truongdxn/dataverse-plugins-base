using Dataverse.Plugins.Tooling.Infrastructure;

namespace Dataverse.Plugins.Tooling.Configuration;

/// <summary>
/// The folders and files belonging to one PowerApps solution: its identity and its code.
/// <para>
/// Deliberately little. Anything describing the <em>org</em> rather than the solution - the schema
/// snapshot, the message id cache, the generated constants - is repo-wide and lives on
/// <see cref="RepoPaths"/>, because every solution here targets the same org and duplicating that
/// per solution only produces the same file several times over.
/// </para>
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

    /// <summary>Publisher and solution identity. The only file a solution folder must hold.</summary>
    public string SolutionConfigFile => Path.Combine(Directory, MarkerFileName);

    /// <summary>Scratch space, one folder per solution so parallel commands never collide.</summary>
    public string ArtifactsDirectory => Path.Combine(Repo.ArtifactsDirectory, Name);

    public string ManifestFile => Path.Combine(ArtifactsDirectory, "manifest.json");

    /// <summary>
    /// Generated SolutionPackager source tree, in the system temp folder rather than the repo.
    /// <para>
    /// It is regenerated wholesale on every pack and nobody keeps it, so the repo is the wrong
    /// place for it - and actively a bad one. This repo commonly lives in a synced folder, and
    /// OneDrive takes ownership of directories it syncs: it marks them ReadOnly, turns them into
    /// reparse points and adds a Deny ACE for DeleteSubdirectoriesAndFiles. The next pack then
    /// cannot clear its own scratch tree, permanently, and no amount of retrying helps. Writing
    /// hundreds of transient files into a sync client's watch path was the mistake; temp has none
    /// of these problems.
    /// </para>
    /// <para>
    /// Keyed by the repo path so two clones do not collide, and stable across runs so it stays
    /// easy to inspect after a failure - 'dv pack -v' prints it.
    /// </para>
    /// </summary>
    public string SolutionSourceDirectory => Path.Combine(
        Path.GetTempPath(),
        "dv-pack",
        $"{Path.GetFileName(Repo.Root)}-{RepoKey()}",
        Name);

    /// <summary>Short stable hash of the repo path, so two clones of it get separate scratch.</summary>
    private string RepoKey()
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Repo.Root.ToLowerInvariant()));

        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

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
