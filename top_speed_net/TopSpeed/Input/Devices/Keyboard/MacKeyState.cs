using System;
using System.Runtime.InteropServices;

namespace TopSpeed.Input.Devices.Keyboard
{
    // Reports whether a key is physically held right now, straight from the HID layer, regardless of
    // who consumed the event on its way to the window. Every macOS keyboard the game has is fed by
    // events in the end, so a release that never arrives leaves that key held forever. SDL looks like
    // an exception and is not: asking it which keys are down reads a table it fills in from the same
    // event queue, so a key-up nobody delivered is missing there too.
    //
    // VoiceOver causes exactly that, because it watches the arrow chords (Left+Right toggles QuickNav,
    // Up+Left and Up+Right turn the rotor) and eats their key-ups. The visible result while driving is
    // a throttle or a steer that stays on after the key is let go. Windows never had this because
    // DirectInput polls the device instead of counting events; this is what lets macOS do the same.
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

        // False when this key has no macOS equivalent here, which leaves the caller's own view of it
        // untouched. Silence is the safe answer: a key we cannot verify keeps whatever state the
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

        // InputKey carries DirectInput scancodes; these are the macOS virtual keycodes for the same
        // physical keys. The synthetic BothShift/BothControl/BothAlt entries are deliberately absent —
        // they stand for either side rather than one key, so there is nothing single to query.
        private static bool TryMapKeyCode(InputKey key, out ushort code)
        {
            switch (key)
            {
                case InputKey.Up: code = 126; return true;
                case InputKey.Down: code = 125; return true;
                case InputKey.Left: code = 123; return true;
                case InputKey.Right: code = 124; return true;

                case InputKey.A: code = 0; return true;
                case InputKey.B: code = 11; return true;
                case InputKey.C: code = 8; return true;
                case InputKey.D: code = 2; return true;
                case InputKey.E: code = 14; return true;
                case InputKey.F: code = 3; return true;
                case InputKey.G: code = 5; return true;
                case InputKey.H: code = 4; return true;
                case InputKey.I: code = 34; return true;
                case InputKey.J: code = 38; return true;
                case InputKey.K: code = 40; return true;
                case InputKey.L: code = 37; return true;
                case InputKey.M: code = 46; return true;
                case InputKey.N: code = 45; return true;
                case InputKey.O: code = 31; return true;
                case InputKey.P: code = 35; return true;
                case InputKey.Q: code = 12; return true;
                case InputKey.R: code = 15; return true;
                case InputKey.S: code = 1; return true;
                case InputKey.T: code = 17; return true;
                case InputKey.U: code = 32; return true;
                case InputKey.V: code = 9; return true;
                case InputKey.W: code = 13; return true;
                case InputKey.X: code = 7; return true;
                case InputKey.Y: code = 16; return true;
                case InputKey.Z: code = 6; return true;

                case InputKey.D0: code = 29; return true;
                case InputKey.D1: code = 18; return true;
                case InputKey.D2: code = 19; return true;
                case InputKey.D3: code = 20; return true;
                case InputKey.D4: code = 21; return true;
                case InputKey.D5: code = 23; return true;
                case InputKey.D6: code = 22; return true;
                case InputKey.D7: code = 26; return true;
                case InputKey.D8: code = 28; return true;
                case InputKey.D9: code = 25; return true;

                case InputKey.Escape: code = 53; return true;
                case InputKey.Return: code = 36; return true;
                case InputKey.Tab: code = 48; return true;
                case InputKey.Space: code = 49; return true;
                case InputKey.Back: code = 51; return true;
                case InputKey.Capital: code = 57; return true;

                case InputKey.Minus: code = 27; return true;
                case InputKey.Equals: code = 24; return true;
                case InputKey.LeftBracket: code = 33; return true;
                case InputKey.RightBracket: code = 30; return true;
                case InputKey.Semicolon: code = 41; return true;
                case InputKey.Apostrophe: code = 39; return true;
                case InputKey.Grave: code = 50; return true;
                case InputKey.Backslash: code = 42; return true;
                case InputKey.Comma: code = 43; return true;
                case InputKey.Period: code = 47; return true;
                case InputKey.Slash: code = 44; return true;

                // The modifiers are deliberately absent, all of them. This layer cannot tell the two
                // sides of one apart: holding the right shift key reports the left one as held and
                // the right one as not, and the same goes for control, alt and the command keys.
                // Answering from here would lose the right hand side of every pair entirely and put
                // the left one down in its place - the right shift key doing nothing at all, while
                // a clutch on "either shift" worked from the left side only.
                //
                // The keyboard events know which side was pressed, so the modifiers are left to
                // them. Nothing is given up by that: what the hardware is here to rescue is the
                // arrow keys, whose presses a screen reader takes for its own shortcuts, and no
                // screen reader shortcut is watching for a bare shift.

                case InputKey.F1: code = 122; return true;
                case InputKey.F2: code = 120; return true;
                case InputKey.F3: code = 99; return true;
                case InputKey.F4: code = 118; return true;
                case InputKey.F5: code = 96; return true;
                case InputKey.F6: code = 97; return true;
                case InputKey.F7: code = 98; return true;
                case InputKey.F8: code = 100; return true;
                case InputKey.F9: code = 101; return true;
                case InputKey.F10: code = 109; return true;
                case InputKey.F11: code = 103; return true;
                case InputKey.F12: code = 111; return true;
                case InputKey.F13: code = 105; return true;
                case InputKey.F14: code = 107; return true;
                case InputKey.F15: code = 113; return true;

                case InputKey.Home: code = 115; return true;
                case InputKey.End: code = 119; return true;
                case InputKey.PageUp: code = 116; return true;
                case InputKey.PageDown: code = 121; return true;
                case InputKey.Delete: code = 117; return true;

                case InputKey.NumberPad0: code = 82; return true;
                case InputKey.NumberPad1: code = 83; return true;
                case InputKey.NumberPad2: code = 84; return true;
                case InputKey.NumberPad3: code = 85; return true;
                case InputKey.NumberPad4: code = 86; return true;
                case InputKey.NumberPad5: code = 87; return true;
                case InputKey.NumberPad6: code = 88; return true;
                case InputKey.NumberPad7: code = 89; return true;
                case InputKey.NumberPad8: code = 91; return true;
                case InputKey.NumberPad9: code = 92; return true;
                case InputKey.NumberPadEnter: code = 76; return true;
                case InputKey.NumberPadEquals: code = 81; return true;
                case InputKey.Decimal: code = 65; return true;
                case InputKey.Add: code = 69; return true;
                case InputKey.Subtract: code = 78; return true;
                case InputKey.Multiply: code = 67; return true;
                case InputKey.Divide: code = 75; return true;

                default:
                    code = 0;
                    return false;
            }
        }
    }
}
