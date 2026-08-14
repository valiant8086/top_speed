using System;
using System.IO;
using FluentAssertions;
using TopSpeed.Server.Service;
using Xunit;

namespace TopSpeed.Tests.Server.Service
{
    /// <summary>
    /// Installing a service on systemd or launchd is done by a script this writes, because the
    /// alternative was a message somebody read aloud and retyped. What matters about that script
    /// is that it survives a folder name nobody thought about: the first Mac it was tried on had
    /// a space in the path, which turned a two argument copy into a three argument one and failed
    /// with an error that mentioned directories rather than spaces.
    /// </summary>
    [Trait("Category", "Behavior")]
    public class UnixInstallerScriptBehavior : IDisposable
    {
        private readonly string _folder;

        public UnixInstallerScriptBehavior()
        {
            // The space is the point of the test and not an accident of naming.
            _folder = Path.Combine(Path.GetTempPath(), "ts unix " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        private string InstallerScript()
        {
            var result = new UnixServiceManager().Install(_folder, startAutomatically: true);
            result.Succeeded.Should().BeTrue();

            var script = Directory.GetFiles(_folder, "install-service.*");
            script.Should().HaveCount(1);
            return File.ReadAllText(script[0]);
        }

        [Fact]
        public void EveryPathInTheInstallerIsQuoted()
        {
            var script = InstallerScript();

            // The folder is named with a space, so an unquoted path is a wrong command rather
            // than merely an untidy one.
            script.Should().Contain("\"" + _folder);
            script.Should().NotContain(" " + _folder + "/");
        }

        [Fact]
        public void TheInstallerRunsItselfWithSudoAndSaysWhatItIsDoing()
        {
            var script = InstallerScript();

            script.Should().StartWith("#!/bin/sh");
            script.Should().Contain("sudo ");
            // Both commands it runs are silent when they work, so without these it finishes
            // without a word and reads as nothing having happened.
            script.Should().Contain("echo ");
        }

        [Fact]
        public void TheServiceDescriptionIsWrittenBesideTheServer()
        {
            new UnixServiceManager().Install(_folder, startAutomatically: true).Succeeded.Should().BeTrue();

            var name = UnixServiceManager.UnitNameFor(_folder);
            var written = Directory.GetFiles(_folder);
            written.Should().Contain(f => Path.GetFileName(f).StartsWith(name, StringComparison.Ordinal));
        }

        [Fact]
        public void RemovingWritesAScriptThatClearsAwayWhatInstallingLeft()
        {
            new UnixServiceManager().Install(_folder, startAutomatically: true).Succeeded.Should().BeTrue();
            new UnixServiceManager().Uninstall(_folder).Succeeded.Should().BeTrue();

            var remover = Directory.GetFiles(_folder, "uninstall-service.*");
            remover.Should().HaveCount(1);

            var script = File.ReadAllText(remover[0]);
            script.Should().Contain("rm -f \"$0\"");
            script.Should().Contain("install-service.");
        }
    }
}
