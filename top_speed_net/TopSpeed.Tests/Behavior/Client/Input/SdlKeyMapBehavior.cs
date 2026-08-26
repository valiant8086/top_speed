using TopSpeed.Input;
using TopSpeed.Input.Devices.Keyboard.Backends.Sdl;
using TopSpeed.Menu;
using TS.Sdl.Input;
using Xunit;

namespace TopSpeed.Tests;

[Trait("Category", "Behavior")]
public sealed class SdlKeyMapBehaviorTests
{
    // Asking whether a key is down starts by turning it back into a scancode, so a key the reverse
    // direction cannot name is a key the game never sees pressed. Letters and digits were missing,
    // which silently took out first-letter and digit navigation in every menu on Linux and macOS.
    [Fact]
    public void EveryKeyTheForwardMapProduces_TurnsBackIntoAScancode()
    {
        for (var i = 0; i < (int)Scancode.Count; i++)
        {
            var scancode = (Scancode)i;
            if (!scancode.TryToInputKey(out var key))
                continue;

            key.TryToScancode(out var back).Should().BeTrue(
                $"{key} is reachable from {scancode} and must map back");
            back.TryToInputKey(out var again).Should().BeTrue();
            again.Should().Be(key);
        }
    }

    [Fact]
    public void LettersAndDigits_MapBackToTheMainKeyboard()
    {
        AssertMapsBack(InputKey.A, Scancode.A);
        AssertMapsBack(InputKey.M, Scancode.M);
        AssertMapsBack(InputKey.Z, Scancode.Z);
        AssertMapsBack(InputKey.D1, Scancode.Alpha1);
        AssertMapsBack(InputKey.D0, Scancode.Alpha0);
    }

    private static void AssertMapsBack(InputKey key, Scancode expected)
    {
        key.TryToScancode(out var code).Should().BeTrue($"{key} must map back to a scancode");
        code.Should().Be(expected);
    }

    // The round trip above only covers keys the outward table already knows. A key missing from it
    // altogether is invisible rather than wrong: it answers "not pressed" forever, on Linux and
    // macOS only, while Windows keeps working because InputKey is DirectInput's own numbering and
    // needs no table. That is how first-letter and digit navigation went missing without a word.
    // So every key the game asks about by name has to be reachable here.
    [Fact]
    public void EveryKeyTheMenusAskAbout_IsReachable()
    {
        for (var c = 'A'; c <= 'Z'; c++)
        {
            MenuInputUtil.TryGetLetterKey(c, out var letter).Should().BeTrue($"the menus ask about {c}");
            letter.TryToScancode(out _).Should().BeTrue(
                $"first-letter navigation presses {c}, so SDL must be able to see it");
        }

        // Named one by one rather than counted off from D1 and NumberPad1: InputKey carries
        // DirectInput's numbering, where the keypad digits are not consecutive - Subtract and Add
        // sit among them - so arithmetic lands on values that are not keys at all.
        var row = new[]
        {
            InputKey.D1, InputKey.D2, InputKey.D3, InputKey.D4, InputKey.D5,
            InputKey.D6, InputKey.D7, InputKey.D8, InputKey.D9,
        };
        var pad = new[]
        {
            InputKey.NumberPad1, InputKey.NumberPad2, InputKey.NumberPad3,
            InputKey.NumberPad4, InputKey.NumberPad5, InputKey.NumberPad6,
            InputKey.NumberPad7, InputKey.NumberPad8, InputKey.NumberPad9,
        };

        foreach (var key in row)
            key.TryToScancode(out _).Should().BeTrue($"digit navigation presses {key}");
        foreach (var key in pad)
            key.TryToScancode(out _).Should().BeTrue($"digit navigation presses {key}");
    }

    // The keys that move around a menu or drive the car. None of these is exotic, which is the
    // point: they are the ones whose absence would be felt immediately and blamed on anything but a
    // lookup table.
    [Fact]
    public void TheEverydayKeys_AreReachable()
    {
        var everyday = new[]
        {
            InputKey.Up, InputKey.Down, InputKey.Left, InputKey.Right,
            InputKey.Return, InputKey.Escape, InputKey.Space, InputKey.Tab, InputKey.Back,
            InputKey.LeftShift, InputKey.RightShift,
            InputKey.LeftControl, InputKey.RightControl,
            InputKey.LeftAlt, InputKey.RightAlt,
            InputKey.Home, InputKey.End, InputKey.PageUp, InputKey.PageDown,
            InputKey.Insert, InputKey.Delete,
            InputKey.Minus, InputKey.Equals, InputKey.Grave,
            InputKey.LeftBracket, InputKey.RightBracket,
            InputKey.Semicolon, InputKey.Apostrophe, InputKey.Comma, InputKey.Period,
            InputKey.Slash, InputKey.Backslash,
        };

        foreach (var key in everyday)
            key.TryToScancode(out _).Should().BeTrue($"{key} is an everyday key and must be visible");
    }

    // The number row and the keypad both produce digits, but they are separate keys and the menus
    // ask about each by name. Whichever way the table is built, the row must not be answered with a
    // keypad scancode.
    [Fact]
    public void NumberRowAndKeypad_StayDistinct()
    {
        InputKey.D7.TryToScancode(out var row).Should().BeTrue();
        InputKey.NumberPad7.TryToScancode(out var pad).Should().BeTrue();
        row.Should().Be(Scancode.Alpha7);
        pad.Should().NotBe(row);
    }
}
