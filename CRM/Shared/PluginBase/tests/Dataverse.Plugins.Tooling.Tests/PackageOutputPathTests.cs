using Dataverse.Plugins.Tooling.Configuration;
using Xunit;

namespace Dataverse.Plugins.Tooling.Tests;

/// <summary>
/// Where 'dv pack' writes the .zip. Four places can say, and they have to be tried in a fixed
/// order - a package written somewhere unexpected is one that quietly never reaches the person
/// waiting to import it.
/// </summary>
public class PackageOutputPathTests : IDisposable
{
    private const string ZipName = "SamplePlugins_1_0_0_1.zip";

    private readonly string _root;

    public PackageOutputPathTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dv-packagepath-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void TheDefaultIsOneFolderPerSolutionUnderCrmSolutions()
    {
        var resolved = Resolve(dvJson: """{ }""", config: Config());

        Assert.Equal(Path.Combine(_root, "CRM", "Solutions", "Sample", ZipName), resolved);
    }

    [Fact]
    public void DvJsonSubstitutesTheSolutionName()
    {
        var resolved = Resolve(
            dvJson: """{ "packageOutputDirectory": "dist/{solution}/packages" }""",
            config: Config());

        Assert.Equal(Path.Combine(_root, "dist", "Sample", "packages", ZipName), resolved);
    }

    [Fact]
    public void TheSolutionsOwnPackageOutputBeatsDvJson()
    {
        var resolved = Resolve(
            dvJson: """{ "packageOutputDirectory": "dist/{solution}" }""",
            config: Config(packageOutput: "handover"));

        Assert.Equal(Path.Combine(_root, "handover", ZipName), resolved);
    }

    [Fact]
    public void OutBeatsBoth()
    {
        var resolved = Resolve(
            dvJson: """{ "packageOutputDirectory": "dist/{solution}" }""",
            config: Config(packageOutput: "handover"),
            outOption: "somewhere/else");

        Assert.Equal(Path.Combine(_root, "somewhere", "else", ZipName), resolved);
    }

    [Fact]
    public void OutNamingAZipIsTakenAsTheFileNotTheFolder()
    {
        var resolved = Resolve(dvJson: """{ }""", config: Config(), outOption: "out/custom-name.zip");

        Assert.Equal(Path.Combine(_root, "out", "custom-name.zip"), resolved);
    }

    [Fact]
    public void OutNamingAFolderKeepsTheConventionalFileName()
    {
        var resolved = Resolve(dvJson: """{ }""", config: Config(), outOption: "out");

        Assert.Equal(Path.Combine(_root, "out", ZipName), resolved);
    }

    [Fact]
    public void AnAbsoluteOutIsUsedAsGiven()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "dv-absolute", "package.zip");

        Assert.Equal(absolute, Resolve(dvJson: """{ }""", config: Config(), outOption: absolute));
    }

    [Fact]
    public void ARelativePathIsRelativeToTheRepoRootNotTheWorkingDirectory()
    {
        // Otherwise the same configured value would mean different folders depending on where the
        // command was run from, which is the whole reason people lose a built package.
        var previous = Directory.GetCurrentDirectory();
        var elsewhere = Path.Combine(_root, "CRM", "Plugins", "Sample");
        Directory.CreateDirectory(elsewhere);

        try
        {
            Directory.SetCurrentDirectory(elsewhere);

            var resolved = Resolve(dvJson: """{ "packageOutputDirectory": "dist" }""", config: Config());

            Assert.Equal(Path.Combine(_root, "dist", ZipName), resolved);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }

    [Fact]
    public void ForwardSlashesInConfigWorkOnWindowsToo()
    {
        var resolved = Resolve(dvJson: """{ "packageOutputDirectory": "a/b/c" }""", config: Config());

        Assert.Equal(Path.Combine(_root, "a", "b", "c", ZipName), resolved);
    }

    private static SolutionConfig Config(string packageOutput = null) =>
        new()
        {
            Publisher = new SolutionConfig.PublisherSection { UniqueName = "sample", Prefix = "sample" },
            Solution = new SolutionConfig.SolutionSection
            {
                UniqueName = "SamplePlugins",
                Version = "1.0.0.0",
                PackageOutput = packageOutput,
            },
        };

    private string Resolve(string dvJson, SolutionConfig config, string outOption = null)
    {
        File.WriteAllText(Path.Combine(_root, RepoPaths.MarkerFileName), dvJson);

        var repo = RepoPaths.Discover(_root);
        var solution = new SolutionPaths(repo, "Sample", Path.Combine(repo.PluginsDirectory, "Sample"));

        return solution.ResolvePackagePath(config, outOption, ZipName);
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
