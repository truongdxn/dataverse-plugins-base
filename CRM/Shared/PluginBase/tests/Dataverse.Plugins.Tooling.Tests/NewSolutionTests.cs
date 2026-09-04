using Dataverse.Plugins.Tooling.Commands;
using Dataverse.Plugins.Tooling.Configuration;
using Dataverse.Plugins.Tooling.Infrastructure;
using Xunit;

namespace Dataverse.Plugins.Tooling.Tests;

/// <summary>
/// <c>dv new solution</c>. It is the only way a solution gets created now that Visual Studio
/// cannot, so it has to handle the folder already existing - and must never rewrite one that does.
/// </summary>
public class NewSolutionTests : IDisposable
{
    private readonly string _root;

    public NewSolutionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dv-newsolution-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, RepoPaths.MarkerFileName),
            """{ "pluginsDirectory": "CRM/Plugins" }""");
    }

    [Fact]
    public void CreatesAUsableSolutionFromNothing()
    {
        Run("new", "solution", "CRMCore", "--prefix", "contoso");

        var solution = Assert.Single(SolutionSet.Discover(Repo()).All);
        var config = SolutionConfig.Load(solution);

        Assert.Equal("CRMCore", solution.Name);
        Assert.Equal("CRMCore", config.Solution.UniqueName);
        Assert.Equal("contoso", config.Publisher.Prefix);
        Assert.Equal("1.0.0.0", config.Solution.Version);
    }

    [Fact]
    public void AdoptsAFolderThatAlreadyHoldsAProject()
    {
        // The case this command exists for: Visual Studio made the project, nothing made the
        // config, and 'dv solutions' showed nothing.
        GuiCreatedProject("CRMCore");

        var repo = Repo();
        Assert.Equal("CRMCore", Assert.Single(SolutionSet.Discover(repo).Candidates));

        Run("new", "solution", "CRMCore");

        var set = SolutionSet.Discover(repo);
        Assert.Equal("CRMCore", Assert.Single(set.All).Name);
        Assert.Empty(set.Candidates);
    }

    [Fact]
    public void InfersTheNameFromTheFolderItWasRunIn()
    {
        GuiCreatedProject("CRMCore");

        Run(["new", "solution"], workingDirectory: Path.Combine(Repo().PluginsDirectory, "CRMCore", "CRMCore.Plugins"));

        Assert.Equal("CRMCore", Assert.Single(SolutionSet.Discover(Repo()).All).Name);
    }

    [Fact]
    public void RefusesToOverwriteAnExistingSolution()
    {
        // solution.json holds uniqueName, which every component id derives from. Rewriting it
        // would silently re-identify everything, so the next sync would create duplicates instead
        // of updating what is deployed. Refusing is the entire safety story.
        Run("new", "solution", "CRMCore", "--prefix", "contoso");

        var configFile = Path.Combine(Repo().PluginsDirectory, "CRMCore", SolutionPaths.MarkerFileName);
        var before = File.ReadAllText(configFile);

        var exception = Assert.Throws<ToolException>(
            () => Run("new", "solution", "CRMCore", "--prefix", "somethingelse"));

        Assert.Contains("already a solution", exception.Message);
        Assert.Equal(before, File.ReadAllText(configFile));
    }

    [Fact]
    public void AnInvalidPrefixFailsBeforeAnythingIsWritten()
    {
        // Half a solution on disk is worse than none: the folder would then look configured.
        var exception = Assert.Throws<ToolException>(
            () => Run("new", "solution", "CRMCore", "--unique-name", "not valid!"));

        Assert.Contains("uniqueName", exception.Message);
        Assert.False(File.Exists(Path.Combine(Repo().PluginsDirectory, "CRMCore", SolutionPaths.MarkerFileName)));
    }

    [Fact]
    public void AFolderNameDataverseWouldRejectIsSanitisedIntoAValidUniqueName()
    {
        // Folder names allow spaces and dashes; Dataverse unique names do not.
        Run("new", "solution", "CRM-Core Extras");

        var config = SolutionConfig.Load(Assert.Single(SolutionSet.Discover(Repo()).All));

        Assert.Equal("CRMCoreExtras", config.Solution.UniqueName);
        Assert.Equal("crmcoreextras", config.Publisher.Prefix);
    }

    [Fact]
    public void ExplicitNamesWinOverTheDefaults()
    {
        Run("new", "solution", "CRMCore",
            "--unique-name", "ContosoCore",
            "--prefix", "contoso",
            "--version", "2.1.0.0",
            "--option-value-prefix", "42000");

        var config = SolutionConfig.Load(Assert.Single(SolutionSet.Discover(Repo()).All));

        Assert.Equal("ContosoCore", config.Solution.UniqueName);
        Assert.Equal("contoso", config.Publisher.Prefix);
        Assert.Equal("2.1.0.0", config.Solution.Version);
        Assert.Equal(42000, config.Publisher.OptionValuePrefix);
    }

    [Fact]
    public void WithNoNameAndNothingToInferFromItAsksRatherThanGuessing()
    {
        var exception = Assert.Throws<ToolException>(
            () => Run(["new", "solution"], workingDirectory: _root));

        Assert.Contains("Which solution?", exception.Message);
    }

    [Fact]
    public void TheWrittenFileKeepsTheCommentsExplainingWhatMustNotChange()
    {
        // Serialising the config would produce the same values with none of the reasons, and
        // uniqueName is precisely the field somebody needs warning about before editing it.
        Run("new", "solution", "CRMCore");

        var text = File.ReadAllText(
            Path.Combine(Repo().PluginsDirectory, "CRMCore", SolutionPaths.MarkerFileName));

        Assert.Contains("$comment", text);
        Assert.Contains("uniqueName", text);
    }

    private RepoPaths Repo() => RepoPaths.Discover(_root);

    private int Run(params string[] args) => Run(args, workingDirectory: _root);

    private int Run(string[] args, string workingDirectory) =>
        ScaffoldCommands.NewSolution(CommandLine.Parse(args), Repo(), workingDirectory);

    /// <summary>A project folder with no solution.json - what the New Project dialog leaves behind.</summary>
    private void GuiCreatedProject(string solutionName)
    {
        var project = Path.Combine(_root, "CRM", "Plugins", solutionName, solutionName + ".Plugins");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, solutionName + ".Plugins.csproj"), "<Project />");
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
