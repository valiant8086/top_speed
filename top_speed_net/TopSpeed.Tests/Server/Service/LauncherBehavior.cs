using FluentAssertions;
using TopSpeed.Server.Service;
using Xunit;

namespace TopSpeed.Tests.Server.Service
{
    /// <summary>
    /// Starting the server without a terminal, on systems where pressing enter on a program with
    /// no extension does something different in every file manager. It cannot be tried here, so
    /// what is checked is the part that would be silently wrong: a script that assumes it was run
    /// from the folder it lives in.
    /// </summary>
    [Trait("Category", "Behavior")]
    public class LauncherBehavior
    {
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
