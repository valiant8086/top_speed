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
            // So the hardware, which never lost the event, decides the arrows, and SDL speaks for
            // everything else - including, on purpose, the keys a screen reader took and answered
            // itself, which the game is better off never seeing.
            for (var i = 0; i < (int)Scancode.Count; i++)
            {
                var code = (Scancode)i;
                if (!code.TryToInputKey(out var key))
                    continue;

                if (Visible(key, HardwareSays(key) ?? keyboard.IsDown(code)))
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

            return Visible(key, down);
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

                // Asked about every key before anything is skipped: this is also what lifts a hold
                // once the key is let go of, and a key nobody ever asks about is never let go of.
                var held = Visible(key, HardwareSays(key) ?? keyboard.IsDown(code));
                if (!held)
                    continue;
                if (ignoreModifiers && IsModifier(key))
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

        /// <summary>
        /// What the game should see for a key, given what it is really doing. A key being held aside
        /// reads as up until it is actually let go of, and letting go is what puts it back to normal.
        ///
        /// Every read goes through here, including reads of keys that are up, because a key that is
        /// up is exactly the news this is waiting for. Skipping the call when the key is not down
        /// leaves nothing able to lift the hold, and the key never works again.
        /// </summary>
        private bool Visible(InputKey key, bool downNow)
        {
            var index = (int)key;
            if (index < 0 || index >= _ignoredUntilReleased.Length)
                return downNow;

            if (_ignoredUntilReleased[index])
            {
                if (downNow)
                    return false;

                _ignoredUntilReleased[index] = false;
            }

            return downNow;
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
