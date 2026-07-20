using System.Collections.Generic;
using TopSpeed.Input;
using TopSpeed.Localization;

namespace TopSpeed.Core.Settings
{
    internal sealed partial class SettingsManager
    {
        private static void ApplyAudio(DriveSettings settings, SettingsAudioDocument audio, List<SettingsIssue> issues)
        {
            settings.AudioVolumes ??= new AudioVolumeSettings();
            var hasCategoryVolumes = false;

            if (audio.MasterVolumePercent.HasValue)
            {
                settings.AudioVolumes.MasterPercent = ClampPercent(audio.MasterVolumePercent.Value, "audio.masterVolumePercent", issues);
                hasCategoryVolumes = true;
            }

            if (audio.PlayerVehicleEnginePercent.HasValue)
            {
                settings.AudioVolumes.PlayerVehicleEnginePercent = ClampPercent(audio.PlayerVehicleEnginePercent.Value, "audio.playerVehicleEnginePercent", issues);
                hasCategoryVolumes = true;
            }

            if (audio.PlayerVehicleEventsPercent.HasValue)
            {
                settings.AudioVolumes.PlayerVehicleEventsPercent = ClampPercent(audio.PlayerVehicleEventsPercent.Value, "audio.playerVehicleEventsPercent", issues);
                hasCategoryVolumes = true;
            }

            if (audio.OtherVehicleEnginePercent.HasValue)
            {
                settings.AudioVolumes.OtherVehicleEnginePercent = ClampPercent(audio.OtherVehicleEnginePercent.Value, "audio.otherVehicleEnginePercent", issues);
                hasCategoryVolumes = true;
            }

            if (audio.OtherVehicleEventsPercent.HasValue)
            {
                settings.AudioVolumes.OtherVehicleEventsPercent = ClampPercent(audio.OtherVehicleEventsPercent.Value, "audio.otherVehicleEventsPercent", issues);
                hasCategoryVolumes = true;
            }

            if (audio.SurfaceLoopsPercent.HasValue)
            {
                settings.AudioVolumes.SurfaceLoopsPercent = ClampPercent(audio.SurfaceLoopsPercent.Value, "audio.surfaceLoopsPercent", issues);
                hasCategoryVolumes = true;
            }

            if (audio.RadioPercent.HasValue)
            {
                settings.AudioVolumes.RadioPercent = ClampPercent(audio.RadioPercent.Value, "audio.radioPercent", issues);
                hasCategoryVolumes = true;
            }

            if (audio.AmbientsAndSourcesPercent.HasValue)
            {
                settings.AudioVolumes.AmbientsAndSourcesPercent = ClampPercent(audio.AmbientsAndSourcesPercent.Value, "audio.ambientsAndSourcesPercent", issues);
                hasCategoryVolumes = true;
            }

            if (audio.MusicPercent.HasValue)
            {
                settings.AudioVolumes.MusicPercent = ClampPercent(audio.MusicPercent.Value, "audio.musicPercent", issues);
                hasCategoryVolumes = true;
            }

            if (audio.OnlineServerEventsPercent.HasValue)
            {
                settings.AudioVolumes.OnlineServerEventsPercent = ClampPercent(audio.OnlineServerEventsPercent.Value, "audio.onlineServerEventsPercent", issues);
                hasCategoryVolumes = true;
            }

            if (audio.CommunicatorPercent.HasValue)
            {
                settings.AudioVolumes.CommunicatorPercent = ClampPercent(audio.CommunicatorPercent.Value, "audio.communicatorPercent", issues);
                hasCategoryVolumes = true;
            }

            if (audio.TextToSpeechPercent.HasValue)
            {
                settings.AudioVolumes.TextToSpeechPercent = ClampPercent(audio.TextToSpeechPercent.Value, "audio.textToSpeechPercent", issues);
                hasCategoryVolumes = true;
            }

            if (audio.HrtfAudio.HasValue)
                settings.HrtfAudio = audio.HrtfAudio.Value;

            if (audio.StereoWidening.HasValue)
                settings.StereoWidening = audio.StereoWidening.Value;

            if (audio.AutoDetectAudioDeviceFormat.HasValue)
                settings.AutoDetectAudioDeviceFormat = audio.AutoDetectAudioDeviceFormat.Value;
            if (audio.VoiceInputDevice != null)
                settings.VoiceInputDeviceName = audio.VoiceInputDevice.Trim();
            if (audio.VoiceInputGainPercent.HasValue)
            {
                settings.VoiceInputGainPercent = ClampInt(
                    audio.VoiceInputGainPercent.Value,
                    settings.VoiceInputGainPercent,
                    DriveSettings.MinVoiceInputGainPercent,
                    DriveSettings.MaxVoiceInputGainPercent,
                    "audio.voiceInputGainPercent",
                    issues);
            }

            if (hasCategoryVolumes)
            {
                settings.AudioVolumes.ClampAll();
                settings.SyncMusicVolumeFromAudioCategories();
            }
            else if (audio.MusicVolume.HasValue)
            {
                var value = (float)audio.MusicVolume.Value;
                if (!float.IsNaN(value) && !float.IsInfinity(value))
                {
                    settings.MusicVolume = ClampFloat(value, 0f, 1f, "audio.musicVolume", issues);
                    settings.SyncAudioCategoriesFromMusicVolume();
                }
                else
                {
                    issues.Add(new SettingsIssue(
                        SettingsIssueSeverity.Warning,
                        "audio.musicVolume",
                        LocalizationService.Mark("Music volume is not a valid number and was reset to default.")));
                }
            }
        }
    }
}


