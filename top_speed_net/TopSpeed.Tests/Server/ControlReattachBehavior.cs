using FluentAssertions;
using TopSpeed.Server.Control;
using TopSpeed.Server.Updates;
using Xunit;

namespace TopSpeed.Tests.Server
{
    /// <summary>
    /// The script an attached window becomes when its server leaves to update. It cannot be run
    /// here, so what is checked is what would be silently wrong: that it is a script at all
    /// rather than the window waiting as itself, which died with a bus error when the updater
    /// rewrote the files the window was running from; that it waits for the right things, in the
    /// right order, and not forever; and that it ends by becoming the updated program, attached.
    /// </summary>
    [Trait("Category", "Behavior")]
    public class ControlReattachBehavior
    {
        private static string Script() => ControlReattach.BuildScript("/srv/top speed", "/srv/top speed/TopSpeed.Server");

        [Fact]
        public void ItWorksFromTheServerFolderAndQuotesIt()
        {
            Script().Should().StartWith("cd \"/srv/top speed\" || exit 1\n");
        }

        [Fact]
        public void ItWaitsForTheUpdateToFinishAndGivesUpEventually()
        {
            var script = Script();
            script.Should().Contain("while [ -e \"" + UpdateMarker.FileName + "\" ]");
            script.Should().Contain("-lt " + (int)UpdateMarker.AssumeAbandonedAfter.TotalSeconds + " ]");
        }

        [Fact]
        public void ItWaitsForAnEndpointNewerThanTheMomentItLostItsServer()
        {
            // A killed server leaves its endpoint file behind. Counting that one would attach to
            // nothing and say the server is not running while a new one is still starting. The
            // stamp is taken before any waiting: taken once the marker had cleared, it raced the
            // server, which could bind first and then never count, and the window sat out the
            // whole bound before attaching.
            var script = Script();
            var stamp = script.IndexOf("_since=$(mktemp)", System.StringComparison.Ordinal);
            var marker = script.IndexOf(UpdateMarker.FileName, System.StringComparison.Ordinal);
            var endpoint = script.IndexOf("\"" + ControlEndpoint.SocketFileName + "\" -nt \"$_since\"", System.StringComparison.Ordinal);

            stamp.Should().BePositive();
            marker.Should().BeGreaterThan(stamp, "the stamp comes before the wait for the update");
            endpoint.Should().BeGreaterThan(marker, "and the endpoint is waited for once the update is over");
        }

        [Fact]
        public void ItEndsByBecomingTheUpdatedProgramAttached()
        {
            Script().Should().EndWith("exec \"/srv/top speed/TopSpeed.Server\" --attach\n");
        }
    }
}
