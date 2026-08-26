using System;
using TS.Sdl.Input;

namespace TopSpeed.Input.Devices.Keyboard.Backends.Sdl
{
    internal static class SdlKeyMap
    {
        public static bool TryToInputKey(this Scancode code, out InputKey key)
        {
            switch (code)
            {
                case Scancode.Escape: key = InputKey.Escape; return true;
                case Scancode.Alpha1: key = InputKey.D1; return true;
                case Scancode.Alpha2: key = InputKey.D2; return true;
                case Scancode.Alpha3: key = InputKey.D3; return true;
                case Scancode.Alpha4: key = InputKey.D4; return true;
                case Scancode.Alpha5: key = InputKey.D5; return true;
                case Scancode.Alpha6: key = InputKey.D6; return true;
                case Scancode.Alpha7: key = InputKey.D7; return true;
                case Scancode.Alpha8: key = InputKey.D8; return true;
                case Scancode.Alpha9: key = InputKey.D9; return true;
                case Scancode.Alpha0: key = InputKey.D0; return true;
                case Scancode.Minus: key = InputKey.Minus; return true;
                case Scancode.Equals: key = InputKey.Equals; return true;
                case Scancode.Backspace: key = InputKey.Back; return true;
                case Scancode.Tab: key = InputKey.Tab; return true;
                case Scancode.Q: key = InputKey.Q; return true;
                case Scancode.W: key = InputKey.W; return true;
                case Scancode.E: key = InputKey.E; return true;
                case Scancode.R: key = InputKey.R; return true;
                case Scancode.T: key = InputKey.T; return true;
                case Scancode.Y: key = InputKey.Y; return true;
                case Scancode.U: key = InputKey.U; return true;
                case Scancode.I: key = InputKey.I; return true;
                case Scancode.O: key = InputKey.O; return true;
                case Scancode.P: key = InputKey.P; return true;
                case Scancode.Leftbracket: key = InputKey.LeftBracket; return true;
                case Scancode.Rightbracket: key = InputKey.RightBracket; return true;
                case Scancode.Return: key = InputKey.Return; return true;
                case Scancode.LCtrl: key = InputKey.LeftControl; return true;
                case Scancode.A: key = InputKey.A; return true;
                case Scancode.S: key = InputKey.S; return true;
                case Scancode.D: key = InputKey.D; return true;
                case Scancode.F: key = InputKey.F; return true;
                case Scancode.G: key = InputKey.G; return true;
                case Scancode.H: key = InputKey.H; return true;
                case Scancode.J: key = InputKey.J; return true;
                case Scancode.K: key = InputKey.K; return true;
                case Scancode.L: key = InputKey.L; return true;
                case Scancode.Semicolon: key = InputKey.Semicolon; return true;
                case Scancode.Apostrophe: key = InputKey.Apostrophe; return true;
                case Scancode.Grave: key = InputKey.Grave; return true;
                case Scancode.LShift: key = InputKey.LeftShift; return true;
                case Scancode.Backslash: key = InputKey.Backslash; return true;
                case Scancode.Z: key = InputKey.Z; return true;
                case Scancode.X: key = InputKey.X; return true;
                case Scancode.C: key = InputKey.C; return true;
                case Scancode.V: key = InputKey.V; return true;
                case Scancode.B: key = InputKey.B; return true;
                case Scancode.N: key = InputKey.N; return true;
                case Scancode.M: key = InputKey.M; return true;
                case Scancode.Comma: key = InputKey.Comma; return true;
                case Scancode.Period: key = InputKey.Period; return true;
                case Scancode.Slash: key = InputKey.Slash; return true;
                case Scancode.RShift: key = InputKey.RightShift; return true;
                case Scancode.KpMultiply: key = InputKey.Multiply; return true;
                case Scancode.LAlt: key = InputKey.LeftAlt; return true;
                case Scancode.Space: key = InputKey.Space; return true;
                case Scancode.Capslock: key = InputKey.Capital; return true;
                case Scancode.F1: key = InputKey.F1; return true;
                case Scancode.F2: key = InputKey.F2; return true;
                case Scancode.F3: key = InputKey.F3; return true;
                case Scancode.F4: key = InputKey.F4; return true;
                case Scancode.F5: key = InputKey.F5; return true;
                case Scancode.F6: key = InputKey.F6; return true;
                case Scancode.F7: key = InputKey.F7; return true;
                case Scancode.F8: key = InputKey.F8; return true;
                case Scancode.F9: key = InputKey.F9; return true;
                case Scancode.F10: key = InputKey.F10; return true;
                case Scancode.NumLockClear: key = InputKey.NumberLock; return true;
                case Scancode.Scrolllock: key = InputKey.ScrollLock; return true;
                case Scancode.Kp7: key = InputKey.NumberPad7; return true;
                case Scancode.Kp8: key = InputKey.NumberPad8; return true;
                case Scancode.Kp9: key = InputKey.NumberPad9; return true;
                case Scancode.KpMinus: key = InputKey.Subtract; return true;
                case Scancode.Kp4: key = InputKey.NumberPad4; return true;
                case Scancode.Kp5: key = InputKey.NumberPad5; return true;
                case Scancode.Kp6: key = InputKey.NumberPad6; return true;
                case Scancode.KpPlus: key = InputKey.Add; return true;
                case Scancode.Kp1: key = InputKey.NumberPad1; return true;
                case Scancode.Kp2: key = InputKey.NumberPad2; return true;
                case Scancode.Kp3: key = InputKey.NumberPad3; return true;
                case Scancode.Kp0: key = InputKey.NumberPad0; return true;
                case Scancode.KpPeriod: key = InputKey.Decimal; return true;
                case Scancode.F11: key = InputKey.F11; return true;
                case Scancode.F12: key = InputKey.F12; return true;
                case Scancode.F13: key = InputKey.F13; return true;
                case Scancode.F14: key = InputKey.F14; return true;
                case Scancode.F15: key = InputKey.F15; return true;
                case Scancode.KpEquals: key = InputKey.NumberPadEquals; return true;
                case Scancode.MediaPreviousTrack: key = InputKey.PreviousTrack; return true;
                case Scancode.MediaNextTrack: key = InputKey.NextTrack; return true;
                case Scancode.KpEnter: key = InputKey.NumberPadEnter; return true;
                case Scancode.RCtrl: key = InputKey.RightControl; return true;
                case Scancode.Mute: key = InputKey.Mute; return true;
                case Scancode.MediaPlayPause: key = InputKey.PlayPause; return true;
                case Scancode.MediaStop: key = InputKey.MediaStop; return true;
                case Scancode.VolumeDown: key = InputKey.VolumeDown; return true;
                case Scancode.VolumeUp: key = InputKey.VolumeUp; return true;
                case Scancode.ACHome: key = InputKey.WebHome; return true;
                case Scancode.KpComma: key = InputKey.NumberPadComma; return true;
                case Scancode.KpDivide: key = InputKey.Divide; return true;
                case Scancode.Printscreen: key = InputKey.PrintScreen; return true;
                case Scancode.RAlt: key = InputKey.RightAlt; return true;
                case Scancode.Pause: key = InputKey.Pause; return true;
                case Scancode.Home: key = InputKey.Home; return true;
                case Scancode.Up: key = InputKey.Up; return true;
                case Scancode.Pageup: key = InputKey.PageUp; return true;
                case Scancode.Left: key = InputKey.Left; return true;
                case Scancode.Right: key = InputKey.Right; return true;
                case Scancode.End: key = InputKey.End; return true;
                case Scancode.Down: key = InputKey.Down; return true;
                case Scancode.Pagedown: key = InputKey.PageDown; return true;
                case Scancode.Insert: key = InputKey.Insert; return true;
                case Scancode.Delete: key = InputKey.Delete; return true;
                case Scancode.LGUI: key = InputKey.LeftWindowsKey; return true;
                case Scancode.RGUI: key = InputKey.RightWindowsKey; return true;
                case Scancode.Application: key = InputKey.Applications; return true;
                case Scancode.Power: key = InputKey.Power; return true;
                case Scancode.Sleep: key = InputKey.Sleep; return true;
                case Scancode.Wake: key = InputKey.Wake; return true;
                case Scancode.ACSearch: key = InputKey.WebSearch; return true;
                case Scancode.ACBookmarks: key = InputKey.WebFavorites; return true;
                case Scancode.ACRefresh: key = InputKey.WebRefresh; return true;
                case Scancode.ACStop: key = InputKey.WebStop; return true;
                case Scancode.ACForward: key = InputKey.WebForward; return true;
                case Scancode.ACBack: key = InputKey.WebBack; return true;
                case Scancode.MediaSelect: key = InputKey.MediaSelect; return true;
                default:
                    key = InputKey.Unknown;
                    return false;
            }
        }

