using System;
using System.Collections.Generic;
using System.Threading;

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
        private static readonly ManualResetEventSlim SessionAvailable = new ManualResetEventSlim(false);

        private static ICommandSession _console = new HeadlessCommandSession();
        private static ICommandSession? _attached;
        private static volatile bool _stopping;

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
                SessionAvailable.Set();
                return true;
            }
        }

        public static void Detach(ICommandSession session)
        {
            lock (Gate)
            {
                if (!ReferenceEquals(_attached, session))
                    return;

                _attached = null;
                SessionAvailable.Reset();
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

        /// <summary>
        /// Blocks until a command arrives from whichever session is active. A server with no
        /// console and nobody attached waits here rather than giving up, which is what lets the
        /// command loop keep running under a service manager until somebody attaches to it.
        /// False means the server is shutting down.
        /// </summary>
        public static bool TryReadLine(out string value)
        {
            value = string.Empty;

            while (!_stopping)
            {
                ICommandSession target;
                lock (Gate)
                    target = _attached ?? _console;

                if (!target.CanRead)
                {
                    // Polled rather than purely event driven so that shutdown is always noticed
                    // even if no session ever arrives.
                    SessionAvailable.Wait(TimeSpan.FromMilliseconds(250));
                    continue;
                }

                if (target.TryReadLine(out value))
                    return true;

                lock (Gate)
                {
                    if (ReferenceEquals(_attached, target))
                    {
                        // The client went away. The server keeps running and waits for the next.
                        _attached = null;
                        SessionAvailable.Reset();
                        continue;
                    }
                }

                // The console ran out of input. It now reports itself unreadable, so the loop
                // falls through to waiting for somebody to attach rather than giving up.
            }

            return false;
        }

        public static void Stop()
        {
            _stopping = true;
            SessionAvailable.Set();
        }
    }
}
