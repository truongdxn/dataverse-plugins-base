using Dataverse.Plugins.Tooling.Model;
using Dataverse.Plugins.Tooling.Scanning;
using Xunit;

namespace Dataverse.Plugins.Tooling.Tests;

public class ManifestValidatorTests
{
    [Fact]
    public void PreImageOnCreateIsRejected()
    {
        var result = Validate(Step(
            "Create",
            images: [Image("PreImage", ImageType.PreImage)]));

        Assert.Contains(result.Errors, e => e.Contains("Create has no pre-image"));
    }

    [Fact]
    public void PostImageOnDeleteIsRejected()
    {
        var result = Validate(Step(
            "Delete",
            images: [Image("PostImage", ImageType.PostImage)]));

        Assert.Contains(result.Errors, e => e.Contains("Delete has no post-image"));
    }

    [Fact]
    public void PostImageBeforeTheOperationIsRejected()
    {
        var result = Validate(Step(
            "Update",
            stage: Stage.PreOperation,
            images: [Image("PostImage", ImageType.PostImage)]));

        Assert.Contains(result.Errors, e => e.Contains("only available in PostOperation"));
    }

    [Fact]
    public void AsynchronousStepOutsidePostOperationIsRejected()
    {
        var result = Validate(Step("Update", stage: Stage.PreOperation, mode: ExecutionMode.Asynchronous));

        Assert.Contains(result.Errors, e => e.Contains("asynchronous steps must be registered in PostOperation"));
    }

    [Fact]
    public void DuplicateImageNameOnOneStepIsRejected()
    {
        var result = Validate(Step(
            "Update",
            images: [Image("PreImage", ImageType.PreImage), Image("PreImage", ImageType.PreImage)]));

        Assert.Contains(result.Errors, e => e.Contains("declared more than once"));
    }

    [Fact]
    public void UpdateWithoutFilteringAttributesWarnsButDoesNotFail()
    {
        var result = Validate(Step("Update"));

        Assert.False(result.HasErrors);
        Assert.Contains(result.Warnings, w => w.Contains("fires on every column change"));
    }

    [Fact]
    public void SecretLookingUnsecureConfigurationWarns()
    {
        var step = Step("Update", filtering: "name");
        step.UnsecureConfiguration = "ApiKey=abc123";

        var result = Validate(step);

        Assert.Contains(result.Warnings, w => w.Contains("looks like it contains a secret"));
    }

    [Fact]
    public void ValidStepPassesCleanly()
    {
        var result = Validate(Step(
            "Update",
            filtering: "firstname",
            images: [Image("PreImage", ImageType.PreImage, "firstname")]));

        Assert.False(result.HasErrors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void ImageNamingAStepThatDoesNotExistFailsAndListsTheRealStepNames()
    {
        var step = Step("Update", filtering: "firstname");

        var type = new ManifestType
        {
            TypeName = step.TypeName,
            Steps = [step],
            UnboundImages = [new UnboundImage { ImageName = "PreImage", StepName = "Mispelled" }],
        };

        var manifest = new PluginManifest
        {
            Assemblies = [new ManifestAssembly { Name = "Sample.Plugins", Types = [type] }],
        };

        var result = ManifestValidator.Validate(manifest);

        var error = Assert.Single(result.Errors);
        Assert.Contains("Mispelled", error);
        Assert.Contains(step.Name, error);
    }

    /// <summary>
    /// The tooling mirrors the abstractions' enums across the net462/net8.0 boundary. If a value
    /// drifts, steps register into the wrong stage - which is silent and very hard to spot.
    /// </summary>
    [Theory]
    [InlineData(Stage.PreValidation, 10)]
    [InlineData(Stage.PreOperation, 20)]
    [InlineData(Stage.PostOperation, 40)]
    public void StageValuesMatchDataverse(Stage stage, int expected) => Assert.Equal(expected, (int)stage);

    [Theory]
    [InlineData(ImageType.PreImage, 0)]
    [InlineData(ImageType.PostImage, 1)]
    [InlineData(ImageType.Both, 2)]
    public void ImageTypeValuesMatchDataverse(ImageType type, int expected) => Assert.Equal(expected, (int)type);

    [Theory]
    [InlineData(IsolationMode.None, 1)]
    [InlineData(IsolationMode.Sandbox, 2)]
    public void IsolationModeValuesMatchDataverse(IsolationMode mode, int expected) =>
        Assert.Equal(expected, (int)mode);

    private static ManifestImage Image(string name, ImageType type, string attributes = "name") =>
        new() { Name = name, ImageType = type, Attributes = attributes };

    private static ManifestStep Step(
        string message,
        Stage stage = Stage.PostOperation,
        ExecutionMode mode = ExecutionMode.Synchronous,
        string filtering = null,
        List<ManifestImage> images = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TypeName = "Sample.Plugins.Thing",
            Name = $"Sample.Plugins.Thing: {message} of contact",
            Message = message,
            PrimaryEntity = "contact",
            Stage = stage,
            Mode = mode,
            FilteringAttributes = filtering,
            Images = images ?? [],
        };

    private static ValidationResult Validate(ManifestStep step)
    {
        var manifest = new PluginManifest
        {
            Assemblies =
            [
                new ManifestAssembly
                {
                    Name = "Sample.Plugins",
                    Types = [new ManifestType { TypeName = step.TypeName, Steps = [step] }],
                },
            ],
        };

        return ManifestValidator.Validate(manifest);
    }
}
