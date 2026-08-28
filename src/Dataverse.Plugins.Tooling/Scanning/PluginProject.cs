using Dataverse.Plugins.Tooling.Model;

namespace Dataverse.Plugins.Tooling.Scanning;

/// <summary>A discovered plugin assembly project and its built output.</summary>
public sealed class PluginProject
{
    public string ProjectPath { get; init; }

    public string AssemblyName { get; init; }

    /// <summary>Absolute path to the built DLL, from MSBuild's TargetPath.</summary>
    public string TargetPath { get; init; }

    public IsolationMode IsolationMode { get; init; } = IsolationMode.Sandbox;
}
