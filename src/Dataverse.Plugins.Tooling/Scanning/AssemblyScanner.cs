using System.Reflection;
using Dataverse.Plugins.Tooling.Infrastructure;
using Dataverse.Plugins.Tooling.Model;

namespace Dataverse.Plugins.Tooling.Scanning;

/// <summary>
/// Reads registration attributes out of a built plugin assembly.
/// <para>
/// The assembly targets net462 while this tool runs on net8.0, so it is opened with
/// <see cref="MetadataLoadContext"/> - metadata only, never executed. Attribute values are read
/// as <see cref="CustomAttributeData"/> rather than by instantiating the attributes, which is
/// what makes reading across the framework boundary work at all.
/// </para>
/// </summary>
public sealed class AssemblyScanner
{
    private const string StepAttributeName = "Dataverse.Plugins.Abstractions.Registration.PluginStepAttribute";
    private const string ImageAttributeName = "Dataverse.Plugins.Abstractions.Registration.PluginImageAttribute";
    private const string PluginInterfaceName = "Microsoft.Xrm.Sdk.IPlugin";

    // Shorthand image properties on PluginStepAttribute. They have no counterpart on ManifestStep
    // (they become images), so they cannot be matched with nameof.
    private const string PreImageMember = "PreImage";
    private const string PostImageMember = "PostImage";

    public ScannedAssembly Scan(string assemblyPath)
    {
        if (!File.Exists(assemblyPath))
        {
            throw new ToolException($"Assembly not found: {assemblyPath}");
        }

        using var context = new MetadataLoadContext(new PathAssemblyResolver(BuildSearchPaths(assemblyPath)));

        Assembly assembly;

        try
        {
            assembly = context.LoadFromAssemblyPath(assemblyPath);
        }
        catch (Exception ex)
        {
            throw new ToolException($"Could not read '{assemblyPath}': {ex.Message}", ex);
        }

        var identity = assembly.GetName();
        var types = new List<ScannedType>();

        foreach (var type in GetTypes(assembly, assemblyPath))
        {
            if (!IsPluginCandidate(type))
            {
                continue;
            }

            var scanned = ScanType(type);
            types.Add(scanned);
            Log.Detail($"  {scanned.TypeName}: {scanned.Steps.Count} step(s), {scanned.Images.Count} image(s) from attributes.");
        }

        return new ScannedAssembly
        {
            Name = identity.Name,
            Version = identity.Version?.ToString() ?? "1.0.0.0",
            Culture = string.IsNullOrEmpty(identity.CultureName) ? "neutral" : identity.CultureName,
            PublicKeyToken = FormatPublicKeyToken(identity.GetPublicKeyToken()),
            Types = types.OrderBy(t => t.TypeName, StringComparer.Ordinal).ToList(),
        };
    }

    /// <summary>
    /// The plugin folder first, then this tool's runtime, so the plugin's net462 copies of shared
    /// libraries win over the net8.0 ones. The runtime folder is what supplies mscorlib.
    /// </summary>
    private static IEnumerable<string> BuildSearchPaths(string assemblyPath)
    {
        var assemblyDirectory = Path.GetDirectoryName(Path.GetFullPath(assemblyPath));
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>();

        foreach (var directory in new[] { assemblyDirectory, runtimeDirectory })
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.dll"))
            {
                // First occurrence wins, so a duplicate simple name never makes the resolver ambiguous.
                if (seen.Add(Path.GetFileName(file)))
                {
                    paths.Add(file);
                }
            }
        }

