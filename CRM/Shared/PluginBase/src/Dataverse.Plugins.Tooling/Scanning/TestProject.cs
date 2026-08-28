namespace Dataverse.Plugins.Tooling.Scanning;

/// <summary>A discovered plugin test project and the assembly it declares it covers.</summary>
public sealed class TestProject
{
    public string ProjectPath { get; init; }

    public string ProjectName { get; init; }

    /// <summary>
    /// The plugin assembly this project tests, from &lt;DataverseTestsFor&gt;. Declared rather than
    /// inferred from the project name: 'dv test -a' filters on it, and a name-matching convention
    /// fails silently the first time somebody names a project differently.
    /// </summary>
    public string TestsFor { get; init; }
}
