using System;

namespace TopSpeed.Server.Updates
{
    /// <summary>
    /// When the manifest names a version whose download is not published yet, the server
    /// re-checks on a schedule that front-loads the common case: a release is normally
    /// downloadable within about fifteen minutes of the version being bumped, so the first
    /// retry lands just past that. After that it settles to hourly, on the assumption that
    /// a build which is genuinely broken is being worked on and will reappear.
    ///
    /// Everything here is a pure function of the attempt number so the whole policy can be
    /// read and tested without a clock.
    /// </summary>
    internal static class UpdateRetrySchedule
    {
        /// <summary>
        /// Total attempts in one cycle, counting the discovery attempt. Attempt 25 lands at
        /// hour 23, so a cycle always finishes before the daily interval that follows it.
        /// </summary>
        public const int MaxAttempts = 25;

        public static readonly TimeSpan DailyInterval = TimeSpan.FromHours(24);

        private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromMinutes(20);
        private static readonly TimeSpan SecondRetryDelay = TimeSpan.FromMinutes(40);
        private static readonly TimeSpan SteadyRetryDelay = TimeSpan.FromMinutes(60);

        /// <summary>Largest amount added to a delay to keep servers from retrying in lockstep.</summary>
        public static readonly TimeSpan MaxJitter = TimeSpan.FromSeconds(120);

        /// <summary>
        /// How long to wait after <paramref name="completedAttempts"/> attempts have failed.
        /// The 20 then 40 minute steps put the third attempt on the hour, after which the
        /// schedule is simply hourly.
        /// </summary>
        public static TimeSpan NextDelay(int completedAttempts)
        {
            return completedAttempts switch
            {
                <= 1 => FirstRetryDelay,
                2 => SecondRetryDelay,
                _ => SteadyRetryDelay
            };
        }

        public static bool IsExhausted(int completedAttempts)
        {
            return completedAttempts >= MaxAttempts;
        }

        /// <summary>
        /// Jitter is only ever added, never subtracted, so a delay is always at least as long
        /// as <see cref="NextDelay"/> promises.
        /// </summary>
        public static TimeSpan ApplyJitter(TimeSpan delay, Random random)
        {
            if (random == null)
                return delay;

            return delay + TimeSpan.FromMilliseconds(random.Next(0, (int)MaxJitter.TotalMilliseconds));
        }

        /// <summary>Time from the start of a cycle to the given attempt, ignoring jitter.</summary>
        public static TimeSpan ElapsedAtAttempt(int attemptNumber)
        {
            if (attemptNumber <= 1)
                return TimeSpan.Zero;

            var elapsed = TimeSpan.Zero;
            for (var completed = 1; completed < attemptNumber; completed++)
                elapsed += NextDelay(completed);

            return elapsed;
        }
    }
}
