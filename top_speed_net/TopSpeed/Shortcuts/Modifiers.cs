using System;
using Key = TopSpeed.Input.InputKey;
using TopSpeed.Input;

namespace TopSpeed.Shortcuts
{
    internal readonly struct ShortcutModifiers : IEquatable<ShortcutModifiers>
    {
        public ShortcutModifiers(bool shift, bool control, bool alt)
        {
            Shift = shift;
            Control = control;
            Alt = alt;
        }

        public bool Shift { get; }
        public bool Control { get; }
        public bool Alt { get; }

        public bool IsEmpty => !Shift && !Control && !Alt;

        // How many modifiers this binding asks for. Used to pick the most specific match when several
        // bindings on one key are satisfied at once.
        public int Count => (Shift ? 1 : 0) + (Control ? 1 : 0) + (Alt ? 1 : 0);

        // Satisfied when everything this binding asks for is held, whether or not anything else is.
        // Demanding equality instead meant an unrelated modifier being down blocked the binding
        // entirely: holding a modifier bound to a game control - the horn, or a throttle - made every
        // shortcut that asks for no modifiers unreachable for as long as it was held. Whether a held
        // extra actually means something is decided by which binding is most specific, not here.
        public bool IsSatisfiedBy(IInputService input)
        {
            if (input == null)
                return false;
            if (Shift && !IsModifierDown(input, ModifierKeyGroup.Shift))
                return false;
            if (Control && !IsModifierDown(input, ModifierKeyGroup.Control))
                return false;
            if (Alt && !IsModifierDown(input, ModifierKeyGroup.Alt))
                return false;

            return true;
        }

        public static ShortcutModifiers None => default;

        public static ShortcutModifiers FromInput(IInputService input)
        {
            if (input == null)
                return None;

            return new ShortcutModifiers(
                IsModifierDown(input, ModifierKeyGroup.Shift),
                IsModifierDown(input, ModifierKeyGroup.Control),
                IsModifierDown(input, ModifierKeyGroup.Alt));
        }

        public bool Equals(ShortcutModifiers other)
        {
            return Shift == other.Shift
                && Control == other.Control
                && Alt == other.Alt;
        }

        public override bool Equals(object? obj)
        {
            return obj is ShortcutModifiers other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Shift ? 1 : 0;
                hash = (hash * 397) ^ (Control ? 1 : 0);
                hash = (hash * 397) ^ (Alt ? 1 : 0);
                return hash;
            }
        }

        private static bool IsModifierDown(IInputService input, ModifierKeyGroup group)
        {
            var left = input.IsDown(ModifierKeys.GetLeftKey(group));
            var right = input.IsDown(ModifierKeys.GetRightKey(group));
            var both = input.IsDown(ModifierKeys.GetBothKey(group));
            return left || right || both;
        }
    }
}

