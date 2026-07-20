using System;
using System.Collections.Generic;
using SoundFlow.Backends.MiniAudio;
using TopSpeed.Data;
using TopSpeed.Input;
using TopSpeed.Localization;

namespace TopSpeed.Game
{
    internal sealed partial class Game
    {
        private void SaveMusicVolume(float volume)
        {
            _settings.MusicVolume = volume;
            _settings.SyncAudioCategoriesFromMusicVolume();
            ApplyAudioSettings();
            SaveSettings();
        }

        private void ApplyAudioSettings()
        {
            _settings.AudioVolumes ??= new AudioVolumeSettings();
            _settings.SyncMusicVolumeFromAudioCategories();
            _audio.SetMasterVolume(_settings.GetCategoryScalar(AudioVolumeCategory.Master));
            ApplyTextToSpeechVolume();
            _menu.SetMenuMusicVolume(_settings.MusicVolume);
        }

        // Covers text to speech only, not the game's recorded speech. Deliberately
        // not multiplied by Master: it renders to its own output that Master does
        // not reach, and for a screen-reader-driven game silencing speech via Master
        // would leave the player unable to hear their way back. Applied to both
        // paths, since which one is in use depends on the active backend: the game's
        // speech output for backends we render ourselves (SAPI, OneCore), and the
        // backend itself for ones that voice directly. Each is harmless when it is
        // not the active path.
        private void ApplyTextToSpeechVolume()
        {
            var scalar = _settings.GetCategoryScalar(AudioVolumeCategory.TextToSpeech);
            _audio.SetSpeechVolume(scalar);
            _speech.SetBackendVolume(scalar);
        }

        private string GetVoiceInputDeviceLabel()
        {
            return string.IsNullOrWhiteSpace(_settings.VoiceInputDeviceName)
                ? LocalizationService.Mark("Automatic")
                : _settings.VoiceInputDeviceName;
        }

        private void ChooseVoiceInputDevice()
        {
            var devices = EnumerateVoiceInputDevices();
            var items = new Dictionary<int, string>
            {
                [1] = LocalizationService.Mark("Automatic")
            };

            var valuesByChoiceId = new Dictionary<int, string>
            {
                [1] = string.Empty
            };

            var choiceId = 2;
            for (var i = 0; i < devices.Count; i++)
            {
                var deviceName = devices[i];
                if (string.IsNullOrWhiteSpace(deviceName))
                    continue;

                items[choiceId] = deviceName;
                valuesByChoiceId[choiceId] = deviceName;
                choiceId++;
            }

            var cancelLabel = LocalizationService.Mark("Cancel");
            ShowChoiceDialog(
                LocalizationService.Mark("Select voice input device"),
                LocalizationService.Mark("Choose the microphone used for communicator voice chat."),
                items,
                cancelable: true,
                cancelLabel,
                result =>
                {
                    if (result.IsCanceled)
                        return;
                    if (!valuesByChoiceId.TryGetValue(result.ChoiceId, out var selected))
                        return;

                    _settings.VoiceInputDeviceName = selected;
                    SaveSettings();
                    _speech.Speak(string.IsNullOrWhiteSpace(selected)
                        ? LocalizationService.Mark("Voice input set to automatic.")
                        : LocalizationService.Format(
                            LocalizationService.Mark("Voice input set to {0}."),
                            selected));
                });
        }

        private static IReadOnlyList<string> EnumerateVoiceInputDevices()
        {
            try
            {
                using var engine = new MiniAudioEngine();
                engine.UpdateAudioDevicesInfo();
                var names = new List<string>(engine.CaptureDevices.Length);
                for (var i = 0; i < engine.CaptureDevices.Length; i++)
                {
                    var name = engine.CaptureDevices[i].Name;
                    if (string.IsNullOrWhiteSpace(name))
                        continue;
                    names.Add(name);
                }

                names.Sort(StringComparer.OrdinalIgnoreCase);
                return names;
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }
}

