using System.IO;

namespace Dataverse.Plugins.VsCommands.Core;

/// <summary>
/// A repository the dv CLI can be run against, found by walking up from a folder in Solution
/// Explorer.
/// <para>
/// The rule is deliberately the same one <c>RepoPaths.Discover</c> uses in the CLI: the nearest
/// <c>dv.json</c> at or above the starting folder marks the root. Anything else - matching on the
/// solution file's folder, or on a configured path - would put the extension and the CLI in
/// disagreement about which repo a command belongs to.
/// </para>
/// </summary>
public sealed class DvRepo
{
    public const string MarkerFileName = "dv.json";

    /// <summary>The shim, which is what the extension actually runs. See <see cref="DvInvocation"/>.</summary>
    public const string ShimFileName = "dv.ps1";

    /// <summary>Present in a consumer repo, where dv arrives as a pinned dotnet tool.</summary>
    public const string ToolManifestPath = ".config/dotnet-tools.json";

    private DvRepo(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public string MarkerFile => Path.Combine(Root, MarkerFileName);

    public string ShimFile => Path.Combine(Root, ShimFileName);

    public string ToolManifestFile =>
        Path.Combine(Root, ToolManifestPath.Replace('/', Path.DirectorySeparatorChar));

    public bool HasShim => File.Exists(ShimFile);

    public bool HasToolManifest => File.Exists(ToolManifestFile);

    /// <summary>
    /// The nearest repo at or above <paramref name="startingDirectory"/>, or null when there is
    /// none. Null is a normal answer, not a failure: most solutions open in Visual Studio have
    /// nothing to do with Dataverse, and the menus stay hidden for them.
    /// </summary>
    public static DvRepo Find(string startingDirectory)
    {
        if (string.IsNullOrWhiteSpace(startingDirectory))
        {
            return null;
        }

        var current = new DirectoryInfo(startingDirectory);

        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, MarkerFileName)))
            {
                return new DvRepo(current.FullName);
            }

            current = current.Parent;
        }

        return null;
    }

    /// <summary>
    /// Finds the repo containing a file rather than a folder, which is what Solution Explorer
    /// hands over for a project node.
    /// </summary>
    public static DvRepo FindFromFile(string filePath) =>
        string.IsNullOrWhiteSpace(filePath) ? null : Find(Path.GetDirectoryName(filePath));
}
