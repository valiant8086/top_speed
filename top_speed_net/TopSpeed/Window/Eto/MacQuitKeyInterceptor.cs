using System;
using MonoMac.AppKit;
using MonoMac.Foundation;

namespace TopSpeed.Windowing.Eto
{
    // Command+Q is a menu key equivalent: AppKit offers it to the main menu's Quit item before any
    // window sees a key event. This app has no menu bar - there is no GUI beyond the odd text box - so
    // nothing claims it and the shortcut does nothing at all, where Alt+F4 closes the game on Windows
    // and Linux. A local NSEvent monitor sees the press before that dispatch and routes it to the same
    // close request the Exit menu item and Escape at the main menu already use, so all three quit by
    // one path.
    //
    // This is a callback into AppKit's own dispatch, not a poll: nothing runs until a matching event
    // is delivered, and non-matching events cost one mask comparison.
    //
    // This file is compiled only for osx runtime identifiers (see TopSpeed.csproj): it needs MonoMac,
    // which only Eto.Platform.Mac64 provides. WindowHost locates it by name via reflection so shared
    // code never references it directly.
    internal static class MacQuitKeyInterceptor
    {
        private const ushort QKeyCode = 12;

        private static NSObject? _monitor;
        private static LocalEventHandler? _handler;

        public static void Install(Func<IntPtr> gameWindowHandle, Action onQuitRequested)
        {
            if (_monitor != null)
                return;

            // Kept in a static so the delegate outlives this call; the monitor holds it for the
            // lifetime of the process.
            _handler = theEvent =>
            {
                if (theEvent.KeyCode != QKeyCode)
                    return theEvent;
                if ((theEvent.ModifierFlags & NSEventModifierMask.CommandKeyMask) == 0)
                    return theEvent;

                // Anything with Control, Option or Shift added is a different shortcut, not Command+Q.
                var disqualifying = NSEventModifierMask.ControlKeyMask
                    | NSEventModifierMask.AlternateKeyMask
                    | NSEventModifierMask.ShiftKeyMask;
                if ((theEvent.ModifierFlags & disqualifying) != 0)
                    return theEvent;

                // Only quit for the game's own window. Command+Q while a file dialog is up belongs to
                // that dialog, and quitting out from under it would strand the callback waiting on it.
                var handle = gameWindowHandle();
                var targetWindow = theEvent.Window;
                if (handle == IntPtr.Zero || targetWindow == null || targetWindow.Handle != handle)
                    return theEvent;

                onQuitRequested();
                return null!;
            };

            _monitor = NSEvent.AddLocalMonitorForEventsMatchingMask(NSEventMask.KeyDown, _handler);
        }
    }
}
