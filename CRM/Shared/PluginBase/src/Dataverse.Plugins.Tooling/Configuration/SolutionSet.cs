using Dataverse.Plugins.Tooling.Infrastructure;

namespace Dataverse.Plugins.Tooling.Configuration;

/// <summary>
/// The PowerApps solutions in the repo, and the rule for deciding which one a command means.
/// <para>
/// The rule matters more than it looks: several developers work in this repo at once, and having
/// to spell out -s on every command is the kind of friction that gets a tool abandoned. So the
/// working directory counts as an answer.
/// </para>
/// </summary>
public sealed class SolutionSet
{
    private readonly RepoPaths _repo;
    private readonly List<SolutionPaths> _solutions;

    private SolutionSet(RepoPaths repo, List<SolutionPaths> solutions)
    {
        _repo = repo;
        _solutions = solutions;
    }

    public IReadOnlyList<SolutionPaths> All => _solutions;

    public static SolutionSet Discover(RepoPaths repo)
    {
        var solutions = new List<SolutionPaths>();

        if (Directory.Exists(repo.PluginsDirectory))
        {
            foreach (var directory in Directory
                         .EnumerateDirectories(repo.PluginsDirectory)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (File.Exists(Path.Combine(directory, SolutionPaths.MarkerFileName)))
                {
                    solutions.Add(new SolutionPaths(repo, Path.GetFileName(directory), directory));
                }
            }
        }

        return new SolutionSet(repo, solutions);
    }

    /// <summary>
    /// Picks the solution a command applies to, highest precedence first:
    /// <list type="number">
    /// <item>the explicit -s / --solution value;</item>
    /// <item>the working directory, when it sits inside a solution folder;</item>
    /// <item>defaultSolution from dv.json;</item>
    /// <item>the only solution, when there is exactly one.</item>
    /// </list>
    /// Otherwise it fails naming every solution it found, rather than picking one and being wrong.
    /// </summary>
    public SolutionPaths Resolve(string requested, string workingDirectory = null)
    {
        if (_solutions.Count == 0)
        {
            throw new ToolException(
                $"No solutions found under {Relative(_repo.PluginsDirectory)}. A solution is a " +
                $"folder containing {SolutionPaths.MarkerFileName}. Create one with " +
                "'dotnet new dv-solution -n <Name>'.");
        }

        if (!string.IsNullOrWhiteSpace(requested))
        {
            return Named(requested);
        }

        var inferred = FromDirectory(workingDirectory ?? Directory.GetCurrentDirectory());

        if (inferred is not null)
        {
            Log.Detail($"Using solution '{inferred.Name}' (inferred from the working directory).");
            return inferred;
        }

        if (!string.IsNullOrWhiteSpace(_repo.Settings.DefaultSolution))
        {
            var fallback = Named(_repo.Settings.DefaultSolution, $"{RepoPaths.MarkerFileName} names ");
            Log.Detail($"Using solution '{fallback.Name}' (defaultSolution in {RepoPaths.MarkerFileName}).");
            return fallback;
        }

        if (_solutions.Count == 1)
        {
            return _solutions[0];
        }

        throw new ToolException(
            "Several solutions exist, so which one to use has to be said. Pass -s <name>, run the " +
            $"command from inside the solution's folder, or set defaultSolution in " +
            $"{RepoPaths.MarkerFileName}. Found: {Names()}.");
    }

    /// <summary>The solution containing this directory, or null when it is outside all of them.</summary>
    public SolutionPaths FromDirectory(string directory)
    {
        var full = Path.GetFullPath(directory);

        return _solutions.FirstOrDefault(solution =>
            full.Equals(solution.Directory, StringComparison.OrdinalIgnoreCase) ||
            full.StartsWith(solution.Directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private SolutionPaths Named(string name, string context = "")
    {
        var match = _solutions.FirstOrDefault(solution =>
            string.Equals(solution.Name, name, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            return match;
        }

        throw new ToolException($"{context}Unknown solution '{name}'. Found: {Names()}.");
    }

    private string Names() => string.Join(", ", _solutions.Select(solution => solution.Name));

    private string Relative(string path) =>
        Path.GetRelativePath(_repo.Root, path).Replace('\\', '/');
}
