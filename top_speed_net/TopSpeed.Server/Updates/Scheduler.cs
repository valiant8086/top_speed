using System;
using System.Globalization;
using System.Threading;
using TopSpeed.Localization;
using TopSpeed.Server.Config;
using TopSpeed.Server.Logging;
using TopSpeed.Server.Network;

namespace TopSpeed.Server.Updates
{
    internal enum UpdateSchedulerState
    {
        Idle,

        /// <summary>A newer version is named by the manifest but its download is not there yet.</summary>
        AwaitingPublication,

        /// <summary>An update is approved and waiting for the last player to disconnect.</summary>
        PendingInstall
    }

    /// <summary>What the command thread should do after a check it asked for.</summary>
    internal enum CheckFollowUp
    {
        None,
        PromptToInstall
    }

    internal readonly struct UpdateSchedulerStatus
    {
        public UpdateSchedulerState State { get; init; }
        public string VersionText { get; init; }
        public int CompletedAttempts { get; init; }
        public TimeSpan TimeUntilNextAttempt { get; init; }
    }

    /// <summary>
    /// Owns everything about pending updates: whether one is known, whether its download exists
    /// yet, and when to look again. All state changes happen under one lock and only one check
    /// ever runs at a time, so the command thread and this thread cannot race each other.
    ///
    /// There is deliberately only ever one timer armed. While a download is being waited on the
    /// retry schedule replaces the daily interval rather than running alongside it, which is what
    /// makes it impossible for two checks to come due at the same moment.
    /// </summary>
    internal sealed class ServerUpdateScheduler : IDisposable
    {
        private static readonly TimeSpan PlayerPollInterval = TimeSpan.FromSeconds(5);

        private readonly RaceServer _server;
        private readonly ServerUpdateRunner _updater;
        private readonly Logger _logger;
        private readonly Action _requestShutdown;
        private readonly StartupUpdateMode _mode;

        private readonly object _gate = new object();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly Random _random = new Random();

        private UpdateSchedulerState _state = UpdateSchedulerState.Idle;
        private ServerUpdateInfo? _pending;
        private string _awaitingVersion = string.Empty;
        private int _attempts;
        private DateTime _nextDueUtc = DateTime.UtcNow;

        /// <summary>
        /// Separate from <see cref="_nextDueUtc"/> so that polling for an empty server, which is
        /// free and local, is never confused with checking GitHub, which is neither.
        /// </summary>
        private DateTime _installRetryAfterUtc = DateTime.MinValue;
        private bool _checkInFlight;
        private bool _installing;
        private Thread? _thread;
        private volatile bool _stop;

        public ServerUpdateScheduler(
            RaceServer server,
            ServerUpdateRunner updater,
            Logger logger,
            StartupUpdateMode mode,
            Action requestShutdown)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _updater = updater ?? throw new ArgumentNullException(nameof(updater));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _requestShutdown = requestShutdown ?? throw new ArgumentNullException(nameof(requestShutdown));
            _mode = mode;
        }

        public StartupUpdateMode Mode => _mode;

        public void Start()
        {
            _thread = new Thread(RunLoop)
            {
                IsBackground = true,
                Name = "TopSpeed.Server.Updates"
            };
            _thread.Start();
        }

