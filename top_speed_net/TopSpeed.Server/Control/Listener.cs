using System;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using TopSpeed.Localization;
using TopSpeed.Server.Commands;
using TopSpeed.Server.Logging;

namespace TopSpeed.Server.Control
{
    /// <summary>
    /// Accepts control connections and hands one of them the command session at a time.
    ///
    /// The endpoint is created before the server binds any network port, so it doubles as the
    /// instance lock: a second copy started from the same folder finds this listening and
    /// attaches instead of quietly claiming the same UDP ports.
    ///
    /// One loop does everything, and it never blocks on the client it is serving. Holding a
    /// session is nothing more than remembering it: the command host reads from it, and the
    /// operating system ends that read the moment the client goes, however it goes. What the
    /// loop keeps doing meanwhile is answering the door, so that a further copy started while
    /// one is attached is told so at once rather than left waiting in a queue, unanswered, until
    /// the attached one leaves. That wait was silent, and looked like a server that had hung.
    /// </summary>
    internal sealed class ControlListener : IDisposable
    {
        public const string Protocol = "TOPSPEED-CONTROL/1";

        /// <summary>How long one wait for a connection lasts before the loop looks around.</summary>
        private const int SliceMilliseconds = 250;

        /// <summary>
        /// How long Dispose gives the loop to notice the stop and leave. Longer than a slice, so
        /// a loop that is going to leave has left.
        /// </summary>
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

        private readonly string _directory;
        private readonly Logger _logger;
        private readonly Func<string> _describeStatus;

        private NamedPipeServerStream? _pipe;
        private Task? _pipeWait;
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

            // The endpoint is disposed only once nothing is waiting on it. On macOS, disposing a
            // socket that another thread is blocked in Accept on wakes nothing: the accept sleeps
            // on, the handle stays in use, and Dispose spins waiting for it to be let go, with
            // nothing that will ever let go of it. That held every shutdown of the server on a
            // Mac, quit and Ctrl+C and updates alike, on the last line before the process would
            // have ended, and it held the updater along with it. The loop waits in slices and
            // leaves on its own when told to, so there is nothing for Dispose to wake, only
            // something to wait for.
            _thread?.Join(StopTimeout);

            try
            {
                _pipe?.Dispose();
                _socket?.Dispose();
            }
            catch
            {
                // Nothing useful to do if the endpoint is already gone.
            }

            // Only what this listener actually bound. A copy that lost the claim to a server
            // already here is disposed on its way out too, and its socket is null: the file it
            // would otherwise remove belongs to the server that won, which would be left running,
            // unreachable and unable to be attached to for the rest of its life.
            if (!OperatingSystem.IsWindows() && _socket != null)
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
            Stream? heldStream = null;
            ControlCommandSession? held = null;

            try
            {
                while (!_stop)
                {
                    try
                    {
                        // The command host reads this session, and a client that has gone, by
                        // any route, ends that read. Nothing here has to ask it anything.
                        if (held != null && !held.CanRead)
                            EndSession(ref held, ref heldStream);

                        var stream = AwaitClient();
                        if (stream == null)
                            continue;

                        if (held != null)
                        {
                            TurnAway(stream, AttachRefusal.AlreadyAttached);
                            continue;
                        }

                        var session = new ControlCommandSession(stream);
                        session.WriteLine(Greeting());

                        if (!CommandSessions.TryAttach(session, out var refusal))
                        {
                            session.WriteLine("REFUSED " + refusal);
                            session.WriteLine(DescribeRefusal(refusal));
                            session.Dispose();
                            Release(stream);
                            continue;
                        }

                        session.WriteLine("OK");
                        session.WriteLine(_describeStatus());

                        // Replay what was printed before this client arrived, so attaching to a
                        // server that has been running unattended shows what it has been saying.
                        var recent = CommandSessions.RecentOutput();
                        for (var i = 0; i < recent.Length; i++)
                            session.WriteLine(recent[i]);

                        _logger.Info(LocalizationService.Mark("Control session attached to server."));
                        held = session;
                        heldStream = stream;
                    }
                    catch (Exception ex)
                    {
                        // Dropping the endpoint is how this loop is stopped, so whatever the wait
                        // throws on the way out is expected and there is nothing left to serve.
                        // Deciding that here rather than in a filter matters: a filter that
                        // declines leaves the exception to escape a thread with nothing above it.
                        if (_stop)
                            return;

                        _logger.Warning(LocalizationService.Format(
                            LocalizationService.Mark("Control connection failed: {0}"),
                            ex.Message));
                        Thread.Sleep(SliceMilliseconds);
                    }
                }
            }
            finally
            {
                if (held != null)
                    EndSession(ref held, ref heldStream);
            }
        }

