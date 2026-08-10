using System;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Threading;
using TopSpeed.Localization;

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
        private volatile bool _managerStartedUs;

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

            // Coming back without ever having been started means no service manager was
            // listening, which is what happens when somebody types the flag themselves. Windows
            // says so first, in its own words, and its advice is to install the service and use
            // net start; this says the same thing in the words this program answers to.
            //
            // Known by what happened rather than by guessing whether the process looks
            // interactive, for the same reason the flag exists at all: one is evidence and the
            // other is a hunch that is wrong for scheduled tasks and wrappers.
            if (!host._managerStartedUs)
            {
                // Straight to the console rather than through the session layer, which this
                // flag has already set to the one that says nothing, on the understanding that
                // a service has nobody to say it to. That understanding is what has just turned
                // out to be wrong.
                Say(LocalizationService.Translate(LocalizationService.Mark(
                    "--service is how a service manager starts this program, and does nothing typed by hand. Use --install-service to install this folder's server, then --start-service to start it.")));
                return 1;
            }

            return host._exitCode;
        }

        private static void Say(string text)
        {
            try
            {
                Console.WriteLine(text);
            }
            catch (System.IO.IOException)
            {
            }
        }

        protected override void OnStart(string[] args)
        {
            _managerStartedUs = true;
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
                    // A server that stops on its own, which an update and the shutdown command
                    // both do, has to tell the manager rather than leave it believing the service
                    // is still running. Whether the manager was the one that asked is the right
                    // question because it already knows in that case, and because every self
                    // chosen stop cancels the same token its own request does.
                    //
                    // Reported as the clean stop it is. An update used to report failure here so
                    // that the manager's restart action would bring the server back, but the
                    // updater starts the service itself now, and the pretence outlived its use in
                    // a way that did harm: a failure arms a restart timer that keeps running after
                    // the server is back, and firing it later turns somebody's deliberate stop
                    // into a service that switches itself on again two minutes afterwards.
                    if (!_managerAskedToStop)
                        Stop();
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

            // Waiting for the worker is what makes a stop orderly, but only from another thread.
            // A stop the server chose reaches here on the worker itself, by way of the finally
            // above, and a thread cannot wait for itself: the bound is all that ends it, so the
            // service would take the whole twenty seconds to report a stop that had already
            // happened. Every restart would carry that, with players waiting through it.
            //
            // Bounded when it does wait, so a wedged shutdown cannot leave the manager waiting
            // indefinitely; the race loop only has to notice a cancelled token, which takes a
            // tick.
            var worker = _worker;
            if (worker != null && worker != Thread.CurrentThread)
                worker.Join(TimeSpan.FromSeconds(20));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _shutdown.Dispose();

            base.Dispose(disposing);
        }
    }
}
