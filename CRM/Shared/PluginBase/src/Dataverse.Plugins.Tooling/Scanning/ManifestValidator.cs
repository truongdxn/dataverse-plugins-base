using Dataverse.Plugins.Tooling.Infrastructure;
using Dataverse.Plugins.Tooling.Model;

namespace Dataverse.Plugins.Tooling.Scanning;

public sealed class ValidationResult
{
    public List<string> Errors { get; } = new();

    public List<string> Warnings { get; } = new();

    public bool HasErrors => Errors.Count > 0;

    /// <summary>Prints everything found and throws if anything was fatal.</summary>
    public void Report()
    {
        foreach (var warning in Warnings)
        {
            Log.Warn(warning);
        }

        if (!HasErrors)
        {
            return;
        }

        throw new ToolException(
            $"Step configuration is invalid:{Environment.NewLine}  - " +
            string.Join(Environment.NewLine + "  - ", Errors));
    }
}

/// <summary>
/// Checks the merged manifest against the rules Dataverse enforces, so a bad registration fails
/// at build time with a clear message instead of at deploy time with an opaque platform error.
/// </summary>
public static class ManifestValidator
{
    private const int NameMaxLength = 256;

    public static ValidationResult Validate(PluginManifest manifest)
    {
        var result = new ValidationResult();
        var seenKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        var seenIds = new Dictionary<Guid, string>();

        foreach (var assembly in manifest.Assemblies)
        {
            foreach (var type in assembly.Types)
            {
                ValidateUnboundImages(type, result);

                foreach (var step in type.Steps)
                {
                    ValidateStep(step, result);
                    ValidateImages(step, result);

                    var key = step.Key();

                    if (seenKeys.TryGetValue(key, out var firstOwner))
                    {
                        result.Errors.Add(
                            $"Two steps resolve to the same identity ({step.Name}). " +
                            $"Seen on '{firstOwner}' and '{type.TypeName}'. Give one an explicit Name.");
                    }
                    else
                    {
                        seenKeys[key] = type.TypeName;
                    }

                    // A collision here would mean two components silently overwriting each other
                    // in the target environment, so it is worth an explicit check.
                    if (seenIds.TryGetValue(step.Id, out var idOwner))
                    {
                        result.Errors.Add(
                            $"Step '{step.Name}' generated the same id as '{idOwner}'.");
                    }
                    else
                    {
                        seenIds[step.Id] = step.Name;
                    }
                }
            }
        }

        if (manifest.Assemblies.Count == 0)
        {
            result.Errors.Add("No plugin assemblies were found.");
        }

        return result;
    }

    /// <summary>
    /// A [PluginImage] whose StepName matches no step would otherwise be dropped in silence, and
    /// the only symptom is a null image at runtime. Fail the build and name the alternatives.
    /// </summary>
    private static void ValidateUnboundImages(ManifestType type, ValidationResult result)
    {
        if (type.UnboundImages.Count == 0)
        {
            return;
        }

        var available = type.Steps.Count == 0
            ? "(this type declares no steps)"
            : string.Join(", ", type.Steps.Select(step => $"'{step.Name}'"));

        foreach (var unbound in type.UnboundImages)
        {
            result.Errors.Add(
                $"{type.TypeName}: image '{unbound.ImageName}' names step '{unbound.StepName}', " +
                $"which does not exist. Steps on this type are: {available}.");
        }
    }

    private static void ValidateStep(ManifestStep step, ValidationResult result)
    {
        var label = $"{step.TypeName} / {step.Message}";

        if (string.IsNullOrWhiteSpace(step.Message))
        {
            result.Errors.Add($"{step.TypeName}: a step must specify a message.");
        }

        if (step.Order < 0)
        {
            result.Errors.Add($"{label}: order must be zero or greater, got {step.Order}.");
        }

        if (step.Name is { Length: > NameMaxLength })
        {
            result.Errors.Add(
                $"{label}: step name is {step.Name.Length} characters; Dataverse allows {NameMaxLength}. " +
                "Set a shorter Name.");
        }

        // Dataverse only runs asynchronous steps after the operation has completed.
        if (step.Mode == ExecutionMode.Asynchronous && step.Stage != Stage.PostOperation)
        {
            result.Errors.Add(
                $"{label}: asynchronous steps must be registered in PostOperation, not {step.Stage}.");
        }

        if (!string.IsNullOrWhiteSpace(step.FilteringAttributes) &&
            !string.Equals(step.Message, "Update", StringComparison.OrdinalIgnoreCase))
        {
            result.Warnings.Add(
                $"{label}: filteringAttributes only affects Update steps and will be ignored here.");
        }

        if (string.Equals(step.Message, "Update", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(step.FilteringAttributes))
        {
            result.Warnings.Add(
                $"{label}: no filteringAttributes, so this step fires on every column change. " +
                "Set them unless it genuinely needs to.");
        }

        if (!string.IsNullOrWhiteSpace(step.UnsecureConfiguration) &&
            LooksLikeASecret(step.UnsecureConfiguration))
        {
            result.Warnings.Add(
                $"{label}: unsecureConfiguration looks like it contains a secret. It is readable by " +
                "any user - use secureConfigurationKey instead.");
        }
    }

    private static void ValidateImages(ManifestStep step, ValidationResult result)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var image in step.Images)
        {
            var label = $"{step.TypeName} / {step.Message} / image '{image.Name}'";

            if (string.IsNullOrWhiteSpace(image.Name))
            {
                result.Errors.Add($"{step.TypeName} / {step.Message}: an image must have a name.");
                continue;
            }

            if (!names.Add(image.Name))
            {
                result.Errors.Add($"{label}: declared more than once on the same step.");
            }

            var wantsPre = image.ImageType is ImageType.PreImage or ImageType.Both;
            var wantsPost = image.ImageType is ImageType.PostImage or ImageType.Both;

            // A pre-image is the row as it was before the operation, so there isn't one on Create.
            if (wantsPre && string.Equals(step.Message, "Create", StringComparison.OrdinalIgnoreCase))
            {
                result.Errors.Add(
                    $"{label}: Create has no pre-image, because the row does not exist yet. " +
                    "Bind the image to a specific step with StepName, or use the PreImage/PostImage " +
                    "shorthand on the step that should carry it.");
            }

            // A post-image is the row after the operation - gone on Delete, not yet written before it.
            if (wantsPost && string.Equals(step.Message, "Delete", StringComparison.OrdinalIgnoreCase))
            {
                result.Errors.Add(
                    $"{label}: Delete has no post-image, because the row no longer exists. " +
                    "Bind the image to a specific step with StepName, or use the PreImage/PostImage " +
                    "shorthand on the step that should carry it.");
            }

            if (wantsPost && step.Stage != Stage.PostOperation)
            {
                result.Errors.Add(
                    $"{label}: a post-image is only available in PostOperation, not {step.Stage}.");
            }

            if (string.IsNullOrWhiteSpace(image.Attributes))
            {
                result.Warnings.Add(
                    $"{label}: no columns listed, so the whole row is loaded. List the columns it reads.");
            }
        }
    }

    private static bool LooksLikeASecret(string value)
    {
        string[] markers = ["password", "secret", "apikey", "api_key", "connectionstring", "pwd="];
        return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
