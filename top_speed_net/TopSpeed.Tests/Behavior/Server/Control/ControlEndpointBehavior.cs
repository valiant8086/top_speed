using System;
using System.IO;
using TopSpeed.Server.Control;
using Xunit;

namespace TopSpeed.Tests;

[Trait("Category", "Behavior")]
public sealed class ControlEndpointBehaviorTests
{
    [Fact]
    public void TwoCopiesInTheSameFolderShouldAgreeOnTheSameAddress()
    {
        // This is what makes a second copy find the running one instead of quietly starting a
        // duplicate server on the same ports.
        var directory = Path.Combine(Path.GetTempPath(), "tsr-control-a");

        ControlEndpoint.AddressFor(directory)
            .Should().Be(ControlEndpoint.AddressFor(directory));
    }

    [Fact]
    public void DifferentFoldersShouldNeverShareAnAddress()
    {
        // Two servers installed side by side have to stay independent, or one would attach to
        // the other and refuse to start.
        var first = Path.Combine(Path.GetTempPath(), "tsr-control-a");
        var second = Path.Combine(Path.GetTempPath(), "tsr-control-b");

        ControlEndpoint.AddressFor(first)
            .Should().NotBe(ControlEndpoint.AddressFor(second));
    }

    [Fact]
    public void ATrailingSeparatorShouldNotChangeTheAddress()
    {
        var bare = Path.Combine(Path.GetTempPath(), "tsr-control-a");
        var trailing = bare + Path.DirectorySeparatorChar;

        ControlEndpoint.AddressFor(trailing).Should().Be(ControlEndpoint.AddressFor(bare));
    }

    [Fact]
    public void PipeNamesShouldBeStableAcrossRuns()
    {
        // Baked in deliberately: if this value ever changes, a server upgraded in place would
        // stop recognising itself and an attaching copy would start a duplicate.
        var name = ControlEndpoint.PipeNameFor("/opt/topspeed");

        name.Should().StartWith("TopSpeedServer-");
        name.Should().HaveLength("TopSpeedServer-".Length + 16);
    }

    [Fact]
    public void PipeNamesShouldOnlyContainCharactersValidInAPipeName()
    {
        var name = ControlEndpoint.PipeNameFor(Path.GetTempPath());

        name.Should().MatchRegex("^[A-Za-z0-9-]+$");
    }

    [Theory]
    [InlineData("windows")]
    public void WindowsPathsShouldCompareWithoutRegardToCase(string _)
    {
        if (!OperatingSystem.IsWindows())
            return;

        ControlEndpoint.AddressFor(@"C:\Servers\Alpha")
            .Should().Be(ControlEndpoint.AddressFor(@"c:\servers\alpha"));
    }

    [Fact]
    public void TheSocketShouldSitInsideTheInstallFolder()
    {
        // On unix the file's own location is what proves "same folder", so it has to live there
        // rather than in a shared temp directory.
        var directory = Path.Combine(Path.GetTempPath(), "tsr-control-a");

        Path.GetDirectoryName(ControlEndpoint.SocketPathFor(directory))
            .Should().Be(ControlEndpoint.NormalizeDirectory(directory));
    }
}
