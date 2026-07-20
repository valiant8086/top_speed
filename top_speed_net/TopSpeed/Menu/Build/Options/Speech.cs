using System.Collections.Generic;
using TopSpeed.Input;
using TopSpeed.Localization;
using TopSpeed.Speech;
using TopSpeed.Speech.Prism;

namespace TopSpeed.Menu
{
    internal sealed partial class MenuRegistry
    {
        private MenuScreen BuildOptionsSpeechSettingsMenu()
        {
            return BackMenu("options_speech", BuildSpeechItems());
        }

        internal void RefreshSpeechSettingsMenu()
        {
            _menu.UpdateItems("options_speech", BuildSpeechItems(), preserveSelection: true);
        }

        private MenuItem BuildSpeechBackendItem()
        {
            var backends = GetSelectableSpeechBackends();
            var hintProvider = HintAdjustProvider(LocalizationService.Mark("Choose which speech backend to prefer. Automatic lets Prism pick the best available backend."));

            if (backends.Count == 0)
            {
                return new MenuItem(
                    () => LocalizationService.Format(
                        LocalizationService.Mark("Speech backend: {0}"),
                        LocalizationService.Translate(LocalizationService.Mark("automatic"))),
                    MenuAction.None,
                    hintProvider: hintProvider);
            }

            var values = new List<string>(backends.Count + 1)
            {
                LocalizationService.Mark("automatic")
            };

            for (var i = 0; i < backends.Count; i++)
                values.Add(backends[i].Name);

            return new RadioButton(
                LocalizationService.Mark("Speech backend"),
                values,
                () => GetSpeechBackendIndex(backends),
                value => _settingsActions.SetSpeechBackend(value == 0 ? null : backends[value - 1].Id),
                hintProvider: hintProvider);
        }

        private List<MenuItem> BuildSpeechItems()
        {
            var items = new List<MenuItem>
            {
                BuildSpeechBackendItem(),
                new CheckBox(
                    LocalizationService.Mark("Interrupt screen reader speech"),
                    () => _settings.ScreenReaderInterrupt,
                    value => _settingsActions.SetScreenReaderInterrupt(value),
                    hintProvider: HintToggleProvider(LocalizationService.Mark("When checked, new spoken messages interrupt the current screen reader speech. Menu titles and usage hints are not affected."))),
                BuildSpeechModeItem()
            };

            if (SupportsVoiceSelection())
                items.Add(BuildSpeechVoiceItem());

            if (SupportsRateSelection())
                items.Add(BuildSpeechRateItem());

            items.Add(new MenuItem(
                LocalizationService.Mark("Recalibrate screen reader rate"),
                MenuAction.None,
                onActivate: _settingsActions.RecalibrateScreenReaderRate,
                hintProvider: HintStartProvider(LocalizationService.Mark("Measure your screen-reader speaking speed again so usage hints and delayed speech timings stay accurate."))));
            return items;
        }

        private List<SpeechBackendInfo> GetSelectableSpeechBackends()
        {
            var source = _settingsActions.GetSpeechBackends();
            var result = new List<SpeechBackendInfo>();
            for (var i = 0; i < source.Count; i++)
            {
                var backend = source[i];
                if (!backend.IsSupported || string.IsNullOrWhiteSpace(backend.Name) || backend.Id == Ids.Uia)
                    continue;

                result.Add(backend);
            }

            return result;
        }

        private MenuItem BuildSpeechModeItem()
        {
            return new RadioButton(
                LocalizationService.Mark("Speech mode"),
                new[]
                {
                    LocalizationService.Mark("speech only"),
                    LocalizationService.Mark("braille"),
                    LocalizationService.Mark("speech with braille")
                },
                () => (int)_settings.SpeechMode,
                value => _settingsActions.SetSpeechMode((SpeechOutputMode)value),
                hintProvider: HintAdjustProvider(LocalizationService.Mark("Choose whether spoken messages use speech only, braille only, or speech with braille.")));
        }

        private MenuItem BuildSpeechVoiceItem()
        {
            var voices = _settingsActions.GetSpeechVoices();

            var values = new List<string>(voices.Count + 1)
            {
                LocalizationService.Mark("default")
            };
            for (var i = 0; i < voices.Count; i++)
                values.Add(FormatVoiceLabel(voices[i]));

            var backendName = _settingsActions.GetActiveSpeechBackendName();
            var title = string.IsNullOrWhiteSpace(backendName)
                ? LocalizationService.Mark("Voice")
                : LocalizationService.Format(LocalizationService.Mark("Voice for {0}"), backendName);

            return new RadioButton(
                title,
                values,
                () => GetSpeechVoiceIndex(voices),
                value => _settingsActions.SetSpeechVoice(value == 0 ? null : voices[value - 1].Name),
                hintProvider: HintAdjustProvider(LocalizationService.Mark("Select which voice the current speech backend should use. Default uses the backend's own default voice.")));
        }

        private MenuItem BuildSpeechRateItem()
        {
            var backendName = _settingsActions.GetActiveSpeechBackendName();
            var title = string.IsNullOrWhiteSpace(backendName)
                ? LocalizationService.Mark("Speech rate")
                : LocalizationService.Format(LocalizationService.Mark("Speech rate for {0}"), backendName);

            return new Slider(
                title,
                "0-100",
                () => (int)System.Math.Round(GetSpeechRateForActiveBackend() * 100f),
                value => _settingsActions.SetSpeechRate(value / 100f),
                hintProvider: HintSliderProvider(LocalizationService.Mark("Adjust the speech rate for the current backend.")));
        }

        private float GetSpeechRateForActiveBackend()
        {
            var activeId = _settingsActions.GetActiveSpeechBackendId();
            if (activeId.HasValue && _settings.SpeechRatesByBackend.TryGetValue(activeId.Value, out var rate))
                return rate;

            return DriveSettings.DefaultSpeechRate;
        }

        private bool SupportsVoiceSelection()
        {
            var capabilities = _settingsActions.GetSpeechCapabilities();
            if ((capabilities & SpeechCapabilities.SetVoice) != SpeechCapabilities.SetVoice)
                return false;

            return _settingsActions.GetSpeechVoices().Count > 0;
        }

        private bool SupportsRateSelection()
        {
            var capabilities = _settingsActions.GetSpeechCapabilities();
            return (capabilities & SpeechCapabilities.SetRate) == SpeechCapabilities.SetRate;
        }

        private int GetSpeechVoiceIndex(IReadOnlyList<SpeechVoiceInfo> voices)
        {
            var activeId = _settingsActions.GetActiveSpeechBackendId();
            if (!activeId.HasValue
                || !_settings.SpeechVoicesByBackend.TryGetValue(activeId.Value, out var name)
                || string.IsNullOrEmpty(name))
                return 0;

            for (var i = 0; i < voices.Count; i++)
            {
                if (string.Equals(voices[i].Name, name, System.StringComparison.OrdinalIgnoreCase))
                    return i + 1;
            }

            return 0;
        }

        private static string FormatVoiceLabel(SpeechVoiceInfo voice)
        {
            if (string.IsNullOrWhiteSpace(voice.Language))
                return voice.Name;

            return $"{voice.Name} ({voice.Language})";
        }

        private int GetSpeechBackendIndex(IReadOnlyList<SpeechBackendInfo> backends)
        {
            if (!_settings.SpeechBackendId.HasValue)
                return 0;

            for (var i = 0; i < backends.Count; i++)
            {
                if (backends[i].Id == _settings.SpeechBackendId.Value)
                    return i + 1;
            }

            return 0;
        }
    }
}
