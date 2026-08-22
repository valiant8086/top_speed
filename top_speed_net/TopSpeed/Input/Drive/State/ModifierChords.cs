using Key = TopSpeed.Input.InputKey;

namespace TopSpeed.Input
{
    internal sealed partial class DriveInput
    {
        // Drive intents bind to bare keys and carry no modifiers of their own, while shortcuts bind to
        // a key plus modifiers. The two are resolved independently from the same keyboard, so without
        // this a combination fires its own action and the intent bound to the key underneath it at the
        // same time: Control+Tab switches vehicle panels and also fired whatever was on Tab - Request
        // info by default - reading the race status out over the panel name, and Control+Shift+C
        // toggled transmission while reading the distance off the C bound to Report distance.
        //
        // Which keys are spoken for is asked of the shortcut catalog rather than listed here, so
        // remapping a shortcut moves the exemption with it and adding one needs no change in this file.
        //
        // What disqualifies a press is a binding matching *when the key went down*, not one matching
        // now. Those are different situations. Holding the throttle and then reaching for Control+Tab
        // must not cut the throttle - the throttle key went down on its own, and it keeps its intent
        // until it is released.
        //
        // None of this is macOS-specific despite the branch it arrived on: Input/Drive is shared, so
        // this arbitration runs identically on Windows, Linux and macOS and needs testing on each.
        private readonly bool[] _pressedUnderModifier = new bool[256];

        private void UpdateModifierChordState()
        {
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
                _pressedUnderModifier[i] = !IsModifierKey(key) && IsClaimedByHeldShortcut(key);
            }
        }

        private bool WasPressedUnderModifier(Key key)
        {
            var index = (int)key;
            if (index < 0 || index >= _pressedUnderModifier.Length)
                return false;
            return _pressedUnderModifier[index];
        }

        // ModifierKeys covers the left, right and Both variants of shift, control and alt; spelling the
        // list out again here is how the Both keys got left off it, and an intent bound to Both Control
        // or Both Alt then never fired at all - IsKeyDown reports those down whenever either side is.
        private static bool IsModifierKey(Key key)
        {
            return ModifierKeys.TryGetGroup(key, out _) ||
                   key == Key.LeftWindowsKey || key == Key.RightWindowsKey;
        }
    }
}