        private void EndSession(ref ControlCommandSession? held, ref Stream? heldStream)
        {
            var session = held;
            var stream = heldStream;
            held = null;
            heldStream = null;
            if (session == null)
                return;

            CommandSessions.Detach(session);
            session.Dispose();
            if (stream != null)
                Release(stream);
            _logger.Info(LocalizationService.Mark("The control session detached."));
        }

        /// <summary>
        /// Waits one slice for a client and returns its stream, or null when nobody came. Never
        /// blocks longer than that, which is what lets the loop notice a stop, a client that has
        /// gone and a newcomer to turn away, whichever comes first.
        /// </summary>
        private Stream? AwaitClient()
        {
            if (OperatingSystem.IsWindows())
                return AwaitPipeClient();

            var socket = _socket;
            if (socket == null)
                return null;

            // Accept is only called once a connection is known to be waiting, so it returns at
            // once and the socket is never disposed underneath it.
            if (!socket.Poll(SliceMilliseconds * 1000, SelectMode.SelectRead))
                return null;

            var accepted = socket.Accept();
            return new NetworkStream(accepted, ownsSocket: true);
        }

        /// <summary>
        /// A pipe instance is the connection, so a client that has one is holding it and the next
        /// client needs another. One is kept listening at all times: the moment a client takes
        /// the current instance, a replacement is made, so the name never disappears and a
        /// further copy always finds something to connect to and be answered by.
        /// </summary>
        private Stream? AwaitPipeClient()
        {
            var listening = _pipe;
            if (listening == null)
            {
                _pipe = ControlTransport.CreatePipe(ControlEndpoint.PipeNameFor(_directory), firstInstance: false);
                return null;
            }

            _pipeWait ??= listening.WaitForConnectionAsync();
            if (!_pipeWait.Wait(SliceMilliseconds))
                return null;

            var wait = _pipeWait;
            _pipeWait = null;
            if (wait.IsFaulted)
                throw wait.Exception!.GetBaseException();

            _pipe = null;
            try
            {
                _pipe = ControlTransport.CreatePipe(ControlEndpoint.PipeNameFor(_directory), firstInstance: false);
            }
            catch (IOException)
            {
                // Made again on the next pass; until then the name is briefly absent, which is
                // what every client used to see and is still the safe way to fail.
            }
            catch (UnauthorizedAccessException)
            {
            }

            return listening;
        }

        /// <summary>
        /// Answers a client that arrived while another holds the session, and closes it. Said by
        /// the server, which knows, rather than guessed by the client from a wait that timed out.
        /// </summary>
        private void TurnAway(Stream stream, AttachRefusal refusal)
        {
            using (var session = new ControlCommandSession(stream))
            {
                session.WriteLine(Greeting());
                session.WriteLine("REFUSED " + refusal);
                session.WriteLine(DescribeRefusal(refusal));
            }

            Release(stream);
        }

        private string Greeting()
        {
            return Protocol + " " + ServerRelease() + " " + ControlEndpoint.NormalizeDirectory(_directory);
        }

        /// <summary>
        /// Closes a client's stream. On Windows that is a pipe instance of its own, and the one
        /// left listening was made when this one was taken, so nothing is made here.
        /// </summary>
        private static void Release(Stream stream)
        {
            try
            {
                stream.Dispose();
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
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

            return LocalizationService.Translate(LocalizationService.Mark("The server refused to attach this instance."));
        }
    }
}
