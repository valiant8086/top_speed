using System;
using System.Collections.Generic;
using TopSpeed.Input;
using TopSpeed.Localization;

namespace TopSpeed.Menu
{
    internal sealed partial class MenuRegistry
    {
        private MenuScreen BuildOptionsVolumeSettingsMenu()
        {
            var items = new List<MenuItem>
            {
                BuildVolumeSlider(
                    LocalizationService.Mark("Master audio volume"),
                    () => _settings.AudioVolumes.MasterPercent,
                    value => _settings.AudioVolumes.MasterPercent = value,
                    LocalizationService.Mark("Controls the overall audio volume for the game. Set lower to reduce every sound category.")),
                BuildVolumeSlider(
                    LocalizationService.Mark("Vehicle engine sounds"),
                    () => _settings.AudioVolumes.PlayerVehicleEnginePercent,
                    value => _settings.AudioVolumes.PlayerVehicleEnginePercent = value,
                    LocalizationService.Mark("Controls your own engine and throttle sounds, including engine start and stop.")),
                BuildVolumeSlider(
                    LocalizationService.Mark("Vehicle event sounds"),
                    () => _settings.AudioVolumes.PlayerVehicleEventsPercent,
                    value => _settings.AudioVolumes.PlayerVehicleEventsPercent = value,
                    LocalizationService.Mark("Controls events related to your own vehicle, such as horn, back-fire, and other vehicle events.")),
                BuildVolumeSlider(
                    LocalizationService.Mark("Other vehicles engine sounds"),
                    () => _settings.AudioVolumes.OtherVehicleEnginePercent,
                    value => _settings.AudioVolumes.OtherVehicleEnginePercent = value,
                    LocalizationService.Mark("Controls engine-related sounds for bots and other players, including engine start and stop.")),
                BuildVolumeSlider(
                    LocalizationService.Mark("Other vehicles event sounds"),
                    () => _settings.AudioVolumes.OtherVehicleEventsPercent,
                    value => _settings.AudioVolumes.OtherVehicleEventsPercent = value,
                    LocalizationService.Mark("Controls horns, crashes, bumps, brakes, and similar event sounds for bots and other players.")),
                BuildVolumeSlider(
                    LocalizationService.Mark("Surface loop sounds"),
                    () => _settings.AudioVolumes.SurfaceLoopsPercent,
                    value => _settings.AudioVolumes.SurfaceLoopsPercent = value,
                    LocalizationService.Mark("Controls road and surface loops like asphalt, gravel, etc.")),
                BuildVolumeSlider(
                    LocalizationService.Mark("Radio volume"),
                    () => _settings.AudioVolumes.RadioPercent,
                    value => _settings.AudioVolumes.RadioPercent = value,
                    LocalizationService.Mark("Controls radio playback volume from other players only. Your own radio playback is not affected.")),
                BuildVolumeSlider(
                    LocalizationService.Mark("Ambients and sound sources"),
                    () => _settings.AudioVolumes.AmbientsAndSourcesPercent,
                    value => _settings.AudioVolumes.AmbientsAndSourcesPercent = value,
                    LocalizationService.Mark("Controls track ambients, weather loops, noise sounds, and custom track sound sources.")),
                BuildVolumeSlider(
                    LocalizationService.Mark("Music volume"),
                    () => _settings.AudioVolumes.MusicPercent,
                    value =>
                    {
                        _settings.AudioVolumes.MusicPercent = value;
                        _settings.SyncMusicVolumeFromAudioCategories();
                    },
                    LocalizationService.Mark("Controls menu and race music volume. This stays synchronized with the menu music volume setting.")),
                BuildVolumeSlider(
                    LocalizationService.Mark("Online server event sounds"),
                    () => _settings.AudioVolumes.OnlineServerEventsPercent,
                    value => _settings.AudioVolumes.OnlineServerEventsPercent = value,
                    LocalizationService.Mark("Controls server and multiplayer event sounds such as connection and other events.")),
                BuildVolumeSlider(
                    LocalizationService.Mark("Communicator volume"),
                    () => _settings.AudioVolumes.CommunicatorPercent,
                    value => _settings.AudioVolumes.CommunicatorPercent = value,
                    LocalizationService.Mark("Controls communicator activation cues and remote voice chat playback. Affects how loud other players sound when they speak through the communicator.")),
                BuildVolumeSlider(
                    LocalizationService.Mark("Text to speech volume"),
                    () => _settings.AudioVolumes.TextToSpeechPercent,
                    value => _settings.AudioVolumes.TextToSpeechPercent = value,
                    LocalizationService.Mark("Controls how loud text to speech is. Does not affect the game's recorded speech, or screen readers such as NVDA and JAWS, which speak at their own volume. Not reduced by master audio volume."))
            };

            return BackMenu("options_volume", items);
        }

        private Slider BuildVolumeSlider(string label, Func<int> getter, Action<int> setter, string hint)
        {
            return new Slider(
                label,
                "0-100",
                getter,
                value => _settingsActions.UpdateSetting(() =>
                {
                    _settings.AudioVolumes ??= new AudioVolumeSettings();
                    setter(value);
                    _settings.AudioVolumes.ClampAll();
                    _settings.SyncMusicVolumeFromAudioCategories();
                }),
                onChanged: _ => _audio.ApplyAudioSettings(),
                hintProvider: () => HintSlider(hint));
        }
    }
}

