using Dataverse.Plugins.Tooling.Configuration;
using Dataverse.Plugins.Tooling.Infrastructure;
using Dataverse.Plugins.Tooling.Model;
using Dataverse.Plugins.Tooling.Scanning;
using Xunit;

namespace Dataverse.Plugins.Tooling.Tests;

public class ManifestBuilderTests
{
    private const string TypeName = "Sample.Plugins.ContactSync";

    [Fact]
    public void SingleStepKeepsItsValuesAndGetsAGeneratedName()
    {
        var manifest = Build([Step("Update", "contact", order: 5, filtering: "firstname")]);

        var step = Assert.Single(manifest.AllSteps());
        Assert.Equal(5, step.Order);
        Assert.Equal("firstname", step.FilteringAttributes);
        Assert.Equal($"{TypeName}: Update of contact", step.Name);
    }

    [Fact]
    public void OneClassCanDeclareSeveralStepsWithDistinctIds()
    {
        var manifest = Build(
        [
            Step("Create", "contact", name: "Contact create"),
            Step("Update", "contact", name: "Contact update"),
        ]);

        var steps = manifest.AllSteps().ToList();

        Assert.Equal(2, steps.Count);
        Assert.Equal(2, steps.Select(s => s.Id).Distinct().Count());
        Assert.All(steps, step => Assert.NotEqual(Guid.Empty, step.Id));
    }

    [Fact]
    public void TwoStepsForTheSameMessageAndTableWithoutNamesIsRejected()
    {
        // They would be indistinguishable, and would collapse onto one deterministic id.
        var exception = Assert.Throws<ToolException>(() => Build(
        [
            Step("Update", "contact"),
            Step("Update", "contact"),
        ]));

        Assert.Contains("explicit Name", exception.Message);
    }

    [Fact]
    public void StepShorthandImagesLandOnTheirOwnStepOnly()
    {
        var create = Step("Create", "contact", name: "Contact create");
        create.Images.Add(Image("PostImage", ImageType.PostImage));

        var update = Step("Update", "contact", name: "Contact update");
        update.Images.Add(Image("PreImage", ImageType.PreImage));

        var manifest = Build([create, update]);

        var built = manifest.AllSteps().ToDictionary(step => step.Name);

        Assert.Equal("PostImage", Assert.Single(built["Contact create"].Images).Name);
        Assert.Equal("PreImage", Assert.Single(built["Contact update"].Images).Name);
    }

    [Fact]
    public void TheSameImageNameOnTwoStepsGetsTwoDifferentIds()
    {
        // Image ids derive from the step, which is what makes per-step images safe to name alike.
        var create = Step("Create", "contact", name: "Contact create");
        create.Images.Add(Image("PreImage", ImageType.PostImage));

        var update = Step("Update", "contact", name: "Contact update");
        update.Images.Add(Image("PreImage", ImageType.PreImage));

        var manifest = Build([create, update]);

        var ids = manifest.AllSteps().SelectMany(step => step.Images).Select(image => image.Id).ToList();

        Assert.Equal(2, ids.Count);
        Assert.Equal(2, ids.Distinct().Count());
    }

    [Fact]
    public void ClassLevelImageWithoutAStepNameAttachesToEveryStep()
    {
        var manifest = Build(
            [Step("Update", "contact", name: "A"), Step("Update", "account", name: "B")],
            classImages: [Image("PreImage", ImageType.PreImage)]);

        Assert.All(manifest.AllSteps(), step => Assert.Single(step.Images));
    }

    [Fact]
    public void ClassLevelImageWithAStepNameAttachesOnlyToThatStep()
    {
        var manifest = Build(
            [Step("Update", "contact", name: "Named"), Step("Update", "account", name: "Other")],
            classImages: [Image("PreImage", ImageType.PreImage, stepName: "Named")]);

        var byName = manifest.AllSteps().ToDictionary(step => step.Name);

        Assert.Single(byName["Named"].Images);
        Assert.Empty(byName["Other"].Images);
    }

    [Fact]
    public void ImageNamingAStepThatDoesNotExistIsRecordedForTheValidator()
    {
        // Dropping it silently would produce a step whose image is simply missing at runtime.
        var manifest = Build(
            [Step("Update", "contact", name: "Named")],
            classImages: [Image("PreImage", ImageType.PreImage, stepName: "Mispelled")]);

        var type = Assert.Single(manifest.AllTypes());
        var unbound = Assert.Single(type.UnboundImages);

        Assert.Equal("PreImage", unbound.ImageName);
        Assert.Equal("Mispelled", unbound.StepName);
    }

    [Fact]
    public void ImageAliasAndMessagePropertyGetSensibleDefaults()
    {
        var step = Step("Update", "contact");
        step.Images.Add(Image("PreImage", ImageType.PreImage));

        var manifest = Build([step]);
        var image = Assert.Single(manifest.AllSteps().Single().Images);

        Assert.Equal("PreImage", image.EntityAlias);
        Assert.Equal("Target", image.MessagePropertyName);
        Assert.NotEqual(Guid.Empty, image.Id);
    }

    private static ManifestImage Image(string name, ImageType type, string stepName = null) =>
        new() { Name = name, ImageType = type, Attributes = "firstname", StepName = stepName };

    private static ManifestStep Step(
        string message,
        string entity,
        int order = 1,
        string filtering = null,
        string name = null) =>
        new()
        {
            TypeName = TypeName,
            Message = message,
            PrimaryEntity = entity,
            Order = order,
            FilteringAttributes = filtering,
            Name = name,
            Stage = Stage.PostOperation,
        };

    private static PluginManifest Build(List<ManifestStep> steps, List<ManifestImage> classImages = null)
    {
        var config = new SolutionConfig
        {
            Publisher = new SolutionConfig.PublisherSection { UniqueName = "sample", Prefix = "sample" },
            Solution = new SolutionConfig.SolutionSection { UniqueName = "SamplePlugins", Version = "1.0.0.0" },
        };

        var scanned = new ScannedAssembly
        {
            Name = "Sample.Plugins",
            Version = "1.0.0.0",
            Types = [new ScannedType { TypeName = TypeName, Steps = steps, Images = classImages ?? [] }],
        };

        var project = new PluginProject
        {
            ProjectPath = Path.Combine("src", "Sample.Plugins", "Sample.Plugins.csproj"),
            AssemblyName = "Sample.Plugins",
            TargetPath = "Sample.Plugins.dll",
            IsolationMode = IsolationMode.Sandbox,
        };

        return new ManifestBuilder(config).Build([new AssemblyInput(project, scanned)]);
    }
}
