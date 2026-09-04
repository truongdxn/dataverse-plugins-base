using System.Xml.Linq;
using Dataverse.Plugins.Tooling.Configuration;
using Dataverse.Plugins.Tooling.Deployment;
using Dataverse.Plugins.Tooling.Infrastructure;
using Dataverse.Plugins.Tooling.Model;

namespace Dataverse.Plugins.Tooling.Packaging;

/// <summary>
/// Writes the manifest out as a SolutionPackager source tree, ready for <c>pac solution pack</c>.
/// <para>
/// The layout below was established by driving the packager and inspecting the zip it produced,
/// not inferred. Two details matter and are easy to get wrong:
/// </para>
/// <list type="bullet">
/// <item>
/// Steps are a <b>sharded</b> component. <c>SdkMessageProcessingSteps</c> must be a childless
/// element in Customizations.xml, with one file per step under SdkMessageProcessingSteps/.
/// Inlining them instead makes the packager log "has unexpected children ... will be skipped"
/// and drop every step from the zip while still exiting successfully.
/// </item>
/// <item>
/// Element order inside each file follows the published schema sequence. The packager itself is
/// lenient about order, but the platform validates against the schema on import.
/// </item>
/// </list>
/// </summary>
public sealed class SolutionSourceWriter
{
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    private readonly SolutionConfig _config;
    private readonly SdkMessageCache _messages;

    public SolutionSourceWriter(SolutionConfig config, SdkMessageCache messages)
    {
        _config = config;
        _messages = messages;
    }

    /// <summary>
    /// Deletes the scratch tree, retrying briefly.
    /// <para>
    /// A file sync client, an antivirus scan or an open Explorer window can hold a handle for a
    /// moment, and the raw <see cref="IOException"/> would surface as an unhandled crash with a
    /// stack trace - reading as a bug in the tool when it is a lock the person at the keyboard can
    /// actually do something about. This repo commonly lives in a synced folder, so it is worth
    /// waiting out rather than failing on.
    /// </para>
    /// </summary>
    private static void Clear(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // Directory.Delete refuses a read-only entry outright, so clear the attribute
                // first - the equivalent of what 'Remove-Item -Force' does.
                foreach (var entry in Directory.EnumerateFileSystemEntries(
                             directory, "*", SearchOption.AllDirectories))
                {
                    var attributes = File.GetAttributes(entry);

                    if (attributes.HasFlag(FileAttributes.ReadOnly))
                    {
                        File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly);
                    }
                }

                Directory.Delete(directory, recursive: true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == 5)
                {
                    throw new ToolException(
                        $"Could not clear '{directory}' - something is holding a file open in it. " +
                        "A file sync client (OneDrive), an antivirus scan or an open Explorer " +
                        "window are the usual causes. Close what is using it, or delete the " +
                        $"folder by hand, and run the command again.{Environment.NewLine}" +
                        exception.Message,
                        exception);
                }

