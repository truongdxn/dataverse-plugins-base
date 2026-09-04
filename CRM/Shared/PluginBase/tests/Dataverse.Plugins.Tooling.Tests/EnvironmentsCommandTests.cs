using Dataverse.Plugins.Tooling.Commands;
using Dataverse.Plugins.Tooling.Configuration;
using Dataverse.Plugins.Tooling.Infrastructure;
using Xunit;

namespace Dataverse.Plugins.Tooling.Tests;

/// <summary>
/// <c>dv environments</c>.
/// <para>
/// Its output is a contract, not just a convenience: the Visual Studio extension runs this command
/// and parses the result to fill its environment picker, rather than reading environments.json
/// itself. Doing it that way keeps one definition of how the git-ignored environments.local.json
/// overlay is merged - but only as long as the printed shape holds, which is what these tests are
/// for.
/// </para>
/// </summary>
public class EnvironmentsCommandTests : IDisposable
{
    private readonly string _root;
    private readonly TextWriter _console;
    private readonly StringWriter _captured = new();

    public EnvironmentsCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dv-environments-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "config"));
        File.WriteAllText(Path.Combine(_root, RepoPaths.MarkerFileName), "{}");

        _console = Console.Out;
        Console.SetOut(_captured);
    }

    [Fact]
    public void ListsEachEnvironmentAsAnItemLine()
    {
        WriteEnvironments("""
            { "environments": { "dev": { "url": "https://contoso.crm5.dynamics.com/", "description": "Shared sandbox." } } }
            """);

        Assert.Equal(0, Run());

        var line = Assert.Single(ItemLines());
        Assert.StartsWith("  dev  ->  https://contoso.crm5.dynamics.com/", line);
        Assert.EndsWith("Shared sandbox.", line);
    }

    [Fact]
    public void EveryItemLineIsIndentedAndCarriesTheArrow()
    {
        // The two rules the extension's parser depends on. Anything printed at column zero - the
        // heading, warnings - it ignores.
        WriteEnvironments("""
            { "environments": { "dev": { "url": "https://a.crm5.dynamics.com/" }, "scratch": { "url": "https://b.crm5.dynamics.com/" } } }
            """);

        Run();

        var lines = ItemLines();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, line => Assert.Contains("  ->  ", line));
    }

    [Fact]
    public void SortsByNameSoThePickerDoesNotReorderItself()
    {
        WriteEnvironments("""
            { "environments": { "zulu": { "url": "https://z.crm5.dynamics.com/" }, "alpha": { "url": "https://a.crm5.dynamics.com/" } } }
            """);

        Run();

        var names = ItemLines().Select(line => line.Trim().Split(' ')[0]).ToList();
        Assert.Equal(new[] { "alpha", "zulu" }, names);
    }

    [Fact]
    public void LocalOverridesWin()
    {
        // The reason the extension asks dv instead of reading the shared file: a developer who has
        // pointed 'dev' at their own sandbox must see their own URL in Visual Studio too.
        WriteEnvironments("""
            { "environments": { "dev": { "url": "https://team.crm5.dynamics.com/" } } }
            """);
        File.WriteAllText(
            Path.Combine(_root, "config", "environments.local.json"),
            """
            { "environments": { "dev": { "url": "https://mine.crm5.dynamics.com/" } } }
            """);

        Run();

        Assert.Contains("https://mine.crm5.dynamics.com/", Assert.Single(ItemLines()));
    }

    [Fact]
    public void AMissingFileIsAWarningAndNotAFailure()
    {
        // Listing is not the command to fail over a file that was never created. A consumer repo
        // may have no environments yet, and being told where to put them is the useful answer.
        Assert.Equal(0, Run());

        Assert.Empty(ItemLines());
        Assert.Contains("environments.json", _captured.ToString());
    }

    [Fact]
    public void AnEmptyEnvironmentsBlockIsAlsoReported()
    {
        WriteEnvironments("""{ "environments": { } }""");

        Assert.Equal(0, Run());
        Assert.Empty(ItemLines());
        Assert.Contains("No environments configured", _captured.ToString());
    }

    private int Run() => CommandHandlers.Environments(CommandLine.Parse([]), RepoPaths.Discover(_root));

    private void WriteEnvironments(string json) =>
        File.WriteAllText(Path.Combine(_root, "config", "environments.json"), json);

    /// <summary>
    /// The lines the extension keeps: indented, and carrying the arrow. Mirrors
    /// <c>EnvironmentList.Parse</c> in the extension's core, so the two cannot drift apart without
    /// one of them failing.
    /// </summary>
    private List<string> ItemLines() =>
        _captured.ToString()
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("  ", StringComparison.Ordinal) && line.Contains("->"))
            .ToList();

    public void Dispose()
    {
        Console.SetOut(_console);
        _captured.Dispose();

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
