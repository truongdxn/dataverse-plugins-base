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
    private readonly List<string> _candidates;

    private SolutionSet(RepoPaths repo, List<SolutionPaths> solutions, List<string> candidates)
    {
        _repo = repo;
        _solutions = solutions;
        _candidates = candidates;
    }

    public IReadOnlyList<SolutionPaths> All => _solutions;

    /// <summary>
    /// Folders that hold projects but no solution.json, by name.
    /// <para>
    /// Visual Studio can create a plugin project but not a solution folder - that is config, not a
    /// project - so this is what a developer gets from the New Project dialog alone. Without
    /// tracking them, such a folder is not reported as broken, it is simply invisible: 'dv
    /// solutions' lists nothing and says nothing, which is the worst way to learn a step is
    /// missing.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> Candidates => _candidates;

    public static SolutionSet Discover(RepoPaths repo)
    {
        var solutions = new List<SolutionPaths>();
        var candidates = new List<string>();

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
                else if (HoldsAProject(directory))
                {
                    candidates.Add(Path.GetFileName(directory));
                }
            }
        }

        return new SolutionSet(repo, solutions, candidates);
    }

    /// <summary>
    /// An empty folder is nobody's mistake; one containing a project is somebody halfway through
    /// creating a solution. Only the second is worth reporting.
    /// </summary>
    private static bool HoldsAProject(string directory)
    {
        try
        {
            return Directory
                .EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories)
                .Any();
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
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
        var directory = workingDirectory ?? Directory.GetCurrentDirectory();

        if (!string.IsNullOrWhiteSpace(requested))
        {
            return Named(requested);
        }

        // Checked before the "no solutions" case: standing in an unconfigured folder is a much
        // more specific situation, and deserves the message that names it.
        var unconfigured = CandidateFromDirectory(directory);

        if (unconfigured is not null)
        {
            throw new ToolException(NotConfigured(unconfigured));
        }

        if (_solutions.Count == 0)
        {
            throw new ToolException(NoSolutions());
        }

        var inferred = FromDirectory(directory);

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
            "command from inside the solution's folder, or set defaultSolution in " +
            $"{RepoPaths.MarkerFileName}. Found: {Names()}.");
    }

    /// <summary>The solution containing this directory, or null when it is outside all of them.</summary>
    public SolutionPaths FromDirectory(string directory)
    {
        var full = Path.GetFullPath(directory);

        return _solutions.FirstOrDefault(solution => Contains(solution.Directory, full));
    }

    /// <summary>The unconfigured folder containing this directory, or null.</summary>
    public string CandidateFromDirectory(string directory)
    {
        var full = Path.GetFullPath(directory);

        return _candidates.FirstOrDefault(
            name => Contains(Path.Combine(_repo.PluginsDirectory, name), full));
    }

    private static bool Contains(string folder, string path) =>
        path.Equals(folder, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private SolutionPaths Named(string name, string context = "")
    {
        var match = _solutions.FirstOrDefault(solution =>
            string.Equals(solution.Name, name, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            return match;
        }

        // Naming a folder that exists but was never configured is a different mistake from naming
        // one that does not exist, and only one of them has a one-line fix.
        var candidate = _candidates.FirstOrDefault(
            existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase));

        if (candidate is not null)
        {
            throw new ToolException(context + NotConfigured(candidate));
        }

        throw new ToolException(
            _solutions.Count == 0
                ? context + NoSolutions()
                : $"{context}Unknown solution '{name}'. Found: {Names()}.");
    }

    private string NotConfigured(string name) =>
        $"'{name}' holds plugin projects but no {SolutionPaths.MarkerFileName}, so it is not yet a " +
        $"PowerApps solution. Visual Studio can create the projects but not this file. Run: " +
        $"dv new solution {name}";

    private string NoSolutions()
    {
        var message =
            $"No solutions found under {Relative(_repo.PluginsDirectory)}. A solution is a folder " +
            $"containing {SolutionPaths.MarkerFileName}.";

        return _candidates.Count == 0
            ? $"{message} Create one with 'dv new solution <Name>'."
            : $"{message} These folders hold projects but are not configured: " +
              $"{string.Join(", ", _candidates)}. Run 'dv new solution {_candidates[0]}'.";
    }

    private string Names() => string.Join(", ", _solutions.Select(solution => solution.Name));

    private string Relative(string path) =>
        Path.GetRelativePath(_repo.Root, path).Replace('\\', '/');
}
