using System;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace TopSpeed.Server.Control
{
    internal enum ControlConnectResult
    {
        Connected,

        /// <summary>Nothing is listening here, so no instance is running from this folder.</summary>
        NotRunning,

        /// <summary>An instance is running but will not let this caller talk to it.</summary>
        AccessDenied,

        /// <summary>
        /// An instance is running here but its endpoint is occupied, which means somebody
        /// already holds the one interactive session.
        /// </summary>
        Busy,

        Failed
    }

    /// <summary>
    /// The platform half of the control channel. Windows gets a named pipe, whose ACL is the
    /// access control; everything else gets a unix socket, whose file permissions are.
    ///
    /// Deliberately not a TCP listener on loopback: that has no access control at all, and any
    /// process belonging to any user on the machine could drive the server with it.
    /// </summary>
    internal static class ControlTransport
    {
        public static ControlConnectResult TryConnect(string directory, TimeSpan timeout, out Stream? stream)
        {
            stream = null;
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var client = new NamedPipeClientStream(
                        ".",
                        ControlEndpoint.PipeNameFor(directory),
                        PipeDirection.InOut,
                        PipeOptions.None);
                    client.Connect((int)timeout.TotalMilliseconds);
                    stream = client;
                    return ControlConnectResult.Connected;
                }

                var path = ControlEndpoint.SocketPathFor(directory);
                if (!File.Exists(path))
                    return ControlConnectResult.NotRunning;

                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                socket.Connect(new UnixDomainSocketEndPoint(path));
                stream = new NetworkStream(socket, ownsSocket: true);
                return ControlConnectResult.Connected;
            }
            catch (UnauthorizedAccessException)
            {
                // The endpoint exists and refused us. Distinguishing this from "nothing there"
                // is what lets the caller offer to elevate rather than elevating on spec.
                return ControlConnectResult.AccessDenied;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AccessDenied)
            {
                return ControlConnectResult.AccessDenied;
            }
            catch (TimeoutException)
            {
                // A connect that times out is ambiguous on its own: nothing may be listening,
                // or the one session may already be taken. The endpoint still exists in the
                // second case, which is what tells the two apart and lets the caller be told
                // that another window has it rather than that no server is here.
                return EndpointExists(directory)
                    ? ControlConnectResult.Busy
                    : ControlConnectResult.NotRunning;
            }
            catch (FileNotFoundException)
            {
                return ControlConnectResult.NotRunning;
            }
            catch (SocketException)
            {
                // A socket file left behind by a server that was killed rather than stopped.
                return ControlConnectResult.NotRunning;
            }
            catch (IOException)
            {
                return ControlConnectResult.Failed;
            }
        }

        /// <summary>
        /// Whether an endpoint exists for this folder, regardless of whether it can be
        /// connected to right now. Named pipes are enumerable, so this needs no permission
        /// beyond listing them, and it never has to elevate to find out.
        /// </summary>
        public static bool EndpointExists(string directory)
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                    return File.Exists(ControlEndpoint.SocketPathFor(directory));

                return File.Exists(@"\\.\pipe\" + ControlEndpoint.PipeNameFor(directory));
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                // Being refused a look still means something is there.
                return true;
            }
        }

        /// <summary>
        /// Creates the listening pipe. The ACL is the access control, so it is built here
        /// rather than left to defaults.
        /// </summary>
        [SupportedOSPlatform("windows")]
        public static NamedPipeServerStream CreatePipe(string pipeName, bool firstInstance)
        {
            var security = new PipeSecurity();

            // The account the server runs under, so it can talk to itself, plus whoever is
            // running this process, so the owner can attach without elevating. Everyone is
            // deliberately absent: reaching this pipe means being able to run any server
            // command, including one that downloads and executes an updater.
            var current = WindowsIdentity.GetCurrent();
            if (current.User != null)
                security.AddAccessRule(new PipeAccessRule(current.User, PipeAccessRights.ReadWrite, AccessControlType.Allow));

            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

            // FirstPipeInstance makes the very first creation fail if the name is already taken
            // rather than quietly joining it, so another process cannot squat the name and pose
            // as the server. Later instances are the replacements kept ready while one is busy,
            // which is what stops the name from ever disappearing between clients.
            var options = firstInstance
                ? PipeOptions.FirstPipeInstance | PipeOptions.Asynchronous
                : PipeOptions.Asynchronous;

            return NamedPipeServerStreamAcl.Create(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 4,
                PipeTransmissionMode.Byte,
                options,
                // Real buffers rather than zero. With no buffer every write waits for the other
                // side to read, so either end could block the moment it spoke first, and a
                // client that blocked writing a command stopped reading replies as well.
                inBufferSize: 8192,
                outBufferSize: 8192,
                security);
        }

        /// <summary>
        /// Creates the listening unix socket, owner-only. The umask is cleared around the bind
        /// because a socket file's permissions are subject to it, and a permissive umask would
        /// otherwise quietly widen access.
        /// </summary>
        [UnsupportedOSPlatform("windows")]
        public static Socket CreateSocket(string path)
        {
            // A file left behind by a server that was killed rather than stopped would block
            // the bind. Reaching here means nothing answered on it, so it is safe to clear.
            if (File.Exists(path))
                File.Delete(path);

            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Bind(new UnixDomainSocketEndPoint(path));
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            socket.Listen(backlog: 1);
            return socket;
        }
    }
}
