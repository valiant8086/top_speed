using FluentAssertions;
using TopSpeed.Server.Updates;
using Xunit;

namespace TopSpeed.Tests.Server
{
    /// <summary>
    /// The script a console server on Linux or macOS becomes while it updates itself.
    ///
    /// It cannot be run here, and by the time it runs there is no server left to report what went
    /// wrong with it — the process has already been replaced. So what is checked is everything
    /// that would be silently wrong: a path that loses its second half at a space, an updater
    /// that starts a second server behind this one's back, a wait with no end to it, or a final
    /// launch that forks instead of taking over the process id, which is the whole point.
    /// </summary>
    [Trait("Category", "Behavior")]
    public class UpdateHandoffBehavior
    {
        private static string Script(string root = "/home/me/ts server")
        {
            return UpdateHandoff.BuildScript(
                root,
                root + "/Updater",
                root + "/update.zip",
                "TopSpeed.Server",
                "Updater",
                root + "/TopSpeed.Server");
        }

        [Fact]
        public void ItEndsByBecomingTheServerRatherThanStartingOne()
        {
            // Without exec the new server is a child of the script, the script exits, the shell
            // takes the terminal back, and the server is left in the background unable to read
            // it. That is the entire bug this exists to fix.
            Script().Should().Contain("exec \"/home/me/ts server/TopSpeed.Server\"");
        }

        [Fact]
        public void TheUpdaterIsToldNotToStartAnything()
        {
            // Coming back is the script's job. An updater that also started one would leave two
            // servers wanting the same port.
            Script().Should().Contain("--no-restart");
        }

        [Fact]
        public void ItWaitsForTheUpdateToFinishAndGivesUpEventually()
        {
            var script = Script();

            script.Should().Contain("while [ -e \".updating\" ]");
            // An updater that died holding the marker must not leave a folder that can never
            // start a server again.
            script.Should().Contain("-lt 120");
        }

        [Fact]
        public void EveryPathIsQuoted()
        {
            // A folder with a space in its name is ordinary, and this same mistake already cost
            // us once on a Mac, where the error said nothing about spaces.
            var script = Script();

            script.Should().Contain("cd \"/home/me/ts server\"");
            script.Should().Contain("\"/home/me/ts server/Updater\"");
            script.Should().Contain("\"/home/me/ts server/update.zip\"");
        }

        [Fact]
        public void ItRunsFromTheFolderBeingUpdated()
        {
            // The wait watches for a marker by its bare name, so the working directory has to be
            // the folder it will appear in.
            Script().Should().StartWith("cd \"");
        }

        [Fact]
        public void CharactersTheShellWouldActOnAreEscaped()
        {
            // A folder name is not a command. Nobody sensible has a dollar sign in one, and the
            // one who does should get a working server rather than an expansion.
            var script = UpdateHandoff.BuildScript(
                "/home/me/$HOME `id`",
                "/home/me/$HOME `id`/Updater",
                "/home/me/$HOME `id`/update.zip",
                "TopSpeed.Server",
                "Updater",
                "/home/me/$HOME `id`/TopSpeed.Server");

            script.Should().NotContain("\"/home/me/$HOME");
            script.Should().Contain("\\$HOME");
            script.Should().Contain("\\`id\\`");
        }
    }
}