        return paths;
    }

    private static IEnumerable<Type> GetTypes(Assembly assembly, string assemblyPath)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // One unreadable type should not hide every readable one; report and carry on.
            Log.Warn(
                $"Some types in '{Path.GetFileName(assemblyPath)}' could not be read and were skipped. " +
                "Run with --verbose for detail.");

            foreach (var loaderException in ex.LoaderExceptions)
            {
                if (loaderException is not null)
                {
                    Log.Detail(loaderException.Message);
                }
            }

            return ex.Types.Where(type => type is not null);
        }
    }

    private static bool IsPluginCandidate(Type type)
    {
        if (!type.IsClass || type.IsAbstract || !type.IsPublic)
        {
            return false;
        }

        try
        {
            return type.GetInterfaces().Any(i => string.Equals(i.FullName, PluginInterfaceName, StringComparison.Ordinal));
        }
        catch (FileNotFoundException)
        {
            // Microsoft.Xrm.Sdk was not resolvable. Fall back to the attributes: a type carrying
            // a [PluginStep] is a plugin whether or not we could walk its interfaces.
            return HasStepAttribute(type);
        }
        catch (TypeLoadException)
        {
            return HasStepAttribute(type);
        }
    }

    private static bool HasStepAttribute(Type type) =>
        type.GetCustomAttributesData()
            .Any(a => string.Equals(a.AttributeType.FullName, StepAttributeName, StringComparison.Ordinal));

    private static ScannedType ScanType(Type type)
    {
        var scanned = new ScannedType { TypeName = type.FullName };

        foreach (var attribute in type.GetCustomAttributesData())
        {
            var attributeName = attribute.AttributeType.FullName;

            if (string.Equals(attributeName, StepAttributeName, StringComparison.Ordinal))
            {
                scanned.Steps.Add(ReadStep(type, attribute));
            }
            else if (string.Equals(attributeName, ImageAttributeName, StringComparison.Ordinal))
            {
                scanned.Images.Add(ReadImage(attribute));
            }
        }

        return scanned;
    }

    private static ManifestStep ReadStep(Type type, CustomAttributeData attribute)
    {
        var step = new ManifestStep
        {
            TypeName = type.FullName,
            Message = ConstructorString(attribute, 0),
            PrimaryEntity = ConstructorString(attribute, 1) ?? string.Empty,
        };

        // Null means the shorthand was not used at all; empty string means it was used with no
        // columns, which registers an image over every column. The two must stay distinguishable.
        string preImageColumns = null;
        string postImageColumns = null;

        foreach (var argument in attribute.NamedArguments)
        {
            var value = argument.TypedValue.Value;

            switch (argument.MemberName)
            {
                case nameof(ManifestStep.Name):
                    step.Name = value as string;
                    break;
                case nameof(ManifestStep.SecondaryEntity):
                    step.SecondaryEntity = value as string;
                    break;
                case nameof(ManifestStep.Stage):
                    step.Stage = (Stage)Convert.ToInt32(value);
                    break;
                case nameof(ManifestStep.Mode):
                    step.Mode = (ExecutionMode)Convert.ToInt32(value);
                    break;
                case nameof(ManifestStep.Order):
                    step.Order = Convert.ToInt32(value);
                    break;
                case nameof(ManifestStep.Description):
                    step.Description = value as string;
                    break;
                case nameof(ManifestStep.FilteringAttributes):
                    step.FilteringAttributes = JoinColumns(value);
                    break;
                case PreImageMember:
                    preImageColumns = JoinColumns(value);
                    break;
                case PostImageMember:
                    postImageColumns = JoinColumns(value);
                    break;
                case nameof(ManifestStep.ImpersonatingUser):
                    step.ImpersonatingUser = value as string;
                    break;
                case nameof(ManifestStep.UnsecureConfiguration):
                    step.UnsecureConfiguration = value as string;
                    break;
                case nameof(ManifestStep.SecureConfigurationKey):
                    step.SecureConfigurationKey = value as string;
                    break;
                case nameof(ManifestStep.State):
                    step.State = (StepState)Convert.ToInt32(value);
                    break;
                case nameof(ManifestStep.AsyncAutoDelete):
                    step.AsyncAutoDelete = Convert.ToBoolean(value);
                    break;
                case nameof(ManifestStep.SupportedDeployment):
                    step.SupportedDeployment = (DeploymentTarget)Convert.ToInt32(value);
                    break;
            }
        }

        // Images declared on the step itself belong to that step by construction, so there is no
        // step name to match and nothing that can silently fail to bind.
        AddShorthandImage(step, ImageType.PreImage, "PreImage", preImageColumns);
        AddShorthandImage(step, ImageType.PostImage, "PostImage", postImageColumns);

        return step;
    }

    private static void AddShorthandImage(ManifestStep step, ImageType imageType, string name, string columns)
    {
        if (columns is null)
        {
            return;
        }

        step.Images.Add(new ManifestImage
        {
            Name = name,
            ImageType = imageType,
            Attributes = columns,
            EntityAlias = name,
            MessagePropertyName = "Target",
        });
    }

    private static ManifestImage ReadImage(CustomAttributeData attribute)
    {
        var image = new ManifestImage
        {
            ImageType = (ImageType)Convert.ToInt32(attribute.ConstructorArguments[0].Value),
            Name = ConstructorString(attribute, 1),
            // The columns parameter is 'params string[]', so it arrives as an array argument.
            Attributes = JoinColumns(ConstructorValue(attribute, 2)) ?? string.Empty,
        };

        foreach (var argument in attribute.NamedArguments)
        {
            switch (argument.MemberName)
            {
                case nameof(ManifestImage.EntityAlias):
                    image.EntityAlias = argument.TypedValue.Value as string;
                    break;
                case nameof(ManifestImage.StepName):
                    image.StepName = argument.TypedValue.Value as string;
                    break;
                case nameof(ManifestImage.MessagePropertyName):
                    image.MessagePropertyName = argument.TypedValue.Value as string;
                    break;
            }
        }

        return image;
    }

    private static string ConstructorString(CustomAttributeData attribute, int index) =>
        ConstructorValue(attribute, index) as string;

    private static object ConstructorValue(CustomAttributeData attribute, int index) =>
        attribute.ConstructorArguments.Count > index
            ? attribute.ConstructorArguments[index].Value
            : null;

    /// <summary>
    /// Flattens an array-valued attribute argument into the comma separated form Dataverse stores.
    /// Array arguments arrive as a collection of <see cref="CustomAttributeTypedArgument"/>, one
    /// per element, rather than as a plain array. Returns null when the argument was absent, so a
    /// caller can tell "not specified" from "specified as empty".
    /// </summary>
    private static string JoinColumns(object value)
    {
        switch (value)
        {
            case null:
                return null;

            // Tolerated so a hand-written comma separated string still works.
            case string text:
                return text.Trim();

            case IEnumerable<CustomAttributeTypedArgument> elements:
                return string.Join(
                    ",",
                    elements
                        .Select(element => element.Value as string)
                        .Where(column => !string.IsNullOrWhiteSpace(column))
                        .Select(column => column.Trim()));

            default:
                return null;
        }
    }

    private static string FormatPublicKeyToken(byte[] token) =>
        token is null || token.Length == 0
            ? "null"
            : string.Concat(token.Select(b => b.ToString("x2")));
}
