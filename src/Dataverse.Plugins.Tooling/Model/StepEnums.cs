namespace Dataverse.Plugins.Tooling.Model;

/*
 * These mirror Dataverse.Plugins.Abstractions.Registration by name and numeric value.
 *
 * They are duplicated rather than shared because the tooling targets net8.0 while plugin
 * assemblies target net462, and a net8.0 project cannot reference a net462 one. The scanner
 * reads the raw numeric attribute values out of the plugin assembly and maps them onto these,
 * so the two sets must keep the same values. ManifestValidatorTests pins that.
 */

public enum Stage
{
    PreValidation = 10,
    PreOperation = 20,
    PostOperation = 40,
}

public enum ExecutionMode
{
    Synchronous = 0,
    Asynchronous = 1,
}

public enum ImageType
{
    PreImage = 0,
    PostImage = 1,
    Both = 2,
}

public enum IsolationMode
{
    None = 1,
    Sandbox = 2,
}

public enum StepState
{
    Enabled = 0,
    Disabled = 1,
}

public enum DeploymentTarget
{
    ServerOnly = 0,
    OutlookClientOnly = 1,
    Both = 2,
}
