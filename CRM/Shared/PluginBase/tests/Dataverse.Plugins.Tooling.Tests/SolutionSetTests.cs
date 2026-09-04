using Dataverse.Plugins.Tooling.Configuration;
using Dataverse.Plugins.Tooling.Infrastructure;
using Xunit;

namespace Dataverse.Plugins.Tooling.Tests;

/// <summary>
/// Which solution a command acts on. Getting this wrong is worse than it sounds: several people
/// work in one repo, and a command that silently picks the wrong solution packs the wrong .zip or
/// registers steps somewhere nobody asked for.
/// </summary>
public class SolutionSetTests : IDisposable
{
    private readonly string _root;

    public SolutionSetTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dv-solutionset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void ExplicitNameWinsOverEverythingElse()
    {
        var repo = Repo(defaultSolution: "Alpha", solutions: ["Alpha", "Beta"]);
        var set = SolutionSet.Discover(repo);

        var resolved = set.Resolve("Beta", workingDirectory: Path.Combine(repo.PluginsDirectory, "Alpha"));

        Assert.Equal("Beta", resolved.Name);
    }

    [Fact]
    public void TheWorkingDirectoryDecidesWhenNoNameIsGiven()
    {
        var repo = Repo(defaultSolution: "Alpha", solutions: ["Alpha", "Beta"]);
        var set = SolutionSet.Discover(repo);

        var resolved = set.Resolve(null, workingDirectory: Path.Combine(repo.PluginsDirectory, "Beta"));

        Assert.Equal("Beta", resolved.Name);
    }

    [Fact]
    public void ADeeplyNestedWorkingDirectoryStillResolves()
    {
        // The realistic case: the developer is in the project folder, not the solution folder.
        var repo = Repo(defaultSolution: "Alpha", solutions: ["Alpha", "Beta"]);
        var nested = Path.Combine(repo.PluginsDirectory, "Beta", "Beta.Plugins", "Handlers");
        Directory.CreateDirectory(nested);

        Assert.Equal("Beta", SolutionSet.Discover(repo).Resolve(null, nested).Name);
    }

    [Fact]
    public void DefaultSolutionIsUsedFromOutsideAnySolutionFolder()
    {
        var repo = Repo(defaultSolution: "Beta", solutions: ["Alpha", "Beta"]);

        Assert.Equal("Beta", SolutionSet.Discover(repo).Resolve(null, repo.Root).Name);
    }

    [Fact]
    public void TheOnlySolutionIsUsedWithoutBeingNamed()
    {
        var repo = Repo(defaultSolution: null, solutions: ["Alpha"]);

        Assert.Equal("Alpha", SolutionSet.Discover(repo).Resolve(null, repo.Root).Name);
    }

    [Fact]
    public void AmbiguityFailsAndListsTheChoices()
    {
        // Picking one and being wrong is far worse than refusing to pick.
        var repo = Repo(defaultSolution: null, solutions: ["Alpha", "Beta"]);

        var exception = Assert.Throws<ToolException>(
            () => SolutionSet.Discover(repo).Resolve(null, repo.Root));

        Assert.Contains("Alpha", exception.Message);
        Assert.Contains("Beta", exception.Message);
        Assert.Contains("-s", exception.Message);
    }

    [Fact]
    public void AnUnknownNameFailsAndListsWhatExists()
    {
        var repo = Repo(defaultSolution: null, solutions: ["Alpha"]);

        var exception = Assert.Throws<ToolException>(
            () => SolutionSet.Discover(repo).Resolve("Gamma", repo.Root));

        Assert.Contains("Gamma", exception.Message);
        Assert.Contains("Alpha", exception.Message);
    }

    [Fact]
    public void ADefaultSolutionThatDoesNotExistSaysWhereItCameFrom()
    {
        // Otherwise the message blames the command line for a mistake in dv.json.
        var repo = Repo(defaultSolution: "Missing", solutions: ["Alpha", "Beta"]);

        var exception = Assert.Throws<ToolException>(
            () => SolutionSet.Discover(repo).Resolve(null, repo.Root));

        Assert.Contains("dv.json", exception.Message);
        Assert.Contains("Missing", exception.Message);
    }

    [Fact]
    public void AFolderWithoutSolutionJsonIsNotASolution()
    {
        var repo = Repo(defaultSolution: null, solutions: ["Alpha"]);
        Directory.CreateDirectory(Path.Combine(repo.PluginsDirectory, "NotASolution"));

        Assert.Equal("Alpha", Assert.Single(SolutionSet.Discover(repo).All).Name);
    }

    [Fact]
    public void NoSolutionsAtAllExplainsHowToMakeOne()
    {
        var repo = Repo(defaultSolution: null, solutions: []);

        var exception = Assert.Throws<ToolException>(
            () => SolutionSet.Discover(repo).Resolve(null, repo.Root));

        Assert.Contains("dv new solution", exception.Message);
    }

    [Fact]
    public void DiscoveryFindsTheRootFromASubfolder()
    {
        var repo = Repo(defaultSolution: "Alpha", solutions: ["Alpha"]);
        var nested = Path.Combine(repo.PluginsDirectory, "Alpha", "Alpha.Plugins");
        Directory.CreateDirectory(nested);

        Assert.Equal(repo.Root, RepoPaths.Discover(nested).Root);
    }

    private RepoPaths Repo(string defaultSolution, string[] solutions)
    {
        var settings = defaultSolution is null
            ? """{ "pluginsDirectory": "CRM/Plugins" }"""
            : $$"""{ "pluginsDirectory": "CRM/Plugins", "defaultSolution": "{{defaultSolution}}" }""";

        File.WriteAllText(Path.Combine(_root, RepoPaths.MarkerFileName), settings);

        foreach (var name in solutions)
        {
            var directory = Path.Combine(_root, "CRM", "Plugins", name);
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, SolutionPaths.MarkerFileName),
                $$"""{ "publisher": { "uniqueName": "p", "prefix": "p" }, "solution": { "uniqueName": "{{name}}" } }""");
        }

        return RepoPaths.Discover(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A locked temp folder is not worth failing a passing test over.
        }
    }
}
