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
        public void The_systemd_unit_comes_back_quickly()
        {
            // This used to be two minutes, spent guessing at how long an update takes. The wait
            // for the update is now its own step, so the pause after an ordinary stop is only
            // what it takes to start again.
            UnixServiceManager.BuildSystemdUnit(Folder).Should().Contain("RestartSec=2");
        }

        [Fact]
        public void The_systemd_unit_waits_for_an_update_to_finish_writing()
        {
            // The reason the pause above can be short: this waits for the update itself rather
            // than for a length of time somebody guessed, so a restart cannot land on a folder
            // that is half one version and half the next.
            var unit = UnixServiceManager.BuildSystemdUnit(Folder);

            unit.Should().Contain("ExecStartPre=");
            unit.Should().Contain(".updating");
        }

        [Fact]
        public void The_systemd_wait_gives_up_rather_than_waiting_forever()
        {
            // An updater that dies partway leaves the file behind, and an updater older than the
            // file never learns to remove it. Unbounded, either of those would mean a server that
            // never starts again, which is far worse than the minute this costs.
            UnixServiceManager.BuildSystemdUnit(Folder).Should().Contain("-lt 60");
        }

        [Fact]
        public void The_systemd_wait_escapes_its_dollars_for_systemd()
        {
            // systemd expands "$i" itself before the shell ever sees it, so a single dollar here
            // silently becomes an empty string and the loop compares nothing against 60. Doubling
            // is what passes one through, and the failure it prevents looks like a unit that
            // parses cleanly and hangs.
            UnixServiceManager.BuildSystemdUnit(Folder).Should().Contain("$$i");
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
        public void The_launchd_job_states_its_pause_rather_than_stretching_it()
        {
            // This was raised to two minutes to cover an update being written, which the wait in
            // front of the server now covers for exactly as long as it takes. What is left is
            // launchd's own default, written down rather than assumed.
            var plist = UnixServiceManager.BuildLaunchdPlist(Folder);

            plist.Should().Contain("<key>ThrottleInterval</key>");
            plist.Should().NotContain("<integer>120</integer>");
        }

        [Fact]
        public void The_systemd_unit_waits_for_a_usable_network()
        {
            // "network" alone is satisfied before an address exists, which a server that binds
            // a port at startup would notice.
            UnixServiceManager.BuildSystemdUnit(Folder).Should().Contain("network-online.target");
        }

        [Fact]
        public void The_launchd_job_waits_and_then_hands_over_to_the_server()
        {
            // launchd runs one program, so the wait systemd gets its own step for has to go in
            // front of the server here. "exec" is what makes the server replace the shell rather
            // than run under it, which is what keeps the server the process launchd watches and
            // restarts; without it launchd is watching a shell that has already done its job.
            var plist = UnixServiceManager.BuildLaunchdPlist(Folder);

            plist.Should().Contain(".updating");
            plist.Should().Contain("exec ");
            plist.Should().Contain("--service");
        }

        [Fact]
        public void The_launchd_job_quotes_the_server_path()
        {
            // A command line now, not a list, so a folder with a space in it would otherwise
            // become two arguments and the job would fail to start on exactly the machines whose
            // owners are most likely to have one.
            var plist = UnixServiceManager.BuildLaunchdPlist("/Users/me/Top Speed/server");

            plist.Should().Contain("exec \"");
            plist.Should().Contain("\" --service");
        }

        [Fact]
        public void The_launchd_job_leaves_its_dollars_alone()
        {
            // The doubling systemd needs is systemd's own. launchd hands the string to the shell
            // untouched, so a doubled dollar copied across from the unit would reach the shell
            // as a literal and the loop would never count.
            UnixServiceManager.BuildLaunchdPlist(Folder).Should().NotContain("$$");
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
