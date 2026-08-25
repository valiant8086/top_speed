using TS.Sdl.Input;
using SdlKeyboard = TS.Sdl.Input.Keyboard;

namespace TopSpeed.Input.Devices.Keyboard.Backends.Sdl
{
    internal sealed class Device : IKeyboardDevice
    {
        private bool _suspended;

        public bool TryPopulateState(InputState state)
        {
            if (state == null)
                return false;
            if (_suspended)
                return true;

            var keyboard = SdlKeyboard.GetState();
            if (!keyboard.IsValid)
                return true;

            for (var i = 0; i < (int)Scancode.Count; i++)
            {
                var code = (Scancode)i;
                if (!keyboard.IsDown(code))
                    continue;
                if (!code.TryToInputKey(out var key))
                    continue;
                if (!IsStillHeld(key))
                    continue;

                state.Set(key, true);
            }

            return true;
        }

        public bool IsDown(InputKey key)
        {
            if (_suspended)
                return false;
            if (!key.TryToScancode(out var code))
                return false;

            var keyboard = SdlKeyboard.GetState();
            return keyboard.IsValid && keyboard.IsDown(code) && IsStillHeld(key);
        }

        // Asking SDL which keys are down reads a table it fills in from its event queue, so a key-up
        // that never arrived leaves that key down there for good. VoiceOver takes the key-ups of the
        // arrow chords it watches for its own shortcuts, which is enough to leave the throttle on or
        // the car steering after the key was let go. The hardware still knows the truth, so a key SDL
        // calls held is checked against it and dropped when the two disagree.
        //
        // Only ever drops a key, never adds one. A key held while another window had focus is not
        // input this game should act on, and reading it as a press from hardware state would be
        // exactly that. Anywhere but macOS, and for any key with no macOS equivalent, SDL is left to
        // speak for itself.
        private static bool IsStillHeld(InputKey key)
        {
            if (!MacKeyState.IsAvailable)
                return true;

            return !MacKeyState.TryIsPhysicallyDown(key, out var held) || held;
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
                if (!keyboard.IsDown(code))
                    continue;
                if (!code.TryToInputKey(out var key))
                    continue;
                if (ignoreModifiers && IsModifier(key))
                    continue;
                if (!IsStillHeld(key))
                    continue;

                return true;
            }

            return false;
        }

        public void ResetHeldState()
        {
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
