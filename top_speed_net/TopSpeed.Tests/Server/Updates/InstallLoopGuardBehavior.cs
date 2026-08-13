using System;
using System.Globalization;
using System.IO;
using FluentAssertions;
using TopSpeed.Server.Config;
using TopSpeed.Server.Logging;
using TopSpeed.Server.Network;
using TopSpeed.Server.Updates;
using Xunit;

namespace TopSpeed.Tests.Server.Updates
{
    /// <summary>
    /// An install ends by ending the process, so everything the scheduler knew about it is gone by
    /// the time anyone could act on it. These cover the one thing written down instead, and the
    /// failure it exists for: a build whose name says one version and whose contents report an
    /// older one, which a server would otherwise fetch and install again every day for good.
    /// </summary>
    [Trait("Category", "Behavior")]
    public class InstallLoopGuardBehavior : IDisposable
    {
        private readonly string _folder;

        public InstallLoopGuardBehavior()
        {
            _folder = Path.Combine(Path.GetTempPath(), "ts-loop-" + Guid.NewGuid().ToString("N"));
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

        private static string VersionAbove(ServerVersion version, int steps = 1)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{version.Year}.{version.Month}.{version.Day}.{version.Revision + steps}");
        }

        /// <summary>Writes the record by hand so its age can be chosen.</summary>
        private void RecordHandoff(string versionText, TimeSpan ago)
        {
            File.WriteAllText(
                UpdateInstallRecord.PathIn(_folder),
                versionText + "\n" +
                DateTime.UtcNow.Subtract(ago).ToString("o", CultureInfo.InvariantCulture));
        }

        private ServerUpdateScheduler NewScheduler(StartupUpdateMode mode)
        {
            var logger = new Logger(LogLevel.None, null, writeToConsole: false);
            return new ServerUpdateScheduler(
                new RaceServer(new RaceServerConfig(), logger),
                new ServerUpdateRunner(ServerUpdateConfig.Default, logger),
                logger,
                mode,
                () => { },
                _folder);
        }

        private static ServerUpdateCheckResult Available(string version)
        {
            ServerVersion.TryParse(version, out var parsed);
            return new ServerUpdateCheckResult
            {
                Outcome = ServerUpdateCheckOutcome.UpdateAvailable,
                VersionText = version,
                Update = new ServerUpdateInfo { VersionText = version, Version = parsed }
            };
        }

        [Fact]
        public void A_version_that_was_installed_and_did_not_take_is_not_installed_again()
        {
            // The loop, in one test. The record says this version went to the updater; the server
            // is running something older, so it plainly did not arrive. Installing it again would
            // download the same build, restart, and land here again.
            var stuckAt = VersionAbove(ServerUpdateConfig.CurrentVersion);
            RecordHandoff(stuckAt, TimeSpan.FromHours(2));
            var scheduler = NewScheduler(StartupUpdateMode.Auto);

            scheduler.ApplyCheckResult(Available(stuckAt), interactive: false);

            scheduler.GetStatus().State.Should().Be(UpdateSchedulerState.Idle);
        }

        [Fact]
        public void A_later_version_still_installs_by_itself()
        {
            // What stops this being an off switch. One version is known not to work; nothing is
            // known about the next one, and auto was set to take it.
            var stuckAt = VersionAbove(ServerUpdateConfig.CurrentVersion);
            var fixedBuild = VersionAbove(ServerUpdateConfig.CurrentVersion, 2);
            RecordHandoff(stuckAt, TimeSpan.FromHours(2));
            var scheduler = NewScheduler(StartupUpdateMode.Auto);

            scheduler.ApplyCheckResult(Available(fixedBuild), interactive: false);

            var status = scheduler.GetStatus();
            status.State.Should().Be(UpdateSchedulerState.PendingInstall);
            status.VersionText.Should().Be(fixedBuild);
        }

        [Fact]
        public void Coming_back_from_an_install_does_not_check_again_straight_away()
        {
            // The server asked what the newest version was moments ago, on its way into the
            // install it just came back from. Asking again on arrival is one answer fetched twice.
            RecordHandoff(ServerUpdateConfig.CurrentVersion.ToMachineString(), TimeSpan.FromSeconds(20));

            var scheduler = NewScheduler(StartupUpdateMode.Auto);

            scheduler.GetStatus().TimeUntilNextAttempt.Should().BeGreaterThan(TimeSpan.FromHours(1));
        }

        [Fact]
        public void A_restart_long_after_an_install_checks_as_usual()
        {
            // The cooldown is about the moments around an install, not about restarts. Somebody
            // rebooting an hour later gets the check they would have got with no record at all.
            RecordHandoff(ServerUpdateConfig.CurrentVersion.ToMachineString(), TimeSpan.FromHours(1));

            var scheduler = NewScheduler(StartupUpdateMode.Auto);

            scheduler.GetStatus().TimeUntilNextAttempt.Should().Be(TimeSpan.Zero);
        }

        [Fact]
        public void A_record_the_server_has_caught_up_with_is_thrown_away()
        {
            // The install worked, so the record has nothing left to say and should not sit in the
            // folder implying otherwise.
            RecordHandoff(ServerUpdateConfig.CurrentVersion.ToMachineString(), TimeSpan.FromHours(2));

            NewScheduler(StartupUpdateMode.Auto);

            File.Exists(UpdateInstallRecord.PathIn(_folder)).Should().BeFalse();
        }
    }
}
