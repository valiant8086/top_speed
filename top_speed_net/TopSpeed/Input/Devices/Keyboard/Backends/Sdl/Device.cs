using System;
using TS.Sdl.Input;
using SdlKeyboard = TS.Sdl.Input.Keyboard;

namespace TopSpeed.Input.Devices.Keyboard.Backends.Sdl
{
    internal sealed class Device : IKeyboardDevice
    {
        private bool _suspended;
        private readonly bool[] _ignoredUntilReleased = BuildIgnoreTable();

        private static bool[] BuildIgnoreTable()
        {
            var highest = 0;
            foreach (InputKey value in Enum.GetValues(typeof(InputKey)))
            {
                if ((int)value > highest)
                    highest = (int)value;
            }

            return new bool[highest + 1];
        }

        public bool TryPopulateState(InputState state)
        {
            if (state == null)
                return false;
            if (_suspended)
                return true;

            var keyboard = SdlKeyboard.GetState();
            if (!keyboard.IsValid)
                return true;

            // SDL fills its table from its own event queue, so a key event something else swallowed
            // never reaches it. VoiceOver swallows both halves of the arrow chords it watches - Left
            // with Right, Up with Left or Right - which leaves the table wrong in both directions:
            // a missing key-up holds the throttle on after it was let go, and a missing key-down
            // stops the car steering while the key is still held. The second is the worse of the two
            // on an oval, where left is held for most of the lap.
            //
            // So the hardware, which never lost the event, decides for every key it can name, and
            // SDL speaks only for the rest.
            for (var i = 0; i < (int)Scancode.Count; i++)
            {
                var code = (Scancode)i;
                if (!code.TryToInputKey(out var key))
                    continue;

                var held = HardwareSays(key) ?? keyboard.IsDown(code);
                if (held && !IsIgnored(key, held))
                    state.Set(key, true);
            }

            return true;
        }

        public bool IsDown(InputKey key)
        {
            if (_suspended)
                return false;
            bool down;
            var hardware = HardwareSays(key);
            if (hardware.HasValue)
            {
                down = hardware.Value;
            }
            else
            {
                if (!key.TryToScancode(out var code))
                    return false;

                var keyboard = SdlKeyboard.GetState();
                down = keyboard.IsValid && keyboard.IsDown(code);
            }

            return down && !IsIgnored(key, down);
        }

        /// <summary>
        /// What the hardware says about a key, when the hardware is worth asking. Null means it is
        /// not, and SDL's own answer stands.
        /// </summary>
        private static bool? HardwareSays(InputKey key)
        {
            if (!MacKeyState.IsAvailable)
                return null;

            // Only while the keyboard is actually talking to us. Keys held in another application
            // are not input this game should act on, and this is the whole reason the hardware can
            // be trusted to add a key here rather than only to take one away.
            if (SdlKeyboard.GetFocusedWindow() == IntPtr.Zero)
                return null;

            return MacKeyState.TryIsPhysicallyDown(key, out var held) ? held : (bool?)null;
        }

        public bool IsAnyKeyHeld(bool ignoreModifiers)
        {
            if (_suspended)
                return false;

            var keyboard = SdlKeyboard.GetState();
            if (!keyboard.IsValid)
                return false;

            for (var i = 0; i < (int)Scancode.Count; i++)
            {
                var code = (Scancode)i;
                if (!code.TryToInputKey(out var key))
                    continue;
                if (ignoreModifiers && IsModifier(key))
                    continue;
                var held = HardwareSays(key) ?? keyboard.IsDown(code);
                if (!held || IsIgnored(key, held))
                    continue;

                return true;
            }

            return false;
        }

        /// <summary>
        /// Forgets whatever is held right now, until it is let go of and pressed again. Reading the
        /// keys as they are means a key still down from whatever just ended - the Return that sent a
        /// chat message, say - would otherwise be read straight away as a fresh press in the game
        /// behind it. The event-driven keyboards get this for nothing, because clearing what they
        /// believe leaves nothing to report until a new press arrives; here the key is still
        /// physically down, so it has to be held aside by name.
        /// </summary>
        public void ResetHeldState()
        {
            var keyboard = SdlKeyboard.GetState();
            for (var i = 0; i < (int)Scancode.Count; i++)
            {
                var code = (Scancode)i;
                if (!code.TryToInputKey(out var key))
                    continue;

                var index = (int)key;
                if (index < 0 || index >= _ignoredUntilReleased.Length)
                    continue;
                if (HardwareSays(key) ?? (keyboard.IsValid && keyboard.IsDown(code)))
                    _ignoredUntilReleased[index] = true;
            }
        }

        // True while the key is being held aside. Stops as soon as it is actually let go of, which
        // is what makes the next press count normally.
        private bool IsIgnored(InputKey key, bool downNow)
        {
            var index = (int)key;
            if (index < 0 || index >= _ignoredUntilReleased.Length)
                return false;
            if (!_ignoredUntilReleased[index])
                return false;
            if (downNow)
                return true;

            _ignoredUntilReleased[index] = false;
            return false;
        }

        public void Suspend()
        {
            _suspended = true;
        }

        public void Resume()
        {
            _suspended = false;
        }

        public void Dispose()
        {
        }

        private static bool IsModifier(InputKey key)
        {
            return key == InputKey.LeftControl ||
                   key == InputKey.RightControl ||
                   key == InputKey.LeftShift ||
                   key == InputKey.RightShift ||
                   key == InputKey.LeftAlt ||
                   key == InputKey.RightAlt;
        }
    }
}
