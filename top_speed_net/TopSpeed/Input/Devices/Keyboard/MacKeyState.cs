using System;
using System.Runtime.InteropServices;

namespace TopSpeed.Input.Devices.Keyboard
{
    // Reports whether an arrow key is physically held right now, straight from the HID layer,
    // regardless of who consumed the event on its way to the window. Every macOS keyboard the game
    // has is fed by events in the end, so a release that never arrives leaves that key held forever.
    // SDL looks like an exception and is not: asking it which keys are down reads a table it fills in
    // from the same event queue, so a key-up nobody delivered is missing there too.
    //
    // VoiceOver causes exactly that, because it watches the arrow chords (Left+Right toggles QuickNav,
    // Up+Left and Up+Right turn the rotor) and eats their key-ups. The visible result while driving is
    // a throttle or a steer that stays on after the key is let go. Windows never had this because
    // DirectInput polls the device instead of counting events; this is what lets macOS do the same.
    //
    // Only the arrows, deliberately. Reading a key from here means seeing it even when the screen
    // reader has already answered it, and for every other key that is a loss rather than a rescue:
    // it is how the volume moved when a VoiceOver command that happens to end in F7 was pressed. A
    // key VoiceOver keeps never reaches SDL's table, which is the same protection Windows gets from
    // DirectInput not seeing what NVDA took, and the game is better off with it. So the hardware is
    // asked only where events are demonstrably failing us, and the arrows are the whole of that.
    //
    // CGEventSourceKeyState is a read of shared HID state, not an event tap: it needs no accessibility
    // permission and takes well under a microsecond, so it is cheap enough to consult every frame.
    internal static class MacKeyState
    {
        private const string ApplicationServices =
            "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

        // kCGEventSourceStateHIDSystemState — the hardware's own view, rather than any one event stream.
        private const int HidSystemState = 1;

        private static readonly object ProbeLock = new object();
        private static bool _probed;
        private static bool _available;

        public static bool IsAvailable
        {
            get
            {
                if (_probed)
                    return _available;

                lock (ProbeLock)
                {
                    if (_probed)
                        return _available;

                    _available = Probe();
                    _probed = true;
                    return _available;
                }
            }
        }

        // False for every key this does not speak for, which leaves the caller's own view of it
        // untouched. Silence is the safe answer: a key we do not answer for keeps whatever state the
        // events gave it.
        public static bool TryIsPhysicallyDown(InputKey key, out bool down)
        {
            down = false;
            if (!IsAvailable)
                return false;

            if (!TryMapKeyCode(key, out var keyCode))
                return false;

            try
            {
                down = CGEventSourceKeyState(HidSystemState, keyCode);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool Probe()
        {
            if (!OperatingSystem.IsMacOS())
                return false;

            try
            {
                // Any keycode will do; this only has to prove the symbol resolves.
                CGEventSourceKeyState(HidSystemState, 126);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        [DllImport(ApplicationServices)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CGEventSourceKeyState(int stateId, ushort keyCode);

        // Only the arrows. Everything else is left to the keyboard events on purpose - see the note
        // at the top of this file - and the modifiers doubly so: asking after them by key code
        // cannot tell the two sides of a pair apart, so holding the right shift key would answer
        // for the left one and leave the right reading as up.
        private static bool TryMapKeyCode(InputKey key, out ushort code)
        {
            switch (key)
            {
                case InputKey.Up: code = 126; return true;
                case InputKey.Down: code = 125; return true;
                case InputKey.Left: code = 123; return true;
                case InputKey.Right: code = 124; return true;

                default:
                    code = 0;
                    return false;
            }
        }
    }
}
