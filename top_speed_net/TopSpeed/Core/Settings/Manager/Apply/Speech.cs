using System.Collections.Generic;
using TopSpeed.Input;
using TopSpeed.Localization;
using TopSpeed.Speech.Prism;

namespace TopSpeed.Core.Settings
{
    internal sealed partial class SettingsManager
    {
        private static void ApplySpeech(DriveSettings settings, SettingsSpeechDocument speech, List<SettingsIssue> issues)
        {
            settings.SpeechMode = ReadEnum(speech.Mode, settings.SpeechMode, "speech.mode", issues);

            if (speech.Backend.HasValue)
            {
                settings.SpeechBackendId = speech.Backend.Value == Ids.Invalid
                    ? null
                    : speech.Backend.Value;
            }

            if (speech.Voice != null)
                settings.SpeechVoiceName = string.IsNullOrWhiteSpace(speech.Voice) ? null : speech.Voice.Trim();

            if (speech.Rate.HasValue)
            {
                var rate = (float)speech.Rate.Value;
                settings.SpeechRate = ClampFloat(rate, 0f, 1f, "speech.rate", issues);
            }

            if (speech.Interrupt.HasValue)
                settings.ScreenReaderInterrupt = speech.Interrupt.Value;

            if (!speech.ScreenReaderRateMs.HasValue)
                return;

            var value = (float)speech.ScreenReaderRateMs.Value;
            if (!float.IsNaN(value) && !float.IsInfinity(value))
            {
                settings.ScreenReaderRateMs = ClampFloat(value, 0f, float.MaxValue, "speech.screenReaderRateMs", issues);
            }
            else
            {
                issues.Add(new SettingsIssue(
                    SettingsIssueSeverity.Warning,
                    "speech.screenReaderRateMs",
                    LocalizationService.Mark("Screen reader rate is not a valid number and was reset to default.")));
            }
        }

        private static void ApplyRadio(DriveSettings settings, SettingsRadioDocument radio, List<SettingsIssue> issues)
        {
            if (radio.LastFolder != null)
                settings.RadioLastFolder = radio.LastFolder.Trim();

            if (radio.ShuffleEnabled.HasValue)
                settings.RadioShuffle = radio.ShuffleEnabled.Value;
        }
    }
}

