using System;
using System.Runtime.InteropServices;

namespace TopSpeed.Windowing.Sdl.Cocoa
{
    /// <summary>
    /// Gives macOS its run loop back for a moment.
    ///
    /// Asking SDL for events empties the event queue and nothing else. Key presses arrive that way,
    /// which is why typing works, but VoiceOver does not talk to a program through events: it asks
    /// over the accessibility interface, and those questions arrive as run loop sources. A loop that
    /// only drains events and then sleeps never answers them, so VoiceOver waits, and a text field
    /// that should read back as you type takes about a second to say anything.
    ///
    /// Running the loop for the length of time the game would have slept costs nothing and answers
    /// whatever is waiting. It also returns early when something arrives, so it is more responsive
    /// than sleeping rather than less.
    /// </summary>
    internal static class MacRunLoop
    {
        private const string CoreFoundation =
            "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        private const uint Utf8 = 0x08000100;

        // The default mode is named by this exact string, so one built here matches the one the
        // system uses without having to reach into the framework for the symbol.
        private const string DefaultModeName = "kCFRunLoopDefaultMode";

        private static readonly object Gate = new object();
        private static IntPtr _defaultMode;
        private static bool _resolved;

        public static bool IsSupported => OperatingSystem.IsMacOS();

        /// <summary>
        /// Runs the loop for at most this many seconds, returning as soon as something has been
        /// dealt with. False when the loop could not be run at all, leaving the caller to sleep.
        /// </summary>
        public static bool Spin(double seconds)
        {
            if (!IsSupported)
                return false;

            var mode = ResolveDefaultMode();
            if (mode == IntPtr.Zero)
                return false;

            try
            {
                CFRunLoopRunInMode(mode, seconds, true);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static IntPtr ResolveDefaultMode()
        {
            if (_resolved)
                return _defaultMode;

            lock (Gate)
            {
                if (_resolved)
                    return _defaultMode;

                try
                {
                    _defaultMode = CFStringCreateWithCString(IntPtr.Zero, DefaultModeName, Utf8);
                }
                catch (Exception)
                {
                    _defaultMode = IntPtr.Zero;
                }

                _resolved = true;
                return _defaultMode;
            }
        }

        [DllImport(CoreFoundation)]
        private static extern IntPtr CFStringCreateWithCString(
            IntPtr allocator,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value,
            uint encoding);

        [DllImport(CoreFoundation)]
        private static extern int CFRunLoopRunInMode(
            IntPtr mode,
            double seconds,
            [MarshalAs(UnmanagedType.I1)] bool returnAfterSourceHandled);
    }
}
