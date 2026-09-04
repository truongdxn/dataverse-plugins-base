using Dataverse.Plugins.Tooling.Configuration;
using Dataverse.Plugins.Tooling.Infrastructure;
using Xunit;

namespace Dataverse.Plugins.Tooling.Tests;

/// <summary>
/// A folder holding plugin projects but no solution.json.
/// <para>
/// This is what Visual Studio's New Project dialog produces on its own: it can create the projects
/// but not the config file, because a solution folder is config rather than a project. Before this
/// was tracked, such a folder was not reported as broken - it was invisible. 'dv solutions' listed
/// nothing and said nothing, and the only symptom was that a solution somebody had just created
/// did not exist. Every test here guards against returning to that silence.
/// </para>
/// </summary>
public class UnconfiguredSolutionTests : IDisposable
{
    private readonly string _root;

    public UnconfiguredSolutionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dv-unconfigured-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void AFolderWithAProjectAndNoSolutionJsonIsReportedAsACandidate()
    {
        var repo = Repo(solutions: [], unconfigured: ["CRMCore"]);
        var set = SolutionSet.Discover(repo);

        Assert.Empty(set.All);
        Assert.Equal("CRMCore", Assert.Single(set.Candidates));
    }

    [Fact]
    public void AnEmptyFolderIsNotACandidate()
    {
        // Nobody's mistake. Only a folder someone has started putting projects in is worth naming.
        var repo = Repo(solutions: [], unconfigured: []);
        Directory.CreateDirectory(Path.Combine(repo.PluginsDirectory, "Empty"));

        Assert.Empty(SolutionSet.Discover(repo).Candidates);
    }

    [Fact]
    public void AProjectNestedDeepInTheFolderStillCounts()
    {
        // The realistic shape: CRM/Plugins/CRMCore/CRMCore.Plugins/CRMCore.Plugins.csproj.
        var repo = Repo(solutions: [], unconfigured: ["CRMCore"]);

        Assert.Equal("CRMCore", Assert.Single(SolutionSet.Discover(repo).Candidates));
    }

    [Fact]
    public void AConfiguredFolderIsASolutionNotACandidate()
    {
        var repo = Repo(solutions: ["CRMCore"], unconfigured: []);
        var set = SolutionSet.Discover(repo);

        Assert.Equal("CRMCore", Assert.Single(set.All).Name);
        Assert.Empty(set.Candidates);
    }

    [Fact]
    public void NamingAnUnconfiguredFolderExplainsItRatherThanCallingItUnknown()
    {
        // 'Unknown solution CRMCore' would be a lie - the folder is right there.
        var repo = Repo(solutions: ["Other"], unconfigured: ["CRMCore"]);

        var exception = Assert.Throws<ToolException>(
            () => SolutionSet.Discover(repo).Resolve("CRMCore", repo.Root));

        Assert.Contains("dv new solution CRMCore", exception.Message);
        Assert.DoesNotContain("Unknown solution", exception.Message);
    }

    [Fact]
    public void RunningInsideAnUnconfiguredFolderExplainsItRatherThanFallingBack()
    {
        // defaultSolution would otherwise quietly resolve to Other, and the command would act on
        // the wrong solution while appearing to work.
        var repo = Repo(solutions: ["Other"], unconfigured: ["CRMCore"], defaultSolution: "Other");
        var inside = Path.Combine(repo.PluginsDirectory, "CRMCore", "CRMCore.Plugins");

        var exception = Assert.Throws<ToolException>(
            () => SolutionSet.Discover(repo).Resolve(null, inside));

        Assert.Contains("dv new solution CRMCore", exception.Message);
    }

    [Fact]
    public void AnExplicitSolutionStillWinsFromInsideAnUnconfiguredFolder()
    {
        var repo = Repo(solutions: ["Other"], unconfigured: ["CRMCore"]);
        var inside = Path.Combine(repo.PluginsDirectory, "CRMCore");

        Assert.Equal("Other", SolutionSet.Discover(repo).Resolve("Other", inside).Name);
    }

    [Fact]
    public void WithNoSolutionsAtAllTheCandidatesAreNamedInTheError()
    {
        var repo = Repo(solutions: [], unconfigured: ["CRMCore"]);

        var exception = Assert.Throws<ToolException>(
            () => SolutionSet.Discover(repo).Resolve(null, repo.Root));

        Assert.Contains("CRMCore", exception.Message);
        Assert.Contains("dv new solution", exception.Message);
    }

    [Fact]
    public void CandidateFromDirectoryFindsTheFolderFromAnyDepthInside()
    {
        var repo = Repo(solutions: [], unconfigured: ["CRMCore"]);
        var deep = Path.Combine(repo.PluginsDirectory, "CRMCore", "CRMCore.Plugins", "Handlers");
        Directory.CreateDirectory(deep);

        var set = SolutionSet.Discover(repo);

        Assert.Equal("CRMCore", set.CandidateFromDirectory(deep));
        Assert.Null(set.CandidateFromDirectory(repo.Root));
    }

    private RepoPaths Repo(string[] solutions, string[] unconfigured, string defaultSolution = null)
    {
        var settings = defaultSolution is null
            ? """{ "pluginsDirectory": "CRM/Plugins" }"""
            : $$"""{ "pluginsDirectory": "CRM/Plugins", "defaultSolution": "{{defaultSolution}}" }""";

        File.WriteAllText(Path.Combine(_root, RepoPaths.MarkerFileName), settings);

        foreach (var name in solutions.Concat(unconfigured))
        {
            // Every folder gets a project; only the configured ones get a solution.json. That is
            // the single difference these tests turn on.
            var project = Path.Combine(_root, "CRM", "Plugins", name, name + ".Plugins");
            Directory.CreateDirectory(project);
            File.WriteAllText(Path.Combine(project, name + ".Plugins.csproj"), "<Project />");
        }

        foreach (var name in solutions)
        {
            File.WriteAllText(
                Path.Combine(_root, "CRM", "Plugins", name, SolutionPaths.MarkerFileName),
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
