using NEXMC.Services;
using Xunit;

namespace NEXMC.Tests;

public sealed class InstallInputValidatorTests
{
    [Theory]
    [InlineData("1.20.1")]
    [InlineData("Fabulously Optimized")]
    [InlineData("fabric-1.21.1")]
    public void ValidInstanceNamesAreAccepted(string value)
    {
        Assert.Equal(value, InstallInputValidator.ValidateInstanceName(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("pack..backup")]
    [InlineData("bad/name")]
    [InlineData("bad\\name")]
    [InlineData("trailing.")]
    [InlineData("CON")]
    [InlineData("lpt1.release")]
    public void InvalidInstanceNamesAreRejected(string value)
    {
        Assert.Throws<ArgumentException>(() => InstallInputValidator.ValidateInstanceName(value));
    }

    [Fact]
    public void RelativeTargetDirectoryIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            InstallInputValidator.ValidateTargetDirectory(".minecraft", "PCL2"));
    }

    [Fact]
    public void EmptyTargetDirectoryIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            InstallInputValidator.ValidateTargetDirectory(" ", "HMCL"));
    }

    [Fact]
    public void DuplicateTargetDirectoriesAreRejectedIgnoringCaseAndTrailingSeparator()
    {
        var root = Path.Combine(Path.GetTempPath(), "NEXMC-Target-Dedupe");
        var targets = new[]
        {
            new LauncherOption("pcl", "PCL2", LauncherKind.PCL2, root, null, false),
            new LauncherOption("hmcl", "HMCL", LauncherKind.HMCL, root + Path.DirectorySeparatorChar, null, false)
        };

        var exception = Assert.Throws<ArgumentException>(() => InstallInputValidator.ValidateTargets(targets));
        Assert.Contains("重复", exception.Message);
    }

    [Fact]
    public void DistinctTargetDirectoriesAreAccepted()
    {
        var root = Path.Combine(Path.GetTempPath(), "NEXMC-Target-Dedupe");
        var targets = new[]
        {
            new LauncherOption("pcl", "PCL2", LauncherKind.PCL2, Path.Combine(root, "pcl"), null, false),
            new LauncherOption("hmcl", "HMCL", LauncherKind.HMCL, Path.Combine(root, "hmcl"), null, false)
        };

        var directories = InstallInputValidator.ValidateTargets(targets);
        Assert.Equal(2, directories.Count);
        Assert.Equal(Path.GetFullPath(Path.Combine(root, "pcl")), directories[0]);
    }

    [Fact]
    public void EmptyTargetListIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            InstallInputValidator.ValidateTargets(Array.Empty<LauncherOption>()));
    }
}
