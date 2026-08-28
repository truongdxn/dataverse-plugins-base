namespace Dataverse.Plugins.Tooling.Model;

/// <summary>
/// The contract between authoring and deployment. [PluginStep] attributes are read into this;
/// packaging and direct registration both read only this. Because both paths consume the same
/// document, a packaged import and a direct sync cannot disagree about what a step is.
/// </summary>
public sealed class PluginManifest
{
    public string SolutionUniqueName { get; set; }

    public string SolutionVersion { get; set; }

    public string PublisherUniqueName { get; set; }

    public string PublisherPrefix { get; set; }

    public DateTimeOffset GeneratedUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<ManifestAssembly> Assemblies { get; set; } = new();

    public IEnumerable<ManifestStep> AllSteps() =>
        Assemblies.SelectMany(a => a.Types).SelectMany(t => t.Steps);

    public IEnumerable<ManifestType> AllTypes() => Assemblies.SelectMany(a => a.Types);
}

public sealed class ManifestAssembly
{
    /// <summary>Simple assembly name, e.g. "Sample.Plugins".</summary>
    public string Name { get; set; }

    public Guid Id { get; set; }

    public string Version { get; set; }

    public string Culture { get; set; } = "neutral";

    public string PublicKeyToken { get; set; } = "null";

    /// <summary>Strong name as Dataverse stores it, e.g. "Sample.Plugins, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null".</summary>
    public string FullName { get; set; }

    public IsolationMode IsolationMode { get; set; } = IsolationMode.Sandbox;

    /// <summary>Absolute path to the built DLL. Not committed - it points into bin/.</summary>
    public string AssemblyPath { get; set; }

    public string ProjectPath { get; set; }

    public List<ManifestType> Types { get; set; } = new();
}

public sealed class ManifestType
{
    /// <summary>Full CLR type name, e.g. "Sample.Plugins.ContactPostUpdate".</summary>
    public string TypeName { get; set; }

    public Guid Id { get; set; }

    public string FriendlyName { get; set; }

    public string Description { get; set; }

    /// <summary>Assembly-qualified name Dataverse instantiates the plugin from.</summary>
    public string AssemblyQualifiedName { get; set; }

    public List<ManifestStep> Steps { get; set; } = new();

    /// <summary>
    /// Images whose StepName matched no step on this type. Recorded during the build so the
    /// validator can fail on them; left empty in a healthy manifest.
    /// </summary>
    public List<UnboundImage> UnboundImages { get; set; } = new();
}

/// <summary>A [PluginImage] whose StepName matched nothing - almost always a typo.</summary>
public sealed class UnboundImage
{
    public string ImageName { get; set; }

    public string StepName { get; set; }
}

public sealed class ManifestStep
{
    public Guid Id { get; set; }

    public string Name { get; set; }

    public string Message { get; set; }

    public string PrimaryEntity { get; set; }

    public string SecondaryEntity { get; set; }

    public Stage Stage { get; set; } = Stage.PostOperation;

    public ExecutionMode Mode { get; set; } = ExecutionMode.Synchronous;

    public int Order { get; set; } = 1;

    public string Description { get; set; }

    public string FilteringAttributes { get; set; }

    public string ImpersonatingUser { get; set; }

    public string UnsecureConfiguration { get; set; }

    public string SecureConfigurationKey { get; set; }

    public StepState State { get; set; } = StepState.Enabled;

    public bool AsyncAutoDelete { get; set; }

    public DeploymentTarget SupportedDeployment { get; set; } = DeploymentTarget.ServerOnly;

    /// <summary>Owning plugin type. Set during merge so a step can be handled on its own.</summary>
    public string TypeName { get; set; }

    public List<ManifestImage> Images { get; set; } = new();

    /// <summary>
    /// Identity of the step, independent of any generated id. Two declarations sharing a key are
    /// the same step, which is how a duplicate declaration on one class is detected.
    /// </summary>
    public string Key() => StepKey(TypeName, Message, PrimaryEntity, Name);

    public static string StepKey(string typeName, string message, string primaryEntity, string name) =>
        string.Join(
            "|",
            (typeName ?? string.Empty).Trim().ToLowerInvariant(),
            (message ?? string.Empty).Trim().ToLowerInvariant(),
            (primaryEntity ?? string.Empty).Trim().ToLowerInvariant(),
            (name ?? string.Empty).Trim().ToLowerInvariant());
}

public sealed class ManifestImage
{
    public Guid Id { get; set; }

    public string Name { get; set; }

    public ImageType ImageType { get; set; } = ImageType.PreImage;

    /// <summary>Comma separated columns, or empty for all columns.</summary>
    public string Attributes { get; set; }

    public string EntityAlias { get; set; }

    public string MessagePropertyName { get; set; } = "Target";

    /// <summary>Binds the image to one step when the class declares several.</summary>
    public string StepName { get; set; }
}
