using TopSpeed.Input;
using TopSpeed.Input.Devices.Keyboard.Backends.Sdl;
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
