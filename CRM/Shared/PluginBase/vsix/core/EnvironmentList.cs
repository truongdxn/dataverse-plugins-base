using System;
using System.Collections.Generic;

namespace Dataverse.Plugins.VsCommands.Core;

/// <summary>One configured developer sandbox, as reported by 'dv environments'.</summary>
public sealed class DvEnvironment
{
    public DvEnvironment(string name, string url, string description)
    {
        Name = name;
        Url = url;
        Description = description;
    }

    public string Name { get; }

    public string Url { get; }

    public string Description { get; }

    public override string ToString() =>
        string.IsNullOrWhiteSpace(Url) ? Name : Name + "  (" + Url + ")";
}

/// <summary>
/// Reads the output of 'dv environments' into a list the picker can show.
/// <para>
/// The extension asks dv rather than reading config/environments.json itself, and that is on
/// purpose: the shared file is merged with the git-ignored environments.local.json, so a developer
/// pointing 'dev' at their own sandbox would otherwise see the team's URL in Visual Studio and
/// their own from the terminal. One place owns that merge, and it is the CLI.
/// </para>
/// </summary>
public static class EnvironmentList
{
    private const string Separator = "->";

    /// <summary>
    /// Parses the listing. Anything that is not an item line - the heading, warnings, the verbose
    /// footnote - is ignored rather than guessed at, so an empty result means "none configured"
    /// and never "the output changed shape".
    /// </summary>
    public static IReadOnlyList<DvEnvironment> Parse(string output)
    {
        var environments = new List<DvEnvironment>();

        if (string.IsNullOrEmpty(output))
        {
            return environments;
        }

        foreach (var raw in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            // Item lines are indented by Log.Item; headings and warnings start at column zero.
            if (!raw.StartsWith("  ", StringComparison.Ordinal))
            {
                continue;
            }

            var index = raw.IndexOf(Separator, StringComparison.Ordinal);

            if (index < 0)
            {
                continue;
            }

            var name = raw.Substring(0, index).Trim();

            if (name.Length == 0)
            {
                continue;
            }

            var rest = raw.Substring(index + Separator.Length).Trim();
            var url = rest;
            var description = string.Empty;

            // The URL cannot contain a space, so the first run of whitespace after it starts the
            // description.
            var space = rest.IndexOf(' ');

            if (space > 0)
            {
                url = rest.Substring(0, space);
                description = rest.Substring(space).Trim();
            }

            environments.Add(new DvEnvironment(name, url, description));
        }

        return environments;
    }

    /// <summary>
    /// The one to offer first: 'dev' when it exists, otherwise whatever came back first. A repo
    /// with a single sandbox should never make anybody choose.
    /// </summary>
    public static DvEnvironment Preferred(IReadOnlyList<DvEnvironment> environments)
    {
        if (environments == null || environments.Count == 0)
        {
            return null;
        }

        foreach (var environment in environments)
        {
            if (string.Equals(environment.Name, "dev", StringComparison.OrdinalIgnoreCase))
            {
                return environment;
            }
        }

        return environments[0];
    }
}
