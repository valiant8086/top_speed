using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using TopSpeed.Localization;
using TopSpeed.Server.Logging;
using TopSpeed.Server.Network;

namespace TopSpeed.Server
{
    internal static partial class Program
    {
        private sealed class WindowsTimerResolution : IDisposable
        {
            private readonly uint _milliseconds;
            private readonly bool _active;

            public WindowsTimerResolution(uint milliseconds)
            {
                _milliseconds = milliseconds;
                try
                {
                    _active = timeBeginPeriod(_milliseconds) == 0;
                }
                catch
                {
                    _active = false;
                }
            }

            public void Dispose()
            {
                if (!_active)
                    return;

                try
                {
                    timeEndPeriod(_milliseconds);
                }
                catch
                {
                    // Ignore timer API shutdown failures.
                }
            }

            [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
            private static extern uint timeBeginPeriod(uint uPeriod);

            [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
            private static extern uint timeEndPeriod(uint uPeriod);
        }

        /// <summary>
        /// Turns a termination signal into the same orderly shutdown Ctrl+C already performs.
        /// Cancel tells the runtime not to terminate the process itself, so the main loop gets
        /// to unwind, disconnect players and stop the listener first.
        /// </summary>
        private static PosixSignalRegistration? CreateShutdownSignalHandler(
            PosixSignal signal,
            CancellationTokenSource shutdownSource,
            Logger logger)
        {
            try
            {
                return PosixSignalRegistration.Create(signal, context =>
                {
                    context.Cancel = true;
                    logger.Info(LocalizationService.Format(
                        LocalizationService.Mark("Received {0}. Shutting down."),
                        signal.ToString()));
                    shutdownSource.Cancel();
                });
            }
            catch (Exception ex)
            {
                // Not every platform offers every signal. Losing one is not worth refusing to run.
                logger.Warning(LocalizationService.Format(
                    LocalizationService.Mark("Could not listen for {0}: {1}"),
                    signal.ToString(),
                    ex.Message));
                return null;
            }
        }

        private static void RunLoop(RaceServer server, CancellationToken token)
        {
            var stopwatch = Stopwatch.StartNew();
            var last = stopwatch.Elapsed;
            while (!token.IsCancellationRequested)
            {
                var now = stopwatch.Elapsed;
                var deltaSeconds = (float)(now - last).TotalSeconds;
                last = now;
                server.Update(deltaSeconds);
                Thread.Sleep(1);
            }
        }
    }
}