                Thread.Sleep(200 * attempt);
            }
        }
    }

    public void Write(PluginManifest manifest, string outputDirectory, string version)
    {
        // Regenerated wholesale every time; a stale step file left behind would otherwise be
        // packed into the zip long after its declaration was deleted.
        Clear(outputDirectory);

        Directory.CreateDirectory(outputDirectory);

        WriteSolutionXml(manifest, outputDirectory, version);
        WriteCustomizationsXml(outputDirectory);
        WriteRelationshipsXml(outputDirectory);

        foreach (var assembly in manifest.Assemblies)
        {
            WriteAssembly(assembly, outputDirectory);
        }

        WriteSteps(manifest, outputDirectory);
    }

    private void WriteSolutionXml(PluginManifest manifest, string outputDirectory, string version)
    {
        var publisher = _config.Publisher;
        var solution = _config.Solution;

        var rootComponents = new XElement("RootComponents");

        // 91 = plug-in assembly, 92 = SDK message processing step. Images are owned by their
        // step and must not be listed separately.
        foreach (var assembly in manifest.Assemblies)
        {
            rootComponents.Add(new XElement(
                "RootComponent",
                new XAttribute("type", 91),
                new XAttribute("id", Braced(assembly.Id)),
                new XAttribute("behavior", 0)));
        }

        foreach (var step in manifest.AllSteps())
        {
            rootComponents.Add(new XElement(
                "RootComponent",
                new XAttribute("type", 92),
                new XAttribute("id", Braced(step.Id)),
                new XAttribute("behavior", 0)));
        }

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                "ImportExportXml",
                new XAttribute("version", "9.1.0.643"),
                new XAttribute("SolutionPackageVersion", "9.1"),
                new XAttribute("languagecode", 1033),
                new XAttribute("generatedBy", "CrmLive"),
                new XAttribute(XNamespace.Xmlns + "xsi", Xsi.NamespaceName),
                new XElement(
                    "SolutionManifest",
                    new XElement("UniqueName", solution.UniqueName),
                    new XElement(
                        "LocalizedNames",
                        new XElement(
                            "LocalizedName",
                            new XAttribute("description", solution.FriendlyName ?? solution.UniqueName),
                            new XAttribute("languagecode", 1033))),
                    new XElement("Descriptions", DescriptionElement(solution.Description)),
                    new XElement("Version", version),
                    // 0 unmanaged / 1 managed / 2 both. Both, so the same source can produce either.
                    new XElement("Managed", 2),
                    new XElement(
                        "Publisher",
                        new XElement("UniqueName", publisher.UniqueName),
                        new XElement(
                            "LocalizedNames",
                            new XElement(
                                "LocalizedName",
                                new XAttribute("description", publisher.FriendlyName ?? publisher.UniqueName),
                                new XAttribute("languagecode", 1033))),
                        new XElement("Descriptions", DescriptionElement(publisher.Description)),
                        Nil("EMailAddress"),
                        Nil("SupportingWebsiteUrl"),
                        new XElement("CustomizationPrefix", publisher.Prefix),
                        new XElement("CustomizationOptionValuePrefix", publisher.OptionValuePrefix),
                        new XElement("Addresses", Address(1), Address(2))),
                    rootComponents,
                    new XElement("MissingDependencies"))));

        Save(document, Path.Combine(outputDirectory, "Other", "Solution.xml"));
    }

    /// <summary>
    /// Every component group is childless: assemblies and steps are both sharded into their own
    /// folders, and the packager only reads those folders when the element here is empty.
    /// Element order follows the schema sequence.
    /// </summary>
    private static void WriteCustomizationsXml(string outputDirectory)
    {
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                "ImportExportXml",
                new XAttribute(XNamespace.Xmlns + "xsi", Xsi.NamespaceName),
                new XElement("Entities"),
                new XElement("Roles"),
                new XElement("Workflows"),
                new XElement("FieldSecurityProfiles"),
                new XElement("Templates"),
                new XElement("EntityMaps"),
                new XElement("EntityRelationships"),
                new XElement("OrganizationSettings"),
                new XElement("optionsets"),
                new XElement("CustomControls"),
                new XElement("SolutionPluginAssemblies"),
                new XElement("SdkMessageProcessingSteps"),
                new XElement("EntityDataProviders"),
                new XElement("Languages", new XElement("Language", 1033))));

        Save(document, Path.Combine(outputDirectory, "Other", "Customizations.xml"));
    }

    private static void WriteRelationshipsXml(string outputDirectory)
    {
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("EntityRelationships", new XAttribute(XNamespace.Xmlns + "xsi", Xsi.NamespaceName)));

        Save(document, Path.Combine(outputDirectory, "Other", "Relationships.xml"));
    }

    private static void WriteAssembly(ManifestAssembly assembly, string outputDirectory)
    {
        // Folder name is "<AssemblyName>-<PluginAssemblyId>", unbraced and lower case.
        var folderName = $"{assembly.Name}-{assembly.Id.ToString("D").ToLowerInvariant()}";
        var folder = Path.Combine(outputDirectory, "PluginAssemblies", folderName);
        Directory.CreateDirectory(folder);

        var dllFileName = $"{assembly.Name}.dll";
        File.Copy(assembly.AssemblyPath, Path.Combine(folder, dllFileName), overwrite: true);

        var pluginTypes = new XElement("PluginTypes");

        foreach (var type in assembly.Types)
        {
            pluginTypes.Add(new XElement(
                "PluginType",
                new XAttribute("Name", type.TypeName),
                new XAttribute("AssemblyQualifiedName", type.AssemblyQualifiedName),
                new XAttribute("PluginTypeId", Braced(type.Id)),
                new XElement("Description", type.Description ?? string.Empty),
                new XElement("FriendlyName", type.FriendlyName ?? type.TypeName),
                new XElement("WorkflowActivityGroupName", string.Empty)));
        }

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                "PluginAssembly",
                new XAttribute("FullName", assembly.FullName),
                new XAttribute("PluginAssemblyId", Braced(assembly.Id)),
                new XElement("Description", string.Empty),
                new XElement("IsolationMode", (int)assembly.IsolationMode),
                // 0 = the assembly bytes travel in the solution, rather than a disk or database path.
                new XElement("SourceType", 0),
                new XElement("IsCustomizable", 1),
                new XElement("FileName", $"/PluginAssemblies/{folderName}/{dllFileName}"),
                pluginTypes));

        Save(document, Path.Combine(folder, dllFileName + ".data.xml"));
        Log.Detail($"  Wrote assembly {assembly.Name} ({assembly.Types.Count} type(s)).");
    }

    private void WriteSteps(PluginManifest manifest, string outputDirectory)
    {
        var folder = Path.Combine(outputDirectory, "SdkMessageProcessingSteps");
        Directory.CreateDirectory(folder);

        var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var step in manifest.AllSteps())
        {
            var document = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                BuildStepElement(step));

            // Prefer the readable name; fall back to the id if two steps sanitise to one filename.
            var fileName = SanitiseFileName(step.Name);

            if (!usedFileNames.Add(fileName))
            {
                fileName = step.Id.ToString("D");
                usedFileNames.Add(fileName);
            }

            Save(document, Path.Combine(folder, fileName + ".xml"));
        }

        Log.Detail($"  Wrote {manifest.AllSteps().Count()} step file(s).");
    }

    private XElement BuildStepElement(ManifestStep step)
    {
        var element = new XElement(
            "SdkMessageProcessingStep",
            new XAttribute("SdkMessageProcessingStepId", Braced(step.Id)),
            new XAttribute("Name", step.Name),
            new XElement("PluginTypeName", step.TypeName),
            // "none" is the sentinel for a step registered against every table.
            new XElement("PrimaryEntity", Entity(step.PrimaryEntity)),
            new XElement("SecondaryEntity", Entity(step.SecondaryEntity)),
            new XElement("AsyncAutoDelete", Bit(step.AsyncAutoDelete)),
            new XElement("Configuration", step.UnsecureConfiguration ?? string.Empty),
            new XElement("Description", step.Description ?? step.Name),
            new XElement("FilteringAttributes", step.FilteringAttributes ?? string.Empty),
            new XElement("ImpersonatingUserIdName", step.ImpersonatingUser ?? string.Empty),
            // 0 = server. Parent/child invocation sources are legacy.
            new XElement("InvocationSource", 0),
            new XElement("Mode", (int)step.Mode),
            new XElement("Rank", step.Order),
            new XElement("SdkMessageId", Braced(_messages.Require(step.Message, step.Name))),
            new XElement("Stage", (int)step.Stage),
            new XElement("IsCustomizable", 1),
            new XElement("IsHidden", 0),
            new XElement("SupportedDeployment", (int)step.SupportedDeployment));

        if (step.Images.Count > 0)
        {
            var images = new XElement("SdkMessageProcessingStepImages");

            foreach (var image in step.Images)
            {
                images.Add(new XElement(
                    "SdkMessageProcessingStepImage",
                    new XAttribute("Name", image.Name),
                    new XElement("Description", string.Empty),
                    new XElement("SdkMessageProcessingStepImageId", Braced(image.Id)),
                    new XElement("Attributes", image.Attributes ?? string.Empty),
                    new XElement("EntityAlias", image.EntityAlias ?? image.Name),
                    new XElement("ImageType", (int)image.ImageType),
                    new XElement("MessagePropertyName", image.MessagePropertyName ?? "Target"),
                    new XElement("IsCustomizable", 1)));
            }

            element.Add(images);
        }

        return element;
    }

    private static XElement DescriptionElement(string description) =>
        string.IsNullOrWhiteSpace(description)
            ? null
            : new XElement(
                "Description",
                new XAttribute("description", description),
                new XAttribute("languagecode", 1033));

    private static XElement Nil(string name) =>
        new(name, new XAttribute(Xsi + "nil", "true"));

    private static XElement Address(int number) =>
        new(
            "Address",
            new XElement("AddressNumber", number),
            new XElement("AddressTypeCode", 1),
            new XElement("ShippingMethodCode", 1));

    private static string Entity(string logicalName) =>
        string.IsNullOrWhiteSpace(logicalName) ? "none" : logicalName.Trim().ToLowerInvariant();

    private static int Bit(bool value) => value ? 1 : 0;

    private static string Braced(Guid id) => id.ToString("B").ToLowerInvariant();

    /// <summary>
    /// Step names carry a colon by convention ("Type: Update of account"), which is not legal in
    /// a file name. Note the packager also mis-parses "name-guid.xml", so the id is never
    /// appended to a name - collisions fall back to the bare id instead.
    /// </summary>
    private static string SanitiseFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat([':']).ToHashSet();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();

        // Keep well inside MAX_PATH once the artifacts folder prefix is added.
        return cleaned.Length > 120 ? cleaned[..120].TrimEnd() : cleaned;
    }

    private static void Save(XDocument document, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        document.Save(stream);
    }
}
