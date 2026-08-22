using Key = TopSpeed.Input.InputKey;

namespace TopSpeed.Input
{
    internal sealed partial class DriveInput
    {
        // Drive intents bind to plain keys and the bindings carry no modifiers, so without this a chord
        // fires its own action and the plain-key intent underneath it at the same time. Control+Tab
        // switches vehicle panels and also fired whatever is on Tab - Request info by default - reading
        // the race status out over the panel name. Nothing about that is specific to Tab: bind an intent
        // to O or F and any Control+O or Control+F the game grows later would do the same.
        //
        // What disqualifies a press is a modifier being held *when the key went down*, not one being
        // held now. Those are different situations and only the first is a chord. Holding the throttle
        // and then reaching for Control+Tab must not cut the throttle - the throttle key went down on
        // its own, and it keeps its intent until it is released.
        //
        // Shift is deliberately not disqualifying: on its own it is far more likely to be incidental
        // than meant, and the one chord that uses it, Control+Shift+Tab, is already caught by Control.
        private readonly bool[] _pressedUnderModifier = new bool[256];

        private void UpdateModifierChordState()
        {
            var modifierHeld = IsCtrlDown() || IsAltDown() || IsCommandDown();

            for (var i = 0; i < _pressedUnderModifier.Length; i++)
            {
                var key = (Key)i;
                if (!IsKeyDown(_lastState, key))
                {
                    _pressedUnderModifier[i] = false;
                    continue;
                }

                // Only the moment of the press decides; after that the flag rides until release.
                if (IsKeyDown(_prevState, key))
                    continue;

                // A modifier is never a chord against itself, or an intent bound to one could never fire.
                _pressedUnderModifier[i] = modifierHeld && !IsModifierKey(key);
            }
        }

        private bool WasPressedUnderModifier(Key key)
        {
            var index = (int)key;
            if (index < 0 || index >= _pressedUnderModifier.Length)
                return false;
            return _pressedUnderModifier[index];
        }

        private bool IsAltDown()
        {
            return _lastState.IsDown(Key.LeftAlt) || _lastState.IsDown(Key.RightAlt);
        }

        // Command on macOS arrives as the Windows-key scancodes.
        private bool IsCommandDown()
        {
            return _lastState.IsDown(Key.LeftWindowsKey) || _lastState.IsDown(Key.RightWindowsKey);
        }

        private static bool IsModifierKey(Key key)
        {
            return key == Key.LeftControl || key == Key.RightControl ||
                   key == Key.LeftShift || key == Key.RightShift ||
                   key == Key.LeftAlt || key == Key.RightAlt ||
                   key == Key.LeftWindowsKey || key == Key.RightWindowsKey;
        }
    }
}
