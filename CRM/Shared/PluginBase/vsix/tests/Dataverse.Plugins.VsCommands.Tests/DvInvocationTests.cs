using Dataverse.Plugins.VsCommands.Core;
using Xunit;

namespace Dataverse.Plugins.VsCommands.Tests;

/// <summary>Deciding what to launch, and quoting what is handed to it.</summary>
public class DvInvocationTests : IDisposable
{
    private readonly string _root;

    public DvInvocationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dv-vsinvoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, DvRepo.MarkerFileName), "{}");
    }

    [Fact]
    public void PrefersTheShim()
    {
        // Not because PowerShell is nicer, but because dv.ps1 already knows how to find a dotnet
        // with an SDK, whether to build the tool from source, and when it is stale. Bypassing it
        // would mean deciding all of that twice.
        WriteShim();
        var invocation = DvInvocation.For(Repo(), new[] { "build" });

        Assert.Equal("powershell.exe", invocation.FileName);
        Assert.Contains("-NoProfile", invocation.Arguments);
        Assert.Contains("-ExecutionPolicy Bypass", invocation.Arguments);
        Assert.Contains(DvRepo.ShimFileName, invocation.Arguments);
        Assert.EndsWith("build", invocation.Arguments);
    }

    [Fact]
    public void FallsBackToThePinnedToolInAConsumerRepo()
    {
        // A repo that consumes the packages and never copied the shims.
        WriteToolManifest();
        var invocation = DvInvocation.For(Repo(), new[] { "solutions" });

        Assert.Equal("dotnet", invocation.FileName);
        Assert.Equal("tool run dv solutions", invocation.Arguments);
    }

    [Fact]
    public void SaysHowToFixARepoThatCanRunDvNeitherWay()
    {
        var thrown = Assert.Throws<DvUnavailableException>(() => DvInvocation.For(Repo(), new[] { "build" }));

        Assert.Contains("dotnet tool install", thrown.Message);
        Assert.Contains(_root, thrown.Message);
    }

    [Fact]
    public void RunsInTheClickedProjectsFolderSoDvInfersTheSolution()
    {
        WriteShim();
        var project = Directory.CreateDirectory(Path.Combine(_root, "CRM", "Plugins", "Sample")).FullName;

        Assert.Equal(project, DvInvocation.For(Repo(), new[] { "build" }, project).WorkingDirectory);
        Assert.Equal(_root, DvInvocation.For(Repo(), new[] { "build" }).WorkingDirectory);
    }

    [Fact]
    public void QuotesArgumentsThatContainSpaces()
    {
        // "C:\My Projects\..." is the case this exists for: unquoted, dv sees two arguments and
        // fails with a message that mentions neither the space nor the path.
        var joined = DvInvocation.Join(new[] { "new", "solution", "Contoso CRM", "--prefix", "con" });

        Assert.Equal("new solution \"Contoso CRM\" --prefix con", joined);
    }

    [Fact]
    public void LeavesOrdinaryArgumentsAlone()
    {
        Assert.Equal("build -a Sample.Plugins -c Release",
            DvInvocation.Join(new[] { "build", "-a", "Sample.Plugins", "-c", "Release" }));
    }

    [Fact]
    public void QuotesTheShimPathToo()
    {
        // The repo path is not ours to choose; OneDrive puts one in "OneDrive - Contoso".
        var spaced = Path.Combine(_root, "a folder");
        Directory.CreateDirectory(spaced);
        File.WriteAllText(Path.Combine(spaced, DvRepo.MarkerFileName), "{}");
        File.WriteAllText(Path.Combine(spaced, DvRepo.ShimFileName), "# shim");

        var invocation = DvInvocation.For(DvRepo.Find(spaced), new[] { "build" });

        Assert.Contains("-File \"" + Path.Combine(spaced, DvRepo.ShimFileName) + "\"", invocation.Arguments);
    }

    [Fact]
    public void DisplayIsWhatYouWouldTypeInATerminal()
    {
        // Echoed into the Output pane, so a run can be repeated by hand - which is the first thing
        // anybody does when a command behaves differently inside the IDE.
        WriteShim();

        Assert.Equal("dv build -a Sample.Plugins",
            DvInvocation.For(Repo(), new[] { "build", "-a", "Sample.Plugins" }).Display);
    }

    [Fact]
    public void HandlesACommandWithNoArgumentsAtAll()
    {
        WriteShim();
        var invocation = DvInvocation.For(Repo(), Array.Empty<string>());

        Assert.EndsWith("\"", invocation.Arguments.TrimEnd());
        Assert.Equal("dv ", invocation.Display);
    }

    private DvRepo Repo() => DvRepo.Find(_root);

    private void WriteShim() => File.WriteAllText(Path.Combine(_root, DvRepo.ShimFileName), "# shim");

    private void WriteToolManifest()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".config"));
        File.WriteAllText(Path.Combine(_root, ".config", "dotnet-tools.json"), "{}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
