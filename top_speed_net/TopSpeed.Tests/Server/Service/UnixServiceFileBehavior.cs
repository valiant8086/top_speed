using FluentAssertions;
using TopSpeed.Server.Service;
using Xunit;

namespace TopSpeed.Tests.Server.Service
{
    /// <summary>
    /// The unit and job files cannot be tried on the machines this is developed on, and the
    /// things most worth getting right in them are the ones that fail silently rather than
    /// loudly: a missing argument produces a server that works and then misbehaves only when it
    /// updates itself. Checking the text is the only verification available, so it is done here.
    /// </summary>
    [Trait("Category", "Behavior")]
    public class UnixServiceFileBehavior
    {
        private const string Folder = "/opt/topspeed/tsServer";

        [Fact]
        public void The_systemd_unit_tells_the_server_it_is_being_managed()
        {
            // Without this the updater launches the server itself after an update, while systemd
            // also restarts the unit, leaving two servers and one of them orphaned.
            UnixServiceManager.BuildSystemdUnit(Folder).Should().Contain("--service");
        }

        [Fact]
        public void The_systemd_unit_starts_the_server_again_whatever_stopped_it()
        {
            // A server that stops to apply an update exits tidily, and only "always" covers
            // that. Anything narrower leaves an updated server switched off.
            UnixServiceManager.BuildSystemdUnit(Folder).Should().Contain("Restart=always");
        }

        [Fact]
        public void The_systemd_unit_waits_before_starting_again()
        {
            // Nothing but systemd can start the unit, since that needs root, so this wait is the
            // only thing stopping a restart from landing on a folder still being replaced.
            UnixServiceManager.BuildSystemdUnit(Folder).Should().Contain("RestartSec=120");
        }

        [Fact]
        public void The_systemd_unit_lets_the_updater_outlive_the_server()
        {
            // The updater is deliberately still running when the server exits, and the default
            // kill mode clears out the whole control group at that moment. Without this it is
            // killed partway through replacing the folder, which is the one failure that leaves
            // an installation in pieces rather than merely stopped.
            UnixServiceManager.BuildSystemdUnit(Folder).Should().Contain("KillMode=process");
        }

        [Fact]
        public void The_launchd_job_waits_before_starting_again()
        {
            // launchd's own default is about ten seconds, which during an update lands while the
            // folder is still being written. Same reasoning as RestartSec on systemd.
            UnixServiceManager.BuildLaunchdPlist(Folder).Should().Contain("<key>ThrottleInterval</key>");
        }

        [Fact]
        public void The_systemd_unit_waits_for_a_usable_network()
        {
            // "network" alone is satisfied before an address exists, which a server that binds
            // a port at startup would notice.
            UnixServiceManager.BuildSystemdUnit(Folder).Should().Contain("network-online.target");
        }

        [Fact]
        public void The_launchd_job_passes_the_argument_as_its_own_entry()
        {
            // Arguments are a list here, not a command line. Appending it to the path would
            // produce a job that looks right and cannot start.
            var plist = UnixServiceManager.BuildLaunchdPlist(Folder);

            plist.Should().Contain("<string>--service</string>");
            plist.Should().NotContain("TopSpeed.Server --service");
        }

        [Fact]
        public void The_launchd_job_starts_the_server_again_when_it_stops()
        {
            UnixServiceManager.BuildLaunchdPlist(Folder).Should().Contain("<key>KeepAlive</key>");
        }

        [Fact]
        public void Two_folders_produce_two_units_that_cannot_collide()
        {
            // Same reason as on Windows: somebody running two servers must be able to install
            // both, and nothing asks them to name either.
            UnixServiceManager.UnitNameFor("/opt/topspeed/a")
                .Should().NotBe(UnixServiceManager.UnitNameFor("/opt/topspeed/b"));
        }
    }
}