        // Turned around from the table above rather than written out a second time. Spelling both
        // directions by hand is what left this one short of the letters and the digits, and a key
        // missing here is not a key that misbehaves - it is a key the game cannot see at all,
        // because asking whether it is down starts by asking which scancode it is. That took out
        // first-letter and digit navigation in every menu, and any driving action bound to a letter.
        //
        // Where several scancodes give the same key, the first one wins, which is the one on the
        // main part of the keyboard: scancodes run in that order, so the number row is reached
        // before the keypad. Keys standing for either side at once, such as BothShift, name no
        // single scancode and are absent by design; callers ask about the left and right ones.
        private static readonly Scancode[] Scancodes = BuildScancodes();

        public static bool TryToScancode(this InputKey key, out Scancode code)
        {
            var index = (int)key;
            if (index < 0 || index >= Scancodes.Length)
            {
                code = Scancode.Unknown;
                return false;
            }

            code = Scancodes[index];
            return code != Scancode.Unknown;
        }

        private static Scancode[] BuildScancodes()
        {
            var highest = 0;
            foreach (InputKey value in Enum.GetValues(typeof(InputKey)))
            {
                if ((int)value > highest)
                    highest = (int)value;
            }

            var map = new Scancode[highest + 1];
            for (var i = 0; i < (int)Scancode.Count; i++)
            {
                var candidate = (Scancode)i;
                if (!candidate.TryToInputKey(out var key))
                    continue;

                var index = (int)key;
                if (index < 0 || index >= map.Length || map[index] != Scancode.Unknown)
                    continue;

                map[index] = candidate;
            }

            return map;
        }
    }
}

