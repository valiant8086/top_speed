using System;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Threading;
using TopSpeed.Localization;
using TopSpeed.Server.Commands;
using TopSpeed.Server.Logging;

namespace TopSpeed.Server.Control
{
    /// <summary>
    /// Accepts control connections and hands each one the command session, one at a time.
    ///
    /// The endpoint is created before the server binds any network port, so it doubles as the
    /// instance lock: a second copy started from the same folder finds this listening and
    /// attaches instead of quietly claiming the same UDP ports.
    /// </summary>
    internal sealed class ControlListener : IDisposable
    {
        public const string Protocol = "TOPSPEED-CONTROL/1";

        private readonly string _directory;
        private readonly Logger _logger;
        private readonly Func<string> _describeStatus;

        private NamedPipeServerStream? _pipe;
        private Socket? _socket;
        private Thread? _thread;
        private volatile bool _stop;

        public ControlListener(string directory, Logger logger, Func<string> describeStatus)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _describeStatus = describeStatus ?? throw new ArgumentNullException(nameof(describeStatus));
        }

        /// <summary>
        /// Claims the endpoint. Returns false when it is already taken, which is how a second
        /// copy discovers that this folder already has a server running.
        /// </summary>
        public bool TryStart()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                    _pipe = ControlTransport.CreatePipe(ControlEndpoint.PipeNameFor(_directory), firstInstance: true);
                else
                    _socket = ControlTransport.CreateSocket(ControlEndpoint.SocketPathFor(_directory));
            }
            catch (IOException)
            {
                // FirstPipeInstance refuses when the name is taken, which is exactly the signal
                // that another server owns this folder.
                return false;
            }
            catch (SocketException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }

            _thread = new Thread(RunLoop)
            {
                IsBackground = true,
                Name = "TopSpeed.Server.Control"
            };
            _thread.Start();
            return true;
        }

        public void Dispose()
        {
            _stop = true;
            try
            {
                _pipe?.Dispose();
                _socket?.Dispose();
            }
            catch
            {
                // Nothing useful to do if the endpoint is already gone.
            }

            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    var path = ControlEndpoint.SocketPathFor(_directory);
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch
                {
                    // A stale socket file is cleared by the next server to start here.
                }
            }
        }

        private void RunLoop()
        {
            while (!_stop)
            {
                try
                {
                    var stream = Accept();
                    if (stream == null)
                        continue;

                    try
                    {
                        Serve(stream);
                    }
                    finally
                    {
                        Release(stream);
                    }
                }
                catch (Exception ex)
                {
                    // Dropping the endpoint is how this loop is stopped, so whatever the wait
                    // throws on the way out is expected and there is nothing left to serve.
                    // Deciding that here rather than in a filter matters: a filter that declines
                    // leaves the exception to escape a thread with nothing above it.
                    if (_stop)
                        return;

                    _logger.Warning(LocalizationService.Format(
                        LocalizationService.Mark("Control connection failed: {0}"),
                        ex.Message));
                    Thread.Sleep(250);
                }
            }
        }

        private Stream? Accept()
        {
            if (OperatingSystem.IsWindows())
            {
                var listening = _pipe;
                if (listening == null)
                    return null;

                listening.WaitForConnection();
                return listening;
            }

            var socket = _socket;
            if (socket == null)
                return null;

            var accepted = socket.Accept();
            return new NetworkStream(accepted, ownsSocket: true);
        }

        /// <summary>
        /// Frees the endpoint for the next client.
        ///
        /// The served instance is replaced rather than disconnected and waited on again: an
        /// instance whose client has gone is left broken, and waiting on a broken one returns
        /// at once and forever, which is a spin rather than a wait.
        /// </summary>
        private void Release(Stream stream)
        {
            try
            {
                if (!OperatingSystem.IsWindows() || stream is not NamedPipeServerStream pipe)
                {
                    stream.Dispose();
                    return;
                }

                // The old instance is dropped before the replacement is made. Windows refuses
                // an additional instance carrying its own security descriptor while the name is
                // still held, so the two cannot overlap. The name is absent for that moment,
                // which is worth far more than an endpoint that never recovers.
                try
                {
                    pipe.Dispose();
                }
                catch (IOException)
                {
                }

                _pipe = ControlTransport.CreatePipe(
                    ControlEndpoint.PipeNameFor(_directory),
                    firstInstance: false);
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void Serve(Stream stream)
        {
            using var session = new ControlCommandSession(stream);
            session.WriteLine(Protocol + " " + ServerRelease() + " " + ControlEndpoint.NormalizeDirectory(_directory));

            if (!CommandSessions.TryAttach(session, out var refusal))
            {
                session.WriteLine("REFUSED " + refusal);
                session.WriteLine(DescribeRefusal(refusal));
                return;
            }

            try
            {
                session.WriteLine("OK");
                session.WriteLine(_describeStatus());

                // Replay what was printed before this client arrived, so attaching to a server
                // that has been running unattended shows what it has been saying.
                var recent = CommandSessions.RecentOutput();
                for (var i = 0; i < recent.Length; i++)
                    session.WriteLine(recent[i]);

                _logger.Info(LocalizationService.Mark("Control session attached to server."));

                // The command host is already looping on CommandSessions, so simply holding the
                // session open is what serves this client. Wait for it to go away.
                while (!_stop && session.CanRead)
                    Thread.Sleep(100);
            }
            finally
            {
                CommandSessions.Detach(session);
                _logger.Info(LocalizationService.Mark("The control session detached."));
            }
        }

        private static string ServerRelease()
        {
            return Updates.ServerUpdateConfig.CurrentVersion.ToMachineString();
        }

        /// <summary>
        /// Says which window already has the session, because finding it and using it is the
        /// whole remedy. There is no way to force one away from somebody else.
        /// </summary>
        private static string DescribeRefusal(AttachRefusal refusal)
        {
            if (refusal == AttachRefusal.ConsoleHoldsSession)
            {
                return LocalizationService.Translate(LocalizationService.Mark(
                    "This server already has a running instance in another interactive console window. Use that window to control it."));
            }

            // One sentence whether or not the moment it attached is known. Saying since when was
            // the only reason there were two, and it answers a question nobody asks: what is
            // wanted is the window, and the time it started does not help find it.
            if (refusal == AttachRefusal.AlreadyAttached)
            {
                return LocalizationService.Translate(LocalizationService.Mark(
                    "Another instance is already attached to this service."));
            }

            return LocalizationService.Translate(LocalizationService.Mark("The server refused to attach this window."));
        }
    }
}
