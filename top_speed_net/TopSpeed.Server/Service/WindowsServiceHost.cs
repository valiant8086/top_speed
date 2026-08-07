using System;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Threading;

namespace TopSpeed.Server.Service
{
    /// <summary>
    /// Runs the server under the Windows service manager.
    ///
    /// A plain console program cannot simply be registered as a service: the manager expects
    /// to be answered within about thirty seconds of starting it, and a program busy running
    /// a race loop never answers at all. This does the answering, and runs the ordinary server
    /// body on a thread it can stop when asked.
    ///
    /// Everything else follows the same path a console server takes. Stopping cancels the same
    /// token that Ctrl+C and SIGTERM cancel, so players are told and the listener closes
    /// properly rather than the process being torn down mid-tick.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class WindowsServiceHost : ServiceBase
    {
        private readonly string[] _args;
        private readonly string _baseDirectory;
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private Thread? _worker;
        private int _exitCode;
        private volatile bool _managerAskedToStop;

        /// <summary>
        /// Reported to the service manager when the server stops in order to come back, which
        /// is what applying an update does. A manager only acts on a stop it can see went wrong,
        /// so a clean exit here would leave an updated server switched off until somebody
        /// noticed. The particular number carries no meaning beyond not being zero.
        /// </summary>
        private const int RestartWantedExitCode = 1;

        private WindowsServiceHost(string[] args, string baseDirectory)
        {
            _args = args;
            _baseDirectory = baseDirectory;

            // Worked out from the folder rather than fixed, so two folders can be installed at
            // once instead of colliding on one name.
            //
            // Windows ignores this at run time for a service that owns its process, so a
            // registration made by hand under some other name still works. It is set correctly
            // anyway: it costs nothing, and something that reports itself accurately is easier
            // to trust than something that happens to be ignored.
            ServiceName = ServiceIdentity.NameFor(baseDirectory);
            CanShutdown = true;
            CanStop = true;
        }

        public static int Run(string[] args, string baseDirectory)
        {
            using var host = new WindowsServiceHost(args, baseDirectory);
            ServiceBase.Run(host);
            return host._exitCode;
        }

        protected override void OnStart(string[] args)
        {
            _worker = new Thread(() =>
            {
                try
                {
                    _exitCode = Program.RunServer(_args, _baseDirectory, _shutdown);
                }
                catch (Exception)
                {
                    _exitCode = 1;
                }
                finally
                {
                    // A server that stops on its own, which is what an update does, has to tell
                    // the manager rather than leaving it believing the service is still running.
                    //
                    // What is checked is whether the manager was the one that asked, not whether
                    // a shutdown was requested at all. Every self chosen stop cancels the same
                    // token the manager's does, so testing the token treated an update, the
                    // shutdown command and a signal as though the manager already knew, and the
                    // service was left reading as running with nothing behind it.
                    if (!_managerAskedToStop)
                    {
                        if (ServiceRuntime.StoppingToRestart)
                            ExitCode = RestartWantedExitCode;

                        Stop();
                    }
                }
            })
            {
                IsBackground = false,
                Name = "TopSpeed.Server.Service"
            };
            _worker.Start();
        }

        protected override void OnStop()
        {
            _managerAskedToStop = true;
            RequestShutdown();
        }

        protected override void OnShutdown()
        {
            // The machine is going down. Same orderly stop, just less time to do it in.
            _managerAskedToStop = true;
            RequestShutdown();
        }

        private void RequestShutdown()
        {
            try
            {
                _shutdown.Cancel();
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            // Bounded so a wedged shutdown cannot leave the manager waiting indefinitely; the
            // race loop only has to notice a cancelled token, which takes a tick.
            _worker?.Join(TimeSpan.FromSeconds(20));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _shutdown.Dispose();

            base.Dispose(disposing);
        }
    }
}
