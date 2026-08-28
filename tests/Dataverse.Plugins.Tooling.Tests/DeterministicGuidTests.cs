using Dataverse.Plugins.Tooling.Infrastructure;
using Xunit;

namespace Dataverse.Plugins.Tooling.Tests;

public class DeterministicGuidTests
{
    [Fact]
    public void SameInputAlwaysGivesSameId()
    {
        var first = DeterministicGuid.Create("SamplePlugins", "step", "Sample.Plugins.Thing", "Update");
        var second = DeterministicGuid.Create("SamplePlugins", "step", "Sample.Plugins.Thing", "Update");

        Assert.Equal(first, second);
    }

    /// <summary>
    /// Pins the exact value. If this changes, every component silently re-identifies itself and
    /// a deploy creates duplicates instead of updating what is already there.
    /// </summary>
    [Fact]
    public void IdIsStableAcrossBuilds()
    {
        var id = DeterministicGuid.Create("SamplePlugins", "assembly", "Sample.Plugins");

        Assert.Equal(Guid.Parse("ffe73aa4-a476-5016-bf9b-c02dab35bfc5"), id);
    }

    [Fact]
    public void DifferentInputGivesDifferentId()
    {
        var update = DeterministicGuid.Create("SamplePlugins", "step", "Thing", "Update");
        var create = DeterministicGuid.Create("SamplePlugins", "step", "Thing", "Create");

        Assert.NotEqual(update, create);
    }

    [Fact]
    public void CasingAndSurroundingWhitespaceDoNotChangeTheId()
    {
        var plain = DeterministicGuid.Create("SamplePlugins", "step", "Thing");
        var noisy = DeterministicGuid.Create("samplePLUGINS", " step ", "  thing");

        Assert.Equal(plain, noisy);
    }

    [Fact]
    public void IdIsAValidVersion5Uuid()
    {
        var id = DeterministicGuid.Create("SamplePlugins", "step", "Thing");
        var bytes = id.ToByteArray();

        // Guid.ToByteArray is little-endian for the first three fields, so the version nibble
        // lives in byte 7 rather than byte 6.
        Assert.Equal(0x50, bytes[7] & 0xF0);
        Assert.Equal(0x80, bytes[8] & 0xC0);
    }
}
