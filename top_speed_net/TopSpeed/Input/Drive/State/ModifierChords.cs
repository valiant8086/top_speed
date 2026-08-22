using Key = TopSpeed.Input.InputKey;

namespace TopSpeed.Input
{
    internal sealed partial class DriveInput
    {
        // Drive intents bind to plain keys and the bindings carry no modifiers, so without this a chord
        // fires its own action and the plain-key intent underneath it at the same time. Control+Tab
        // switches vehicle panels and also fired whatever is on Tab - Request info by default - reading
        // the race status out over the panel name. Nothing about that is specific to Tab: bind an intent
        // to O or F and any Control+O or Control+F the game grows later would do the same, which is why
        // the keys a chord can claim are listed in IsChordKey rather than inferred.
        //
        // What disqualifies a press is a modifier being held *when the key went down*, not one being
        // held now. Those are different situations and only the first is a chord. Holding the throttle
        // and then reaching for Control+Tab must not cut the throttle - the throttle key went down on
        // its own, and it keeps its intent until it is released.
        //
        // None of this is macOS-specific despite the branch it arrived on: Input/Drive is shared, so
        // this arbitration runs identically on Windows, Linux and macOS and needs testing on each.
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
                // Two ways a press can belong to something else: a hardcoded chord in Query.cs, or a
                // registered shortcut whose modifiers are held. The second is why Control+Shift+C used
                // to toggle transmission and read the distance off the C underneath it - shortcuts and
                // drive intents are separate readers of the same keyboard and neither knew about the
                // other. IsClaimedByHeldShortcut checks the binding's own modifiers, so it needs no
                // modifierHeld gate of its own.
                _pressedUnderModifier[i] = !IsModifierKey(key)
                    && ((modifierHeld && IsChordKey(key)) || IsClaimedByHeldShortcut(key));
            }
        }

        // Only a key a chord can actually claim is worth disqualifying. Disqualifying every key instead
        // meant that holding a modifier which is itself bound to a drive intent - the horn on Both
        // Control, say - left every other control on the keyboard inert for as long as it was held: no
        // gear changes, no throttle, nothing. Keep this in step with the chord queries in Query.cs.
        private static bool IsChordKey(Key key)
        {
            return key == Key.Tab;
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
