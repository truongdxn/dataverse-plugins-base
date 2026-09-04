using Dataverse.Plugins.VsCommands.Core;
using Xunit;

namespace Dataverse.Plugins.VsCommands.Tests;

/// <summary>
/// What each menu item actually runs.
/// <para>
/// Written as the command line a developer would type, because that is the promise the extension
/// makes: the menu is a shortcut for the CLI, not a second way of doing things.
/// </para>
/// </summary>
public class DvArgumentsTests
{
    [Fact]
    public void BuildingOneAssemblyNarrowsWithMinusA()
    {
        Assert.Equal("build -a Sample.Plugins -c Debug", Line(DvArguments.Build("Sample.Plugins", "Debug")));
    }

    [Fact]
    public void BuildingTheSolutionOmitsTheAssembly()
    {
        // No -a is how dv means "all of them", so the solution-node menu passes nothing at all.
        Assert.Equal("build -c Release", Line(DvArguments.Build(null, "Release")));
    }

    [Fact]
    public void NoSolutionIsEverPassed()
    {
        // -s exists, but the extension runs in the clicked project's folder and lets dv infer the
        // solution from it. Passing both would let a stale selection contradict the folder.
        Assert.DoesNotContain("-s", Line(DvArguments.Test("Sample.Plugins", "Debug")));
        Assert.DoesNotContain("--solution", Line(DvArguments.Pack("1.0.0.7", "Release")));
    }

    [Fact]
    public void SyncNeedsAnEnvironmentAndSaysSoBeforeAnythingRuns()
    {
        var thrown = Assert.Throws<ArgumentException>(() => DvArguments.Sync("Sample.Plugins", "", false, "Debug"));

        Assert.Contains("environment is required", thrown.Message);
    }

    [Fact]
    public void SyncPassesTheEnvironmentAndOmitsPruneUnlessAsked()
    {
        Assert.Equal("sync -a Sample.Plugins -c Debug -e dev",
            Line(DvArguments.Sync("Sample.Plugins", "dev", prune: false, configuration: "Debug")));
    }

    [Fact]
    public void PruneIsSpeltOutInFull()
    {
        // It deletes registrations that are not declared in source. The CLI gives it no short form
        // for that reason, and neither does this.
        Assert.Equal("sync -e dev --prune", Line(DvArguments.Sync(null, "dev", prune: true, configuration: null)));
    }

    [Fact]
    public void PackTakesTheSolutionVersion()
    {
        Assert.Equal("pack --version 1.0.0.42 -c Release", Line(DvArguments.Pack("1.0.0.42", "Release")));
    }

    [Fact]
    public void PackWithoutAVersionLetsDvChooseTheDefault()
    {
        Assert.Equal("pack -c Release", Line(DvArguments.Pack(null, "Release")));
    }

    [Fact]
    public void SchemaAndMessageCommandsCarryTheirSubVerb()
    {
        Assert.Equal("schema pull -e dev -t account,contact", Line(DvArguments.SchemaPull("dev", "account,contact")));
        Assert.Equal("schema pull -e dev", Line(DvArguments.SchemaPull("dev", null)));
        Assert.Equal("schema codegen", Line(DvArguments.SchemaCodegen()));
        Assert.Equal("messages pull -e dev", Line(DvArguments.MessagesPull("dev")));
    }

    [Fact]
    public void ListingCommandsTakeNothing()
    {
        Assert.Equal("solutions", Line(DvArguments.Solutions()));
        Assert.Equal("environments", Line(DvArguments.Environments()));
    }

    [Fact]
    public void ScaffoldingPassesOnlyWhatWasFilledIn()
    {
        Assert.Equal("new solution CRMCore --prefix contoso", Line(DvArguments.NewSolution("CRMCore", "contoso", null)));
        Assert.Equal("new solution CRMCore", Line(DvArguments.NewSolution("CRMCore", "", "   ")));
        Assert.Equal("new assembly CRMCore.Plugins", Line(DvArguments.NewAssembly("CRMCore.Plugins")));
        Assert.Equal("new tests CRMCore.Plugins.Tests --for CRMCore.Plugins",
            Line(DvArguments.NewTests("CRMCore.Plugins.Tests", "CRMCore.Plugins")));
        Assert.Equal("new plugin SetName --entity account --message Update --stage PostOperation",
            Line(DvArguments.NewPlugin("SetName", "account", "Update", "PostOperation")));
    }

    [Fact]
    public void ANameIsRequiredForEveryScaffold()
    {
        Assert.Throws<ArgumentException>(() => DvArguments.NewSolution(null, null, null));
        Assert.Throws<ArgumentException>(() => DvArguments.NewAssembly(" "));
        Assert.Throws<ArgumentException>(() => DvArguments.NewTests("", "Sample.Plugins"));
        Assert.Throws<ArgumentException>(() => DvArguments.NewPlugin(null, "account", "Update", "PostOperation"));
    }

    [Fact]
    public void ValuesAreTrimmedBecauseTheyComeFromTextBoxes()
    {
        Assert.Equal("new solution CRMCore --prefix contoso", Line(DvArguments.NewSolution("  CRMCore  ", " contoso ", null)));
    }

    private static string Line(IReadOnlyList<string> arguments) => DvInvocation.Join(arguments);
}
