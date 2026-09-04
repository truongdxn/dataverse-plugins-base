using Dataverse.Plugins.VsCommands.Core;
using Xunit;

namespace Dataverse.Plugins.VsCommands.Tests;

/// <summary>
/// Finding the repo a click belongs to.
/// <para>
/// Getting this wrong is worse than failing: a command that resolves the wrong root would build,
/// or worse sync, a different solution than the one whose project was clicked.
/// </para>
/// </summary>
public class DvRepoTests : IDisposable
{
    private readonly string _root;

    public DvRepoTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dv-vsrepo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void FindsTheRootFromDeepInsideIt()
    {
        MarkRepo();
        var project = Directory.CreateDirectory(Path.Combine(_root, "CRM", "Plugins", "Sample", "Sample.Plugins"));

        var repo = DvRepo.Find(project.FullName);

        Assert.NotNull(repo);
        Assert.Equal(_root, repo.Root);
    }

    [Fact]
    public void ReturnsNullWhenNothingAboveIsADvRepo()
    {
        // Most solutions opened in Visual Studio have nothing to do with Dataverse. Not finding a
        // repo is the normal answer for them, and the menus stay hidden rather than erroring.
        var elsewhere = Directory.CreateDirectory(Path.Combine(_root, "unrelated"));

        Assert.Null(DvRepo.Find(elsewhere.FullName));
    }

    [Fact]
    public void FindsTheRootFromAProjectFilePath()
    {
        MarkRepo();
        var directory = Directory.CreateDirectory(Path.Combine(_root, "CRM", "Plugins", "Sample"));
        var csproj = Path.Combine(directory.FullName, "Sample.Plugins.csproj");
        File.WriteAllText(csproj, "<Project />");

        // Solution Explorer hands over a file, not a folder.
        Assert.Equal(_root, DvRepo.FindFromFile(csproj)?.Root);
    }

    [Fact]
    public void TheNearestMarkerWins()
    {
        // A repo checked out inside another one. The inner dv.json is the one that owns the click.
        MarkRepo();
        var inner = Directory.CreateDirectory(Path.Combine(_root, "vendor", "other-repo"));
        File.WriteAllText(Path.Combine(inner.FullName, DvRepo.MarkerFileName), "{}");

        var project = Directory.CreateDirectory(Path.Combine(inner.FullName, "CRM", "Plugins", "X"));

        Assert.Equal(inner.FullName, DvRepo.Find(project.FullName)?.Root);
    }

    [Fact]
    public void ReportsWhetherTheShimAndTheToolManifestAreThere()
    {
        MarkRepo();
        var repo = DvRepo.Find(_root);

        Assert.False(repo.HasShim);
        Assert.False(repo.HasToolManifest);

        File.WriteAllText(Path.Combine(_root, DvRepo.ShimFileName), "# shim");
        Directory.CreateDirectory(Path.Combine(_root, ".config"));
        File.WriteAllText(Path.Combine(_root, ".config", "dotnet-tools.json"), "{}");

        Assert.True(repo.HasShim);
        Assert.True(repo.HasToolManifest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyStartingPointIsNotAnError(string startingDirectory)
    {
        Assert.Null(DvRepo.Find(startingDirectory));
        Assert.Null(DvRepo.FindFromFile(startingDirectory));
    }

    private void MarkRepo() => File.WriteAllText(Path.Combine(_root, DvRepo.MarkerFileName), "{}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
