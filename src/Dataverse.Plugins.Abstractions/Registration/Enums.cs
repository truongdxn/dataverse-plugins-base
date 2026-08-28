using System;

namespace Dataverse.Plugins.Abstractions.Registration
{
    /// <summary>Pipeline stage. Values are the numbers Dataverse stores in sdkmessageprocessingstep.stage.</summary>
    public enum Stage
    {
        PreValidation = 10,
        PreOperation = 20,
        PostOperation = 40
    }

    /// <summary>sdkmessageprocessingstep.mode</summary>
    public enum ExecutionMode
    {
        Synchronous = 0,
        Asynchronous = 1
    }

    /// <summary>sdkmessageprocessingstepimage.imagetype</summary>
    public enum ImageType
    {
        PreImage = 0,
        PostImage = 1,
        Both = 2
    }

    /// <summary>pluginassembly.isolationmode</summary>
    public enum IsolationMode
    {
        None = 1,
        Sandbox = 2
    }

    /// <summary>Initial statecode for the registered step.</summary>
    public enum StepState
    {
        Enabled = 0,
        Disabled = 1
    }

    /// <summary>sdkmessageprocessingstep.supporteddeployment</summary>
    public enum Deployment
    {
        ServerOnly = 0,
        OutlookClientOnly = 1,
        Both = 2
    }
}
