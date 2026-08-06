using System;
using System.Collections.Generic;

namespace TopSpeed.Server.Commands
{
    internal enum AttachRefusal
    {
        None,

        /// <summary>The server is running in its own console window, which holds the session.</summary>
        ConsoleHoldsSession,

        /// <summary>Somebody else is already attached.</summary>
        AlreadyAttached
    }

    /// <summary>
    /// Holds the one active command session and a short history of recent output.
    ///
    /// Only one session is interactive at a time, which is what keeps this simple: the whole
    /// command layer writes here without knowing whether a console window or a control
    /// connection is on the other end. Two concurrent sessions would interleave halfway
    /// through a stateful options menu, so the second one is refused rather than merged.
    /// </summary>
    internal static class CommandSessions
    {
        private const int RecentOutputLines = 200;

        private static readonly object Gate = new object();
        private static readonly Queue<string> Recent = new Queue<string>();

        private static ICommandSession _console = new HeadlessCommandSession();
        private static ICommandSession? _attached;

        /// <summary>Whether the server's own console is able to take commands.</summary>
        public static bool ConsoleHoldsSession
        {
            get
            {
                lock (Gate)
                    return _console.CanRead;
            }
        }

        public static bool HasAttachedSession
        {
            get
            {
                lock (Gate)
                    return _attached != null;
            }
        }

        public static void UseConsoleSession(ICommandSession session)
        {
            lock (Gate)
                _console = session ?? new HeadlessCommandSession();
        }

        public static bool TryAttach(ICommandSession session, bool takeOver, out AttachRefusal refusal)
        {
            refusal = AttachRefusal.None;
            if (session == null)
                return false;

            lock (Gate)
            {
                // A console window cannot be taken over, because the person at it is not
                // reachable to be told that it happened.
                if (_console.CanRead)
                {
                    refusal = AttachRefusal.ConsoleHoldsSession;
                    return false;
                }

                if (_attached != null && !takeOver)
                {
                    refusal = AttachRefusal.AlreadyAttached;
                    return false;
                }

                _attached = session;
                return true;
            }
        }

        public static void Detach(ICommandSession session)
        {
            lock (Gate)
            {
                if (ReferenceEquals(_attached, session))
                    _attached = null;
            }
        }

        /// <summary>Recent output, so an attaching client can see what it missed.</summary>
        public static string[] RecentOutput()
        {
            lock (Gate)
                return Recent.ToArray();
        }

        public static bool WriteLine(string text)
        {
            var line = text ?? string.Empty;

            ICommandSession target;
            lock (Gate)
            {
                Recent.Enqueue(line);
                while (Recent.Count > RecentOutputLines)
                    Recent.Dequeue();

                target = _attached ?? _console;
            }

            var written = target.WriteLine(line);
            if (written)
                return true;

            // A dead attached session must not be reported as a permanently dead console, or
            // the logger would stop writing to a console that is still perfectly fine.
            lock (Gate)
            {
                if (ReferenceEquals(_attached, target))
                {
                    _attached = null;
                    return true;
                }
            }

            return false;
        }

        public static bool TryReadLine(out string value)
        {
            ICommandSession target;
            lock (Gate)
                target = _attached ?? _console;

            if (target.TryReadLine(out value))
                return true;

            lock (Gate)
            {
                if (ReferenceEquals(_attached, target))
                {
                    // The client went away. The server keeps running and waits for the next one.
                    _attached = null;
                }
            }

            return false;
        }
    }
}
