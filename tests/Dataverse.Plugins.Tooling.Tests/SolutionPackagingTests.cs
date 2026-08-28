using System.IO.Compression;
using System.Xml.Linq;
using Dataverse.Plugins.Tooling.Configuration;
using Dataverse.Plugins.Tooling.Deployment;
using Dataverse.Plugins.Tooling.Infrastructure;
using Dataverse.Plugins.Tooling.Model;
using Dataverse.Plugins.Tooling.Packaging;
using Xunit;

namespace Dataverse.Plugins.Tooling.Tests;

/// <summary>
/// End-to-end check of the packaging path: manifest -> SolutionPackager source -> real
/// <c>pac solution pack</c> -> assertions against the zip it produced.
/// <para>
/// This exists because the packager's two worst failure modes are silent. Given a malformed
/// source tree it prints "has unexpected children ... will be skipped" or "root components are
/// not defined in customizations", drops the components, and still exits zero - producing a zip
/// that imports cleanly and registers nothing. Only inspecting the output catches that.
/// </para>
/// </summary>
public class SolutionPackagingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dv-pack-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void PackedSolutionContainsEveryStepImageAndAssembly()
    {
        RequirePac();

        var manifest = BuildManifest();
        var sourceDirectory = Path.Combine(_root, "solution-src");
        var zipPath = Path.Combine(_root, "Test.zip");

        new SolutionSourceWriter(Config(), MessageCache()).Write(manifest, sourceDirectory, "1.0.0.9");

        // Throws if the packager reported that anything was skipped.
        SolutionPacker.Pack(sourceDirectory, zipPath, managed: false);

        using var zip = ZipFile.OpenRead(zipPath);

        var entries = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("customizations.xml", entries);
        Assert.Contains("solution.xml", entries);
        Assert.Contains("[Content_Types].xml", entries);

        // The assembly bytes must physically travel with the package.
        Assert.Contains(entries, e => e.StartsWith("PluginAssemblies/", StringComparison.Ordinal)
                                      && e.EndsWith("Sample.Plugins.dll", StringComparison.Ordinal));

        var customizations = ReadXml(zip, "customizations.xml");

        var stepNames = customizations
            .Descendants("SdkMessageProcessingStep")
            .Select(e => e.Attribute("Name")?.Value)
            .ToList();

        Assert.Equal(2, stepNames.Count);
        Assert.Contains("Sample.Plugins.Thing: Update of contact", stepNames);
        Assert.Contains("Sample.Plugins.Thing: Create of contact", stepNames);

        var image = Assert.Single(customizations.Descendants("SdkMessageProcessingStepImage"));
        Assert.Equal("PreImage", image.Attribute("Name")?.Value);
        Assert.Equal("firstname,lastname", image.Element("Attributes")?.Value);

        var pluginType = Assert.Single(customizations.Descendants("PluginType"));
        Assert.Equal("Sample.Plugins.Thing", pluginType.Attribute("Name")?.Value);

        var update = customizations
            .Descendants("SdkMessageProcessingStep")
            .Single(e => e.Attribute("Name")!.Value.Contains("Update"));

        Assert.Equal("contact", update.Element("PrimaryEntity")?.Value);
        Assert.Equal("40", update.Element("Stage")?.Value);        // PostOperation
        Assert.Equal("20", update.Element("Rank")?.Value);
        Assert.Equal("firstname,lastname", update.Element("FilteringAttributes")?.Value);
    }

    [Fact]
    public void SolutionXmlListsEveryComponentAsARootComponent()
    {
        RequirePac();

        var manifest = BuildManifest();
        var sourceDirectory = Path.Combine(_root, "solution-src");
        var zipPath = Path.Combine(_root, "Test.zip");

        new SolutionSourceWriter(Config(), MessageCache()).Write(manifest, sourceDirectory, "1.0.0.9");
        SolutionPacker.Pack(sourceDirectory, zipPath, managed: false);

        using var zip = ZipFile.OpenRead(zipPath);
        var solution = ReadXml(zip, "solution.xml");

        var types = solution
            .Descendants("RootComponent")
            .Select(e => e.Attribute("type")?.Value)
            .ToList();

        // A component missing here imports but never becomes part of the solution.
        Assert.Equal(1, types.Count(t => t == "91"));  // the assembly
        Assert.Equal(2, types.Count(t => t == "92"));  // both steps

        Assert.Equal("1.0.0.9", solution.Descendants("Version").First().Value);
        Assert.Equal("SamplePlugins", solution.Descendants("UniqueName").First().Value);
    }

    [Fact]
    public void MissingMessageIdFailsWithTheMessageNamed()
    {
        var manifest = BuildManifest();
        var writer = new SolutionSourceWriter(Config(), new SdkMessageCache());

        var exception = Assert.Throws<ToolException>(
            () => writer.Write(manifest, Path.Combine(_root, "solution-src"), "1.0.0.9"));

        Assert.Contains("Update", exception.Message);
        Assert.Contains("dv messages pull", exception.Message);
    }

    /// <summary>
    /// pac is a hard prerequisite for building packages at all, so its absence is a real failure
    /// rather than something to quietly pass over.
    /// </summary>
    private static void RequirePac()
    {
        try
        {
            SolutionPacker.LocatePac();
        }
        catch (ToolException ex)
        {
            Assert.Fail(
                "These tests exercise the real solution packager, which needs the Power Platform " +
                "CLI. Install it from https://aka.ms/PowerPlatformCLI. " + ex.Message);
        }
    }

    private static XElement ReadXml(ZipArchive zip, string entryName)
    {
        using var stream = zip.GetEntry(entryName)!.Open();
        using var reader = new StreamReader(stream);
        return XDocument.Parse(reader.ReadToEnd()).Root!;
    }

    private static SolutionConfig Config() =>
        new()
        {
            Publisher = new SolutionConfig.PublisherSection
            {
                UniqueName = "sample",
                FriendlyName = "Sample",
                Prefix = "sample",
                OptionValuePrefix = 55973,
            },
            Solution = new SolutionConfig.SolutionSection
            {
                UniqueName = "SamplePlugins",
                FriendlyName = "Sample Plugins",
                Version = "1.0.0.0",
            },
        };

    private static SdkMessageCache MessageCache() =>
        new()
        {
            Messages = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
            {
                ["Update"] = Guid.Parse("20bebb1b-ea3e-db11-86a7-000a3a5473e8"),
                ["Create"] = Guid.Parse("9ebdbb1b-ea3e-db11-86a7-000a3a5473e8"),
            },
        };

    private PluginManifest BuildManifest()
    {
        // Any file will do - the writer only copies the bytes into the package.
        var assemblyPath = typeof(SolutionPackagingTests).Assembly.Location;

        var update = new ManifestStep
        {
            Id = Guid.Parse("11111111-1111-5111-8111-111111111111"),
            TypeName = "Sample.Plugins.Thing",
            Name = "Sample.Plugins.Thing: Update of contact",
            Message = "Update",
            PrimaryEntity = "contact",
            Stage = Stage.PostOperation,
            Order = 20,
            FilteringAttributes = "firstname,lastname",
            Images =
            [
                new ManifestImage
                {
                    Id = Guid.Parse("22222222-2222-5222-8222-222222222222"),
                    Name = "PreImage",
                    ImageType = ImageType.PreImage,
                    Attributes = "firstname,lastname",
                    EntityAlias = "PreImage",
                },
            ],
        };

        var create = new ManifestStep
        {
            Id = Guid.Parse("33333333-3333-5333-8333-333333333333"),
            TypeName = "Sample.Plugins.Thing",
            Name = "Sample.Plugins.Thing: Create of contact",
            Message = "Create",
            PrimaryEntity = "contact",
            Stage = Stage.PostOperation,
            Order = 10,
        };

        return new PluginManifest
        {
            SolutionUniqueName = "SamplePlugins",
            Assemblies =
            [
                new ManifestAssembly
                {
                    Name = "Sample.Plugins",
                    Id = Guid.Parse("44444444-4444-5444-8444-444444444444"),
                    Version = "1.0.0.0",
                    FullName = "Sample.Plugins, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                    IsolationMode = IsolationMode.Sandbox,
                    AssemblyPath = assemblyPath,
                    Types =
                    [
                        new ManifestType
                        {
                            TypeName = "Sample.Plugins.Thing",
                            Id = Guid.Parse("55555555-5555-5555-8555-555555555555"),
                            FriendlyName = "Sample.Plugins.Thing",
                            AssemblyQualifiedName =
                                "Sample.Plugins.Thing, Sample.Plugins, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                            Steps = [update, create],
                        },
                    ],
                },
            ],
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
