using System;
using System.IO;

namespace TopSpeed.Server.Commands
{
    /// <summary>
    /// Where server output goes and where commands are read from. The console is one such
    /// place; a control connection is another. Only one is ever active, which is what lets
    /// the whole command layer stay unaware of which it is talking to.
    /// </summary>
    internal interface ICommandSession
    {
        /// <summary>False means this session is finished and should be given up on.</summary>
        bool WriteLine(string text);

        /// <summary>False means no more input will arrive.</summary>
        bool TryReadLine(out string value);

        /// <summary>Whether this session can actually accept commands.</summary>
        bool CanRead { get; }
    }

    /// <summary>The server's own console window, when it has one.</summary>
    internal sealed class ConsoleCommandSession : ICommandSession
    {
        private volatile bool _exhausted;

        /// <summary>
        /// Redirected input looks available right up until it turns out to be empty, which is
        /// what stdin attached to nothing looks like under a service manager. So availability
        /// is settled by actually trying to read: once input ends, this session stops claiming
        /// the command session and somebody attaching can have it instead.
        /// </summary>
        public bool CanRead => !_exhausted && IsInputAvailable();

        public bool WriteLine(string text)
        {
            try
            {
                Console.WriteLine(text);
                return true;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }

        public bool TryReadLine(out string value)
        {
            value = string.Empty;
            try
            {
                var line = Console.ReadLine();
                if (line == null)
                {
                    _exhausted = true;
                    return false;
                }

                value = line;
                return true;
            }
            catch (InvalidOperationException)
            {
                _exhausted = true;
                return false;
            }
            catch (IOException)
            {
                _exhausted = true;
                return false;
            }
        }

        internal static bool IsInputAvailable()
        {
            if (Console.IsInputRedirected)
                return true;

            try
            {
                _ = Console.KeyAvailable;
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Used when the server has no console at all, which is the normal case under a service
    /// manager. Output is still accepted so that it reaches the log and the recent-output
    /// buffer, and so an attaching client can be shown what it missed.
    /// </summary>
    internal sealed class HeadlessCommandSession : ICommandSession
    {
        public bool CanRead => false;

        public bool WriteLine(string text) => true;

        public bool TryReadLine(out string value)
        {
            value = string.Empty;
            return false;
        }
    }
}
