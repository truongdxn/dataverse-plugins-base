using Dataverse.Plugins.VsCommands.Core;
using Xunit;

namespace Dataverse.Plugins.VsCommands.Tests;

/// <summary>
/// Reading 'dv environments' back.
/// <para>
/// The extension asks the CLI instead of reading config/environments.json, so that the git-ignored
/// environments.local.json overlay applies in Visual Studio exactly as it does in a terminal. That
/// only holds if this parser matches what the command prints - which
/// <c>EnvironmentsCommandTests</c> in the tooling suite pins from the other side.
/// </para>
/// </summary>
public class EnvironmentListTests
{
    private const string Listing =
        "\r\n" +
        "Environments in C:\\repo\r\n" +
        "  dev  ->  https://contoso.crm5.dynamics.com/  Shared developer sandbox.\r\n" +
        "  scratch  ->  https://scratch.crm5.dynamics.com/\r\n";

    [Fact]
    public void ReadsNameUrlAndDescription()
    {
        var environments = EnvironmentList.Parse(Listing);

        Assert.Equal(2, environments.Count);
        Assert.Equal("dev", environments[0].Name);
        Assert.Equal("https://contoso.crm5.dynamics.com/", environments[0].Url);
        Assert.Equal("Shared developer sandbox.", environments[0].Description);
    }

    [Fact]
    public void ADescriptionIsOptional()
    {
        var scratch = EnvironmentList.Parse(Listing)[1];

        Assert.Equal("scratch", scratch.Name);
        Assert.Equal("https://scratch.crm5.dynamics.com/", scratch.Url);
        Assert.Equal(string.Empty, scratch.Description);
    }

    [Fact]
    public void IgnoresTheHeadingAndAnythingElseUnindented()
    {
        Assert.DoesNotContain(EnvironmentList.Parse(Listing), e => e.Name.StartsWith("Environments"));
    }

    [Fact]
    public void AWarningIsNotAnEnvironment()
    {
        // What the command prints when there is no environments.json yet. An empty list has to
        // mean "none configured" and never "I could not read the output".
        var environments = EnvironmentList.Parse(
            "warning: No environments file at C:\\repo\\config\\environments.json. Create it with a 'dev' entry.\r\n");

        Assert.Empty(environments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoOutputIsNoEnvironments(string output)
    {
        Assert.Empty(EnvironmentList.Parse(output));
    }

    [Fact]
    public void HandlesUnixLineEndings()
    {
        Assert.Single(EnvironmentList.Parse("Environments in /repo\n  dev  ->  https://contoso.crm5.dynamics.com/\n"));
    }

    [Fact]
    public void PrefersDevSoASingleSandboxNeverAsks()
    {
        var environments = EnvironmentList.Parse(Listing);

        Assert.Equal("dev", EnvironmentList.Preferred(environments).Name);
    }

    [Fact]
    public void FallsBackToTheFirstWhenThereIsNoDev()
    {
        var environments = EnvironmentList.Parse("  sandbox-a  ->  https://a.crm5.dynamics.com/\r\n  sandbox-b  ->  https://b.crm5.dynamics.com/\r\n");

        Assert.Equal("sandbox-a", EnvironmentList.Preferred(environments).Name);
    }

    [Fact]
    public void PreferredOfNothingIsNull()
    {
        Assert.Null(EnvironmentList.Preferred(EnvironmentList.Parse(string.Empty)));
        Assert.Null(EnvironmentList.Preferred(null));
    }
}
