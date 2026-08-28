using Dataverse.Plugins.Tooling.Configuration;
using Dataverse.Plugins.Tooling.Infrastructure;
using Dataverse.Plugins.Tooling.Model;

namespace Dataverse.Plugins.Tooling.Scanning;

/// <summary>One assembly's inputs: the project and what was scanned out of it.</summary>
public sealed record AssemblyInput(PluginProject Project, ScannedAssembly Scanned);

/// <summary>
/// Turns what the scanner read into the manifest, assigning the deterministic ids that let direct
/// registration and solution import agree on component identity.
/// </summary>
public sealed class ManifestBuilder
{
    private readonly SolutionConfig _config;

    public ManifestBuilder(SolutionConfig config)
    {
        _config = config;
    }

    public PluginManifest Build(IEnumerable<AssemblyInput> inputs)
    {
        var manifest = new PluginManifest
        {
            SolutionUniqueName = _config.Solution.UniqueName,
            SolutionVersion = _config.Solution.Version,
            PublisherUniqueName = _config.Publisher.UniqueName,
            PublisherPrefix = _config.Publisher.Prefix,
        };

        foreach (var input in inputs)
        {
            manifest.Assemblies.Add(BuildAssembly(input));
        }

        return manifest;
    }

    private ManifestAssembly BuildAssembly(AssemblyInput input)
    {
        var scanned = input.Scanned;
        var solution = _config.Solution.UniqueName;

        var assembly = new ManifestAssembly
        {
            Name = scanned.Name,
            Id = DeterministicGuid.Create(solution, "assembly", scanned.Name),
            Version = scanned.Version,
            Culture = scanned.Culture,
            PublicKeyToken = scanned.PublicKeyToken,
            FullName = scanned.FullName,
            IsolationMode = input.Project.IsolationMode,
            AssemblyPath = input.Project.TargetPath,
            ProjectPath = input.Project.ProjectPath,
        };

        foreach (var scannedType in scanned.Types)
        {
            var type = new ManifestType
            {
                TypeName = scannedType.TypeName,
                Id = DeterministicGuid.Create(solution, "type", scanned.Name, scannedType.TypeName),
                FriendlyName = scannedType.TypeName,
                AssemblyQualifiedName = $"{scannedType.TypeName}, {scanned.FullName}",
            };

            type.Steps.AddRange(BuildSteps(scannedType, assembly, type));

            // A type with no steps registers nothing, so it is not worth shipping.
            if (type.Steps.Count > 0)
            {
                assembly.Types.Add(type);
            }
        }

        assembly.Types = assembly.Types
            .OrderBy(type => type.TypeName, StringComparer.Ordinal)
            .ToList();

        return assembly;
    }

    private List<ManifestStep> BuildSteps(ScannedType scannedType, ManifestAssembly assembly, ManifestType type)
    {
        var steps = new List<ManifestStep>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var step in scannedType.Steps)
        {
            // Keyed on what was declared, before name defaulting, so two attributes that both
            // omit Name for the same message and table are caught rather than silently colliding.
            if (!seen.Add(step.Key()))
            {
                throw new ToolException(
                    $"{scannedType.TypeName} declares two [PluginStep] attributes for " +
                    $"'{step.Message}' on '{step.PrimaryEntity}' with the same name. Give at least " +
                    "one of them an explicit Name so the two steps can be told apart.");
            }

            step.Name = string.IsNullOrWhiteSpace(step.Name) ? DefaultStepName(step) : step.Name.Trim();
            step.Id = DeterministicGuid.Create(
                _config.Solution.UniqueName,
                "step",
                assembly.Name,
                step.TypeName,
                step.Message,
                step.PrimaryEntity,
                step.Name);

            steps.Add(step);
        }

        AttachClassLevelImages(scannedType, steps, type);

        foreach (var step in steps)
        {
            foreach (var image in step.Images)
            {
                image.EntityAlias = string.IsNullOrWhiteSpace(image.EntityAlias) ? image.Name : image.EntityAlias;
                image.MessagePropertyName = string.IsNullOrWhiteSpace(image.MessagePropertyName)
                    ? "Target"
                    : image.MessagePropertyName;

                // Derived from the step, so the same image name on two steps yields two distinct ids.
                image.Id = DeterministicGuid.Create(
                    _config.Solution.UniqueName,
                    "image",
                    step.Id.ToString(),
                    image.Name);
            }
        }

        return steps;
    }

    /// <summary>
    /// Appends images declared by a separate [PluginImage] to the steps they belong to. Images the
    /// step attribute itself declared are already in place and need no binding.
    /// <para>
    /// A StepName matching no step is recorded rather than ignored: dropping it silently produces a
    /// step whose image is simply missing, which only shows up at runtime.
    /// </para>
    /// </summary>
    private static void AttachClassLevelImages(ScannedType scannedType, List<ManifestStep> steps, ManifestType type)
    {
        foreach (var declared in scannedType.Images)
        {
            var bound = false;

            foreach (var step in steps)
            {
                if (!string.IsNullOrWhiteSpace(declared.StepName) &&
                    !string.Equals(declared.StepName, step.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                step.Images.Add(new ManifestImage
                {
                    Name = declared.Name,
                    ImageType = declared.ImageType,
                    Attributes = declared.Attributes ?? string.Empty,
                    EntityAlias = declared.EntityAlias,
                    MessagePropertyName = declared.MessagePropertyName,
                });

                bound = true;
            }

            if (!bound && !string.IsNullOrWhiteSpace(declared.StepName))
            {
                type.UnboundImages.Add(new UnboundImage
                {
                    ImageName = declared.Name,
                    StepName = declared.StepName,
                });
            }
        }
    }

    /// <summary>Matches the naming the Plugin Registration Tool uses, so steps look familiar in the UI.</summary>
    private static string DefaultStepName(ManifestStep step) =>
        string.IsNullOrWhiteSpace(step.PrimaryEntity)
            ? $"{step.TypeName}: {step.Message}"
            : $"{step.TypeName}: {step.Message} of {step.PrimaryEntity}";
}
