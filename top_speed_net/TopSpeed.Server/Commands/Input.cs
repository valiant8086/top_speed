using System;
using System.Collections.Generic;
using System.Globalization;
using TopSpeed.Server.Logging;

using TopSpeed.Localization;
namespace TopSpeed.Server.Commands
{
    internal static class CommandInput
    {
        public static bool TryReadLine(string? prompt, out string value)
        {
            value = string.Empty;
            if (!string.IsNullOrWhiteSpace(prompt))
                ConsoleSink.WriteLine(prompt);

            // Reads from whichever session is active rather than from the console directly,
            // so every command and menu below works unchanged over a control connection.
            return CommandSessions.TryReadLine(out value);
        }

        public static bool TryPromptMenuChoice(
            string title,
            IReadOnlyList<string> options,
            out int selectedIndex,
            int backOptionIndex = -1)
        {
            selectedIndex = -1;
            if (options == null || options.Count == 0)
                return false;
            if (backOptionIndex < -1 || backOptionIndex >= options.Count)
                return false;

            ConsoleSink.WriteLine(title);

            var menuToIndex = new Dictionary<int, int>();
            var displayNumber = 1;
            for (var i = 0; i < options.Count; i++)
            {
                if (i == backOptionIndex)
                    continue;

                ConsoleSink.WriteLine(displayNumber.ToString(CultureInfo.InvariantCulture) + ". " + options[i]);
                menuToIndex[displayNumber] = i;
                displayNumber++;
            }

            if (backOptionIndex >= 0)
                ConsoleSink.WriteLine("0. " + options[backOptionIndex]);

            while (true)
            {
                if (!TryReadLine(LocalizationService.Mark("Enter option number:"), out var raw))
                    return false;

                if (!int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var menuNumber))
                {
                    ConsoleSink.WriteLine(LocalizationService.Mark("Invalid input. Enter a valid number."));
                    continue;
                }

                if (backOptionIndex >= 0 && menuNumber == 0)
                {
                    selectedIndex = backOptionIndex;
                    return true;
                }

                if (!menuToIndex.TryGetValue(menuNumber, out var index))
                {
                    ConsoleSink.WriteLine(LocalizationService.Mark("Invalid option number."));
                    continue;
                }

                selectedIndex = index;
                return true;
            }
        }

        public static bool TryPromptInt(string prompt, int min, int max, out int value)
        {
            value = 0;
            while (true)
            {
                if (!TryReadLine(prompt, out var raw))
                    return false;

                if (!int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    ConsoleSink.WriteLine(LocalizationService.Mark("Invalid input. Enter a valid number."));
                    continue;
                }

                if (parsed < min || parsed > max)
                {
                    ConsoleSink.WriteLineFormat(LocalizationService.Mark("Value must be between {0} and {1}."), min, max);
                    continue;
                }

                value = parsed;
                return true;
            }
        }

        public static bool TryPromptText(string prompt, int maxLength, bool allowEmpty, out string value)
        {
            value = string.Empty;
            while (true)
            {
                if (!TryReadLine(prompt, out var raw))
                    return false;

                var text = raw.Trim();
                if (!allowEmpty && text.Length == 0)
                {
                    ConsoleSink.WriteLine(LocalizationService.Mark("Value cannot be empty."));
                    continue;
                }

                if (text.Length > maxLength)
                {
                    ConsoleSink.WriteLineFormat(LocalizationService.Mark("Value is too long. Maximum length is {0} characters."), maxLength);
                    continue;
                }

                value = text;
                return true;
            }
        }

        public static string FormatOnOff(bool value)
        {
            return value
                ? LocalizationService.Translate(LocalizationService.Mark("on"))
                : LocalizationService.Translate(LocalizationService.Mark("off"));
        }
    }
}




