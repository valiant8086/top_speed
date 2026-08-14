using System.IO;
using FluentAssertions;
using TopSpeed.Server.Service;
using Xunit;

namespace TopSpeed.Tests.Server.Service
{
    /// <summary>
    /// The two ways to start the server without a terminal, on systems where pressing enter on a
    /// program with no extension does something different in every file manager. Neither can be
    /// tried here, so what is checked is the part that would be silently wrong: a desktop entry
    /// that starts a console program without a console, or a script that assumes it was run from
    /// the folder it lives in.
    /// </summary>
    [Trait("Category", "Behavior")]
    public class LauncherBehavior
    {
        [Fact]
        public void TheDesktopEntryAsksForATerminal()
        {
            var entry = Launchers.BuildDesktopEntry(Path.Combine(Path.GetTempPath(), "ts launcher"));

            entry.Should().StartWith("[Desktop Entry]");
            // Without this the server runs with nowhere to type shutdown, holding the port while
            // looking as though nothing happened.
            entry.Should().Contain("Terminal=true");
            entry.Should().Contain("Type=Application");
        }

        [Fact]
        public void TheDesktopEntryNamesTheServerWhereItActuallyIs()
        {
            var folder = Path.Combine(Path.GetTempPath(), "ts launcher");
            var entry = Launchers.BuildDesktopEntry(folder);

            // Quoted, because a folder with a space in its name is ordinary and an unquoted Exec
            // would be read as a program name followed by arguments.
            entry.Should().Contain("Exec=\"");
            entry.Should().Contain("Path=" + ServiceIdentity.DisplayPath(folder));
        }

        [Fact]
        public void TheScriptFindsItsOwnFolderRatherThanTrustingTheCurrentOne()
        {
            var script = Launchers.BuildScript();

            script.Should().StartWith("#!/bin/sh");
            // A file manager may run this from anywhere, and usually does.
            script.Should().Contain("cd \"$(dirname \"$0\")\"");
            script.Should().Contain("exec ./TopSpeed.Server");
        }
    }
}
