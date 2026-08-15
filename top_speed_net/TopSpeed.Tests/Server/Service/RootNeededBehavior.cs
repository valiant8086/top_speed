using System.IO;
using FluentAssertions;
using TopSpeed.Server.Service;
using Xunit;

namespace TopSpeed.Tests.Server.Service
{
    /// <summary>
    /// The one thing the server says on Linux and macOS when it is asked to touch the service
    /// without root. It replaced a route that wrote scripts, so it is now the whole of what
    /// somebody gets: if it names the wrong command they have nothing else to go on.
    /// </summary>
    [Trait("Category", "Behavior")]
    public class RootNeededBehavior
    {
        private static readonly string Folder = Path.Combine(Path.GetTempPath(), "ts root needed");

        [Fact]
        public void ItNamesTheCommandForTheActionThatWasAskedFor()
        {
            // One message serves five actions, so the flag is the only part that distinguishes
            // them. Getting it wrong would tell somebody asking to stop the service to install it.
            ServiceCommands.RootNeeded(Folder, ServiceAction.Install).Should().Contain("--install-service");
            ServiceCommands.RootNeeded(Folder, ServiceAction.Uninstall).Should().Contain("--uninstall-service");
            ServiceCommands.RootNeeded(Folder, ServiceAction.Start).Should().Contain("--start-service");
            ServiceCommands.RootNeeded(Folder, ServiceAction.Stop).Should().Contain("--stop-service");
            ServiceCommands.RootNeeded(Folder, ServiceAction.Restart).Should().Contain("--restart-service");
        }

        [Fact]
        public void TheCommandIsAbsoluteAndQuotedSoItCanBePastedFromAnywhere()
        {
            var message = ServiceCommands.RootNeeded(Folder, ServiceAction.Install);

            // Relative would only work from the server's own folder, which is not where somebody
            // reading this necessarily is. Quoted because a folder name with a space in it is
            // ordinary and produced a command that failed while looking correct.
            message.Should().Contain("sudo \"" + ServiceIdentity.ExecutablePathFor(Folder) + "\"");
        }

        [Theory]
        [InlineData("install", "--install-service")]
        [InlineData("uninstall", "--uninstall-service")]
        [InlineData("start", "--start-service")]
        [InlineData("stop", "--stop-service")]
        [InlineData("restart", "--restart-service")]
        public void TheAnswerNamesTheVerbThatWasTyped(string verb, string flag)
        {
            // The service command took this route before the verb had been read, so every one of
            // them was answered with the install command. It reads as correct unless the verb is
            // compared against the flag, which is exactly why it needs checking here.
            ServiceConsole.UnprivilegedAnswer(verb, Folder).Should().Contain(flag);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("frobnicate")]
        public void NoVerbOrAnUnknownOneFallsBackToInstalling(string verb)
        {
            // Where the menu would have opened. Install is what somebody opening it almost always
            // wants, and an unknown word is treated as none, as it is everywhere else.
            ServiceConsole.UnprivilegedAnswer(verb, Folder).Should().Contain("--install-service");
        }

        [Fact]
        public void ItSaysNotToGiveTheServerItselfSudo()
        {
            // The half that matters most, since running the whole server as root is the mistake
            // that costs something: it is silent at the time and surfaces later as an update that
            // cannot replace files the folder's owner no longer owns.
            ServiceCommands.RootNeeded(Folder, ServiceAction.Install)
                .Should().Contain("should not be given it");
        }
    }
}
