using Dataverse.Plugins.Tooling.Model;

namespace Dataverse.Plugins.Tooling.Scanning;

/// <summary>What the scanner read out of one built plugin assembly.</summary>
public sealed class ScannedAssembly
{
    public string Name { get; init; }

    public string Version { get; init; }

    public string Culture { get; init; } = "neutral";

    public string PublicKeyToken { get; init; } = "null";

    /// <summary>Strong name in the form Dataverse stores it.</summary>
    public string FullName => $"{Name}, Version={Version}, Culture={Culture}, PublicKeyToken={PublicKeyToken}";

    public List<ScannedType> Types { get; init; } = new();
}

/// <summary>A plugin class, with whatever its attributes declared.</summary>
public sealed class ScannedType
{
    public string TypeName { get; init; }

    public List<ManifestStep> Steps { get; init; } = new();

    public List<ManifestImage> Images { get; init; } = new();
}
