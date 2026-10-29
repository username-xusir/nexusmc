using NEXMC.Services;
using Xunit;

namespace NEXMC.Tests;

public sealed class MinecraftVersionClassifierTests
{
    [Theory]
    [InlineData("1.21.4", "release", MinecraftVersionClassifier.Release)]
    [InlineData("26.3", "release", MinecraftVersionClassifier.Release)]
    [InlineData("25w14a", "snapshot", MinecraftVersionClassifier.Snapshot)]
    [InlineData("1.0", "old_alpha", MinecraftVersionClassifier.Legacy)]
    [InlineData("b1.7.3", "old_beta", MinecraftVersionClassifier.Legacy)]
    public void RegularVersionsAreClassified(string id, string type, string expected)
    {
        Assert.Equal(expected, MinecraftVersionClassifier.Classify(id, type));
    }

    [Theory]
    [InlineData("15w14a")]
    [InlineData("1.RV-Pre1")]
    [InlineData("3D Shareware v1.34")]
    [InlineData("20w14infinite")]
    [InlineData("22w13oneBlockAtATime")]
    [InlineData("23w13a_or_b")]
    [InlineData("24w14potato")]
    [InlineData("25w14craftmine")]
    public void AprilFoolsVersionsAreClassified(string id)
    {
        Assert.Equal(MinecraftVersionClassifier.AprilFools, MinecraftVersionClassifier.Classify(id, "snapshot"));
    }
}
