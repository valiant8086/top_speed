using System;
using SdlWindow = TS.Sdl.Video.Window;

namespace TopSpeed.Windowing.Sdl.Cocoa
{
    /// <summary>
    /// Watches for the game window being left with nothing listening to the keyboard, and hands the
    /// keyboard back to SDL's view when it happens.
    ///
    /// Anything AppKit puts up over the window - a file chooser above all - takes first responder
    /// with it, and does not reliably give it back. What is left is a window that is still the key
    /// window with nothing in it accepting keys: SDL sees no key presses so the game stops
    /// responding, AppKit beeps at every press because the event reaches the end of the responder
    /// chain unhandled, and a screen reader finds no focused element and calls it an empty window.
    /// Switching away and back cures it, because that makes AppKit work first responder out again.
    ///
    /// Rather than guess which of the things that can take focus failed to give it back, this looks
    /// for the state itself - key window, wrong first responder - and puts it right.
    /// </summary>
    internal static class MacWindowFocus
    {
        /// <summary>
        /// True when the window is the one taking keys but its first responder is not SDL's view,
        /// so key presses reach nothing. False on every other platform and in every other state.
        /// </summary>
        public static bool HasLostKeyboardFocus(IntPtr sdlWindow)
        {
            var window = GetCocoaWindow(sdlWindow);
            if (window == IntPtr.Zero)
                return false;

            // While a chooser or an alert is up it is the key window, not this one, so this is also
            // what keeps the check from fighting a window the player is deliberately using.
            if (!ObjC.SendBool(window, ObjC.Selector("isKeyWindow")))
                return false;

            var contentView = ObjC.Send(window, ObjC.Selector("contentView"));
            if (contentView == IntPtr.Zero)
                return false;

            return ObjC.Send(window, ObjC.Selector("firstResponder")) != contentView;
        }

        /// <summary>Gives the keyboard back to SDL's view. True if it took.</summary>
        public static bool RestoreKeyboardFocus(IntPtr sdlWindow)
        {
            var window = GetCocoaWindow(sdlWindow);
            if (window == IntPtr.Zero)
                return false;

            var contentView = ObjC.Send(window, ObjC.Selector("contentView"));
            if (contentView == IntPtr.Zero)
                return false;

            return ObjC.SendBool(window, ObjC.Selector("makeFirstResponder:"), contentView);
        }

        private static IntPtr GetCocoaWindow(IntPtr sdlWindow)
        {
            if (!OperatingSystem.IsMacOS() || sdlWindow == IntPtr.Zero)
                return IntPtr.Zero;

            return SdlWindow.GetCocoaWindow(sdlWindow);
        }
    }
}
