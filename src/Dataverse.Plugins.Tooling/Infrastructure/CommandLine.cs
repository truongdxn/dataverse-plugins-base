namespace Dataverse.Plugins.Tooling.Infrastructure;

/// <summary>
/// Minimal argument parser: one or more verbs, then <c>--option value</c> pairs and <c>--flags</c>.
/// Hand-rolled rather than taken from a package so the tool has no dependency that churns its API
/// between versions.
/// <para>
/// Short and long forms are the same thing: the leading dashes are stripped, then a one-letter name
/// is expanded through <see cref="Aliases"/>. So <c>-e dev</c> and <c>--env dev</c> reach the code
/// identically.
/// </para>
/// </summary>
public sealed class CommandLine
{
    /// <summary>
    /// Short forms. Two names are deliberately absent:
    /// <list type="bullet">
    /// <item><c>--prune</c> deletes registrations, and a destructive flag should not sit one
    /// keystroke away from a harmless one.</item>
    /// <item><c>--version</c> cannot be <c>-v</c>, because names compare case-insensitively and
    /// verbose is typed far more often.</item>
    /// </list>
    /// </summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["e"] = "env",
        ["t"] = "tables",
        ["a"] = "assembly",
        ["c"] = "configuration",
        ["o"] = "out",
        ["p"] = "package",
        ["m"] = "managed",
        ["v"] = "verbose",
        ["h"] = "help",
    };

    /// <summary>
    /// Every option and flag the CLI understands, so a typo can be rejected rather than ignored.
    /// </summary>
    private static readonly HashSet<string> KnownNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "all",
        "assembly",
        "configuration",
        "env",
        "help",
        "holding",
        "managed",
        "no-build",
        "no-publish",
        "out",
        "package",
        "prune",
        "tables",
        "verbose",
        "version",
    };

    private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _verbs = new();

    private CommandLine()
    {
    }

    /// <summary>Leading non-option words, e.g. ["messages", "pull"].</summary>
    public IReadOnlyList<string> Verbs => _verbs;

    public static CommandLine Parse(string[] args)
    {
        var command = new CommandLine();
        var index = 0;

        while (index < args.Length && !args[index].StartsWith('-'))
        {
            command._verbs.Add(args[index]);
            index++;
        }

        while (index < args.Length)
        {
            var token = args[index];

            if (!token.StartsWith('-'))
            {
                throw new ToolException($"Unexpected argument '{token}'.");
            }

            var name = token.TrimStart('-');

            // Support --name=value as well as --name value.
            var separator = name.IndexOf('=');
            if (separator >= 0)
            {
                command._options[Resolve(name[..separator])] = name[(separator + 1)..];
                index++;
                continue;
            }

            var next = index + 1 < args.Length ? args[index + 1] : null;

            if (next is null || next.StartsWith('-'))
            {
                command._flags.Add(Resolve(name));
                index++;
            }
            else
            {
                command._options[Resolve(name)] = next;
                index += 2;
            }
        }

        return command;
    }

    /// <summary>
    /// Expands a short form and rejects anything unrecognised. Silently ignoring an unknown option
    /// is the worst outcome: '--enviroment dev' would otherwise be reported as a missing --env,
    /// never mentioning the actual mistake.
    /// </summary>
    private static string Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ToolException("Found '-' with no option name after it.");
        }

        var resolved = Aliases.TryGetValue(name, out var expanded) ? expanded : name;

        if (KnownNames.Contains(resolved))
        {
            return resolved;
        }

        var suggestion = ClosestMatch(resolved);

        throw new ToolException(
            $"Unknown option '--{name}'." +
            (suggestion is null ? string.Empty : $" Did you mean '--{suggestion}'?"));
    }

    private static string ClosestMatch(string name)
    {
        var lowered = name.ToLowerInvariant();

        // Prefix matches first. Edit distance alone misses the most common mistake of all:
        // '--enviroment' is 8 edits from 'env', far outside any sane threshold, yet obviously meant
        // it. Longest match wins, so '--conf' prefers 'configuration' over a shorter coincidence.
        var byPrefix = KnownNames
            .Where(known => lowered.StartsWith(known, StringComparison.Ordinal)
                            || known.StartsWith(lowered, StringComparison.Ordinal))
            .OrderByDescending(known => known.Length)
            .FirstOrDefault();

        if (byPrefix is not null)
        {
            return byPrefix;
        }

        string best = null;
        var bestDistance = int.MaxValue;

        foreach (var known in KnownNames)
        {
            var distance = EditDistance(name.ToLowerInvariant(), known);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = known;
            }
        }

        // Beyond a third of the name being wrong, a suggestion is more misleading than helpful.
        return bestDistance <= Math.Max(2, name.Length / 3) ? best : null;
    }

    private static int EditDistance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var j = 0; j <= right.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= right.Length; j++)
            {
                var substitution = previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    public string Verb(int position) => position < Verbs.Count ? Verbs[position] : string.Empty;

    public bool HasFlag(string name) => _flags.Contains(name);

    public string Option(string name, string fallback = null) =>
        _options.TryGetValue(name, out var value) ? value : fallback;

    public string RequiredOption(string name) =>
        _options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ToolException($"Missing required option --{name}.");

    public int OptionAsInt(string name, int fallback)
    {
        var raw = Option(name);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return int.TryParse(raw, out var value)
            ? value
            : throw new ToolException($"Option --{name} must be a whole number, got '{raw}'.");
    }
}
