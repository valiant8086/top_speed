using FluentAssertions;
using TopSpeed.Server.Logging;
using TopSpeed.Server.Network;
using TopSpeed.Server.Config;
using TopSpeed.Server.Updates;
using Xunit;

namespace TopSpeed.Tests.Server.Updates
{
    /// <summary>
    /// An install approved while players are connected waits for them to leave, and the daily
    /// check keeps running while it waits. What that check does to the approval is the whole of
    /// what these cover: it is the one place where an update somebody asked for can quietly stop
    /// being one, hours after they asked and with nobody watching.
    /// </summary>
    [Trait("Category", "Behavior")]
    public class PendingInstallBehavior
    {
        private static ServerUpdateScheduler NewScheduler(StartupUpdateMode mode)
        {
            var logger = new Logger(LogLevel.None, null, writeToConsole: false);
            return new ServerUpdateScheduler(
                new RaceServer(new RaceServerConfig(), logger),
                new ServerUpdateRunner(ServerUpdateConfig.Default, logger),
                logger,
                mode,
                () => { });
        }

        private static ServerUpdateCheckResult Available(string version)
        {
            return new ServerUpdateCheckResult
            {
                Outcome = ServerUpdateCheckOutcome.UpdateAvailable,
                VersionText = version,
                Update = new ServerUpdateInfo
                {
                    VersionText = version,
                    DownloadUrl = "https://example.invalid/" + version + ".zip",
                    AssetSizeBytes = 1024
                }
            };
        }

        /// <summary>Offers a version and approves it, which is what typing update twice does.</summary>
        private static ServerUpdateScheduler WithApprovedInstall(StartupUpdateMode mode, string version)
        {
            var scheduler = NewScheduler(mode);
            scheduler.ApplyCheckResult(Available(version), interactive: true);
            scheduler.TryApproveOffered(out _).Should().BeTrue();
            scheduler.GetStatus().State.Should().Be(UpdateSchedulerState.PendingInstall);
            return scheduler;
        }

        [Fact]
        public void A_check_finding_the_same_version_leaves_the_approval_standing()
        {
            // The case that has to keep working: nothing changed, so the install somebody approved
            // is still the install they approved, and it goes in when the last player leaves.
            var scheduler = WithApprovedInstall(StartupUpdateMode.Notify, "2026.8.9.4");

            scheduler.ApplyCheckResult(Available("2026.8.9.4"), interactive: false);

            var status = scheduler.GetStatus();
            status.State.Should().Be(UpdateSchedulerState.PendingInstall);
            status.VersionText.Should().Be("2026.8.9.4");
        }

        [Fact]
        public void Notify_drops_an_approval_rather_than_installing_a_version_nobody_approved()
        {
            // What was approved was a version whose changes somebody read. A different one is not
            // covered by that, so it is dropped and the new one has to be approved in its own
            // right rather than going on in its place. Off takes this same branch.
            var scheduler = WithApprovedInstall(StartupUpdateMode.Notify, "2026.8.9.4");

            scheduler.ApplyCheckResult(Available("2026.8.9.5"), interactive: false);

            scheduler.GetStatus().State.Should().Be(UpdateSchedulerState.Idle);
        }

        [Fact]
        public void Auto_moves_the_approval_on_to_the_newer_version()
        {
            // Auto was set to take new versions by itself, so a newer one is what it was asked
            // for. It stays armed rather than dropping to nothing and waiting to be asked again.
            var scheduler = WithApprovedInstall(StartupUpdateMode.Auto, "2026.8.9.4");

            scheduler.ApplyCheckResult(Available("2026.8.9.5"), interactive: false);

            var status = scheduler.GetStatus();
            status.State.Should().Be(UpdateSchedulerState.PendingInstall);
            status.VersionText.Should().Be("2026.8.9.5");
        }

        [Fact]
        public void An_offer_nobody_approved_is_made_again_rather_than_kept()
        {
            // The opposite of an approval: an offer is only ever the first of two steps, so a
            // check that comes round while one is outstanding starts it over. Approving is then
            // still the second thing typed, and the changes are read again first.
            var scheduler = NewScheduler(StartupUpdateMode.Notify);
            scheduler.ApplyCheckResult(Available("2026.8.9.4"), interactive: true);
            scheduler.GetStatus().State.Should().Be(UpdateSchedulerState.Offered);

            scheduler.ApplyCheckResult(Available("2026.8.9.4"), interactive: false);

            scheduler.GetStatus().State.Should().Be(UpdateSchedulerState.Idle);
        }
    }
}