        public void Dispose()
        {
            _stop = true;
            try
            {
                _wake.Set();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public UpdateSchedulerStatus GetStatus()
        {
            lock (_gate)
            {
                var remaining = _nextDueUtc - DateTime.UtcNow;
                return new UpdateSchedulerStatus
                {
                    State = _state,
                    VersionText = _state == UpdateSchedulerState.PendingInstall
                        ? _pending?.VersionText ?? string.Empty
                        : _awaitingVersion,
                    CompletedAttempts = _attempts,
                    TimeUntilNextAttempt = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero
                };
            }
        }

        /// <summary>Claims the right to run a check. Only one may be in flight at a time.</summary>
        public bool TryBeginCheck()
        {
            lock (_gate)
            {
                if (_checkInFlight || _installing)
                    return false;

                _checkInFlight = true;
                return true;
            }
        }

        public void EndCheck()
        {
            lock (_gate)
            {
                _checkInFlight = false;
            }
        }

        /// <summary>
        /// Folds a check result into the scheduler state and arms the next wakeup. Interactive
        /// checks stop short of arming an install so the owner can be shown the changes first.
        /// </summary>
        public CheckFollowUp ApplyCheckResult(ServerUpdateCheckResult result, bool interactive)
        {
            if (result == null)
                return CheckFollowUp.None;

            string? announcement = null;
            var followUp = CheckFollowUp.None;

            lock (_gate)
            {
                switch (result.Outcome)
                {
                    case ServerUpdateCheckOutcome.UpToDate:
                        ResetCycle();
                        ArmDaily();
                        break;

                    case ServerUpdateCheckOutcome.Failed:
                        _logger.Warning(LocalizationService.Format(
                            LocalizationService.Mark("Server update check failed: {0}"),
                            result.ErrorMessage));
                        // A check that could not complete still consumes an attempt, so a
                        // server that cannot reach GitHub at all does not retry forever.
                        if (_state == UpdateSchedulerState.AwaitingPublication)
                            announcement = AdvanceAwaitingPublication(_awaitingVersion);
                        else
                            ArmDaily();
                        break;

                    case ServerUpdateCheckOutcome.NotPublished:
                        announcement = AdvanceAwaitingPublication(result.VersionText);
                        break;

                    case ServerUpdateCheckOutcome.UpdateAvailable when result.Update != null:
                        ResetCycle();
                        if (interactive)
                        {
                            followUp = CheckFollowUp.PromptToInstall;
                            ArmDaily();
                        }
                        else if (_mode == StartupUpdateMode.Auto)
                        {
                            _state = UpdateSchedulerState.PendingInstall;
                            _pending = result.Update;
                            // The install itself waits on the player count, not on this; this
                            // only paces re-checks made while players are still connected.
                            ArmDaily();
                            announcement = LocalizationService.Format(
                                LocalizationService.Mark("Version {0} is available and will be installed once no players are connected."),
                                result.Update.VersionText);
                        }
                        else
                        {
                            ArmDaily();
                            announcement = LocalizationService.Format(
                                LocalizationService.Mark("Version {0} is available. Type update to install it."),
                                result.Update.VersionText);
                        }

                        break;
                }
            }

            if (announcement != null)
                Announce(announcement);

            return followUp;
        }

        /// <summary>Approves an update found by an interactive check.</summary>
        public void ArmInstall(ServerUpdateInfo update)
        {
            if (update == null)
                return;

            lock (_gate)
            {
                _state = UpdateSchedulerState.PendingInstall;
                _pending = update;
                _installRetryAfterUtc = DateTime.MinValue;
                ArmDaily();
            }

            _wake.Set();
        }

        /// <summary>
        /// Stops waiting, whatever is being waited on: a pending install goes ahead with players
        /// still connected, and a pending re-check happens immediately.
        /// </summary>
        public bool TryForceNow(out ServerUpdateInfo? installNow)
        {
            installNow = null;
            lock (_gate)
            {
                if (_installing)
                    return false;

                if (_state == UpdateSchedulerState.PendingInstall && _pending != null)
                {
                    installNow = _pending;
                    _installing = true;
                    return true;
                }

                if (_state == UpdateSchedulerState.AwaitingPublication)
                {
                    _nextDueUtc = DateTime.UtcNow;
                    _wake.Set();
                    return true;
                }

                return false;
            }
        }

        /// <summary>Runs an install that the command thread forced, with progress on screen.</summary>
        public void InstallNow(ServerUpdateInfo update)
        {
            PerformInstall(update, showProgress: true);
        }

        public void ReleaseForcedInstall()
        {
            lock (_gate)
            {
                _installing = false;
            }
        }

        private void RunLoop()
        {
            // Mode Off never schedules anything; it only answers manual update commands.
            if (_mode == StartupUpdateMode.Off)
                return;

            while (!_stop)
            {
                var wait = ComputeWait();
                if (wait > TimeSpan.Zero)
                    _wake.WaitOne(wait);

                if (_stop)
                    return;

                try
                {
                    Tick();
                }
                catch (Exception ex)
                {
                    _logger.Error(LocalizationService.Format(
                        LocalizationService.Mark("Update scheduler failed: {0}"),
                        ex.Message));
                }
            }
        }

        private TimeSpan ComputeWait()
        {
            lock (_gate)
            {
                // While an install is pending the server has to be watched for emptying, which
                // is a local counter rather than a request, so polling it often costs nothing.
                if (_state == UpdateSchedulerState.PendingInstall)
                    return PlayerPollInterval;

                var remaining = _nextDueUtc - DateTime.UtcNow;
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }

        private void Tick()
        {
            UpdateSchedulerState state;
            bool due;
            DateTime installRetryAfter;

            lock (_gate)
            {
                state = _state;
                due = DateTime.UtcNow >= _nextDueUtc;
                installRetryAfter = _installRetryAfterUtc;
                if (_checkInFlight || _installing)
                    return;
            }

            if (state == UpdateSchedulerState.PendingInstall)
            {
                if (_server.GetPlayersSnapshot().Length == 0)
                {
                    // A failed install sets a backoff so a bad download is not fetched again
                    // on every poll.
                    if (DateTime.UtcNow >= installRetryAfter)
                        PerformVerifiedInstall();

                    return;
                }

                // Players are still racing, so spend the wait keeping track of what the newest
                // version is. There is only ever one release, and publishing a new one deletes
                // the assets of the old one, so a pending update goes stale rather than simply
                // becoming out of date.
                if (due)
                    RunScheduledCheck();

                return;
            }

            if (!due)
                return;

            RunScheduledCheck();
        }

        private void RunScheduledCheck()
        {
            if (!TryBeginCheck())
                return;

            try
            {
                var result = _updater.Check();
                ApplyCheckResult(result, interactive: false);
            }
            finally
            {
                EndCheck();
            }
        }

        /// <summary>
        /// Checks again before installing, and installs whatever that check returns rather than
        /// what was stored earlier. A download URL only stays valid until the next release is
        /// published, so anything held for a while is not safe to reuse.
        /// </summary>
        private void PerformVerifiedInstall()
        {
            if (!TryBeginCheck())
                return;

            ServerUpdateCheckResult result;
            try
            {
                result = _updater.Check();
            }
            finally
            {
                EndCheck();
            }

            switch (result.Outcome)
            {
                case ServerUpdateCheckOutcome.UpdateAvailable when result.Update != null:
                    var approved = ApproveForInstall(result.Update);
                    if (approved != null)
                        PerformInstall(approved, showProgress: false);
                    return;

                case ServerUpdateCheckOutcome.UpToDate:
                    // The version was withdrawn, or another copy of the server already applied it.
                    lock (_gate)
                    {
                        ResetCycle();
                        ArmDaily();
                    }

                    Announce(LocalizationService.Mark("The pending update is no longer offered and has been dropped."));
                    return;

                case ServerUpdateCheckOutcome.NotPublished:
                    ApplyCheckResult(result, interactive: false);
                    return;

                default:
                    ApplyCheckResult(result, interactive: false);
                    return;
            }
        }

        /// <summary>
        /// Decides whether a freshly checked update may be installed in place of the pending one.
        /// Auto always takes the newest. Notify does not, because the whole point of that mode is
        /// that the owner chooses which version goes on; a different one needs saying so.
        /// </summary>
        private ServerUpdateInfo? ApproveForInstall(ServerUpdateInfo found)
        {
            string? announcement = null;

            lock (_gate)
            {
                var pendingVersion = _pending?.VersionText ?? string.Empty;
                var sameVersion = string.Equals(pendingVersion, found.VersionText, StringComparison.OrdinalIgnoreCase);
                if (sameVersion)
                {
                    _pending = found;
                    return found;
                }

                if (_mode == StartupUpdateMode.Auto)
                {
                    _pending = found;
                    announcement = LocalizationService.Format(
                        LocalizationService.Mark("Version {0} is no longer offered. Installing version {1} instead."),
                        pendingVersion,
                        found.VersionText);
                }
                else
                {
                    ResetCycle();
                    ArmDaily();
                    announcement = LocalizationService.Format(
                        LocalizationService.Mark("Version {0} could not be installed because it is no longer offered. Version {1} is available instead. Type update to install it."),
                        pendingVersion,
                        found.VersionText);
                }
            }

            if (announcement != null)
                Announce(announcement);

            lock (_gate)
            {
                return _state == UpdateSchedulerState.PendingInstall ? _pending : null;
            }
        }

        private void PerformInstall(ServerUpdateInfo update, bool showProgress)
        {
            lock (_gate)
            {
                _installing = true;
            }

            _logger.Info(LocalizationService.Format(
                LocalizationService.Mark("Installing server update {0}."),
                update.VersionText));

            if (!_updater.Install(update, showProgress))
            {
                // Leave the update armed. The server is empty when an unattended install runs,
                // so trying again on the next tick costs nobody anything.
                lock (_gate)
                {
                    _installing = false;
                    ArmRetryAfterFailedInstall();
                }

                return;
            }

            Announce(LocalizationService.Format(
                LocalizationService.Mark("Update {0} is ready to install. The server is shutting down to apply it."),
                update.VersionText));
            _requestShutdown();
        }

        // Callers hold _gate.
        private void ResetCycle()
        {
            _state = UpdateSchedulerState.Idle;
            _pending = null;
            _awaitingVersion = string.Empty;
            _attempts = 0;
            _installRetryAfterUtc = DateTime.MinValue;
        }

        // Callers hold _gate.
        private void ArmDaily()
        {
            _nextDueUtc = DateTime.UtcNow + UpdateRetrySchedule.DailyInterval;
        }

        // Callers hold _gate.
        private void ArmRetryAfterFailedInstall()
        {
            _installRetryAfterUtc = DateTime.UtcNow + UpdateRetrySchedule.NextDelay(int.MaxValue);
        }

        /// <summary>
        /// Advances the not-published cycle and returns a line to print, or null to stay quiet.
        /// Only the first two failures and the final one are worth interrupting anybody for.
        /// Callers hold _gate.
        /// </summary>
        private string? AdvanceAwaitingPublication(string versionText)
        {
            var version = string.IsNullOrWhiteSpace(versionText) ? _awaitingVersion : versionText.Trim();

            // A different version means the old cycle is moot and a fresh one starts.
            if (_state != UpdateSchedulerState.AwaitingPublication ||
                !string.Equals(_awaitingVersion, version, StringComparison.OrdinalIgnoreCase))
            {
                _state = UpdateSchedulerState.AwaitingPublication;
                _awaitingVersion = version;
                _attempts = 0;
                _pending = null;
            }

            _attempts++;
            _logger.Info(LocalizationService.Format(
                LocalizationService.Mark("Update {0} is not published yet. Attempt {1} of {2}."),
                version,
                _attempts,
                UpdateRetrySchedule.MaxAttempts));

            if (UpdateRetrySchedule.IsExhausted(_attempts))
            {
                ResetCycle();
                ArmDaily();
                return LocalizationService.Format(
                    LocalizationService.Mark("Update failed. The download for version {0} is still not available after {1} attempts. This can happen if that build did not finish. Type update to try again."),
                    version,
                    UpdateRetrySchedule.MaxAttempts);
            }

            var delay = UpdateRetrySchedule.ApplyJitter(UpdateRetrySchedule.NextDelay(_attempts), _random);
            _nextDueUtc = DateTime.UtcNow + delay;

            if (_attempts == 1)
            {
                return LocalizationService.Format(
                    LocalizationService.Mark("Version {0} is available, but its download has not been published yet. This is normal for a short time after a release. The server will check again in {1} minutes."),
                    version,
                    (int)Math.Round(delay.TotalMinutes));
            }

            if (_attempts == 2)
            {
                return LocalizationService.Format(
                    LocalizationService.Mark("Version {0} is still not published. The server will keep checking hourly and will say so if it gives up."),
                    version);
            }

            return null;
        }

        /// <summary>
        /// Update messages are timestamped because the console is often left hidden, and a bare
        /// line gives no clue whether it appeared minutes or hours ago.
        /// </summary>
        private void Announce(string message)
        {
            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            ConsoleSink.WriteLine("[" + stamp + "] " + LocalizationService.Translate(message));
            _logger.Info(message);
        }
    }
}
