using Dataverse.Plugins.Tooling.Infrastructure;
using Xunit;

namespace Dataverse.Plugins.Tooling.Tests;

public class CommandLineTests
{
    [Fact]
    public void VerbsComeBeforeOptions()
    {
        var cli = CommandLine.Parse(["schema", "pull", "--env", "dev"]);

        Assert.Equal(["schema", "pull"], cli.Verbs);
        Assert.Equal("schema", cli.Verb(0));
        Assert.Equal("pull", cli.Verb(1));
        Assert.Equal(string.Empty, cli.Verb(2));
    }

    [Theory]
    [InlineData("-e", "env")]
    [InlineData("-t", "tables")]
    [InlineData("-a", "assembly")]
    [InlineData("-c", "configuration")]
    [InlineData("-o", "out")]
    [InlineData("-p", "package")]
    public void ShortOptionsResolveToTheirLongForm(string shortForm, string longName)
    {
        var cli = CommandLine.Parse(["sync", shortForm, "value"]);

        Assert.Equal("value", cli.Option(longName));
    }

    [Theory]
    [InlineData("-v", "verbose")]
    [InlineData("-m", "managed")]
    [InlineData("-h", "help")]
    public void ShortFlagsResolveToTheirLongForm(string shortForm, string longName)
    {
        var cli = CommandLine.Parse(["pack", shortForm]);

        Assert.True(cli.HasFlag(longName));
    }

    [Fact]
    public void ShortAndLongFormsAreInterchangeable()
    {
        var shortForm = CommandLine.Parse(["sync", "-e", "dev", "-a", "Contoso.Plugins"]);
        var longForm = CommandLine.Parse(["sync", "--env", "dev", "--assembly", "Contoso.Plugins"]);

        Assert.Equal(longForm.Option("env"), shortForm.Option("env"));
        Assert.Equal(longForm.Option("assembly"), shortForm.Option("assembly"));
    }

    [Fact]
    public void NameEqualsValueWorksForShortFormsToo()
    {
        var cli = CommandLine.Parse(["manifest", "-a=Sample.Plugins", "--env=dev"]);

        Assert.Equal("Sample.Plugins", cli.Option("assembly"));
        Assert.Equal("dev", cli.Option("env"));
    }

    /// <summary>
    /// --prune deletes registrations. A destructive flag must not sit one keystroke away from a
    /// harmless one, so it deliberately has no short form.
    /// </summary>
    [Fact]
    public void PruneHasNoShortForm()
    {
        // -p is package. The point is that no single letter reaches prune, so a slip cannot delete
        // registrations; only the spelled-out --prune does.
        var slip = CommandLine.Parse(["sync", "-p", "some.zip"]);

        Assert.False(slip.HasFlag("prune"));
        Assert.Equal("some.zip", slip.Option("package"));

        Assert.True(CommandLine.Parse(["sync", "--prune"]).HasFlag("prune"));
    }

    [Fact]
    public void NoSingleLetterAliasReachesADestructiveOption()
    {
        // Guards the table itself: adding "-p" => "prune" later would make this fail.
        foreach (var letter in "abcdefghijklmnopqrstuvwxyz")
        {
            CommandLine parsed;

            try
            {
                parsed = CommandLine.Parse(["sync", $"-{letter}"]);
            }
            catch (ToolException)
            {
                continue; // Not an alias at all, which is fine.
            }

            Assert.False(parsed.HasFlag("prune"), $"-{letter} must not resolve to --prune.");
        }
    }

    [Fact]
    public void UnknownOptionIsRejectedRatherThanIgnored()
    {
        // Silently ignoring it would surface later as "Missing required option --env", never
        // mentioning the actual mistake.
        var exception = Assert.Throws<ToolException>(() => CommandLine.Parse(["sync", "--nonsense", "x"]));

        Assert.Contains("--nonsense", exception.Message);
    }

    [Theory]
    [InlineData("--enviroment", "--env")]
    [InlineData("--conf", "--configuration")]
    [InlineData("--asembly", "--assembly")]
    [InlineData("--tabels", "--tables")]
    public void UnknownOptionSuggestsTheClosestName(string typo, string expected)
    {
        var exception = Assert.Throws<ToolException>(() => CommandLine.Parse(["sync", typo, "x"]));

        Assert.Contains(expected, exception.Message);
    }

    [Fact]
    public void OptionNamesAreCaseInsensitive()
    {
        var cli = CommandLine.Parse(["sync", "--ENV", "dev"]);

        Assert.Equal("dev", cli.Option("env"));
    }

    [Fact]
    public void MissingRequiredOptionSaysWhichOne()
    {
        var exception = Assert.Throws<ToolException>(() => CommandLine.Parse(["sync"]).RequiredOption("env"));

        Assert.Contains("--env", exception.Message);
    }

    [Fact]
    public void AFlagAtTheEndIsNotMistakenForAnOptionValue()
    {
        var cli = CommandLine.Parse(["pack", "--configuration", "Release", "--managed"]);

        Assert.Equal("Release", cli.Option("configuration"));
        Assert.True(cli.HasFlag("managed"));
    }

    [Fact]
    public void AnOptionFollowedByAnotherOptionIsTreatedAsAFlag()
    {
        var cli = CommandLine.Parse(["sync", "--prune", "--env", "dev"]);

        Assert.True(cli.HasFlag("prune"));
        Assert.Equal("dev", cli.Option("env"));
    }

    [Fact]
    public void PositionalArgumentAfterAnOptionIsRejected()
    {
        var exception = Assert.Throws<ToolException>(
            () => CommandLine.Parse(["sync", "--env", "dev", "stray"]));

        Assert.Contains("stray", exception.Message);
    }
}
