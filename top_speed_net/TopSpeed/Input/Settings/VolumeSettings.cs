using System;

namespace TopSpeed.Input
{
    internal enum AudioVolumeCategory
    {
        Master,
        PlayerVehicleEngine,
        PlayerVehicleEvents,
        OtherVehicleEngine,
        OtherVehicleEvents,
        SurfaceLoops,
        Radio,
        AmbientsAndSources,
        Music,
        OnlineServerEvents,
        Communicator,
        // Appended deliberately: the F6 category list and its index are ordinal,
        // so inserting earlier would move every existing category under the user.
        TextToSpeech
    }

    internal sealed class AudioVolumeSettings
    {
        public int MasterPercent { get; set; }
        public int PlayerVehicleEnginePercent { get; set; }
        public int PlayerVehicleEventsPercent { get; set; }
        public int OtherVehicleEnginePercent { get; set; }
        public int OtherVehicleEventsPercent { get; set; }
        public int SurfaceLoopsPercent { get; set; }
        public int RadioPercent { get; set; }
        public int AmbientsAndSourcesPercent { get; set; }
        public int MusicPercent { get; set; }
        public int OnlineServerEventsPercent { get; set; }
        public int CommunicatorPercent { get; set; }
        public int TextToSpeechPercent { get; set; }

        public void RestoreDefaults(int defaultMusicPercent)
        {
            MasterPercent = 100;
            PlayerVehicleEnginePercent = 100;
            PlayerVehicleEventsPercent = 100;
            OtherVehicleEnginePercent = 80;
            OtherVehicleEventsPercent = 100;
            SurfaceLoopsPercent = 70;
            RadioPercent = 100;
            AmbientsAndSourcesPercent = 100;
            MusicPercent = ClampPercent(defaultMusicPercent);
            OnlineServerEventsPercent = 100;
            CommunicatorPercent = 100;
            TextToSpeechPercent = 100;
        }

        public void ClampAll()
        {
            MasterPercent = ClampPercent(MasterPercent);
            PlayerVehicleEnginePercent = ClampPercent(PlayerVehicleEnginePercent);
            PlayerVehicleEventsPercent = ClampPercent(PlayerVehicleEventsPercent);
            OtherVehicleEnginePercent = ClampPercent(OtherVehicleEnginePercent);
            OtherVehicleEventsPercent = ClampPercent(OtherVehicleEventsPercent);
            SurfaceLoopsPercent = ClampPercent(SurfaceLoopsPercent);
            RadioPercent = ClampPercent(RadioPercent);
            AmbientsAndSourcesPercent = ClampPercent(AmbientsAndSourcesPercent);
            MusicPercent = ClampPercent(MusicPercent);
            OnlineServerEventsPercent = ClampPercent(OnlineServerEventsPercent);
            CommunicatorPercent = ClampPercent(CommunicatorPercent);
            TextToSpeechPercent = ClampPercent(TextToSpeechPercent);
        }

        public int GetPercent(AudioVolumeCategory category)
        {
            return category switch
            {
                AudioVolumeCategory.Master => MasterPercent,
                AudioVolumeCategory.PlayerVehicleEngine => PlayerVehicleEnginePercent,
                AudioVolumeCategory.PlayerVehicleEvents => PlayerVehicleEventsPercent,
                AudioVolumeCategory.OtherVehicleEngine => OtherVehicleEnginePercent,
                AudioVolumeCategory.OtherVehicleEvents => OtherVehicleEventsPercent,
                AudioVolumeCategory.SurfaceLoops => SurfaceLoopsPercent,
                AudioVolumeCategory.Radio => RadioPercent,
                AudioVolumeCategory.AmbientsAndSources => AmbientsAndSourcesPercent,
                AudioVolumeCategory.Music => MusicPercent,
                AudioVolumeCategory.OnlineServerEvents => OnlineServerEventsPercent,
                AudioVolumeCategory.Communicator => CommunicatorPercent,
                AudioVolumeCategory.TextToSpeech => TextToSpeechPercent,
                _ => 100
            };
        }

        public static int ClampPercent(int percent)
        {
            if (percent < 0)
                return 0;
            if (percent > 100)
                return 100;
            return percent;
        }

        public static float PercentToScalar(int percent)
        {
            return ClampPercent(percent) / 100f;
        }
    }

    internal static class AudioVolumeSettingsExtensions
    {
        public static float GetCategoryScalar(this DriveSettings settings, AudioVolumeCategory category)
        {
            if (settings == null)
                return 1f;
            return AudioVolumeSettings.PercentToScalar(settings.AudioVolumes.GetPercent(category));
        }

        public static float GetEffectiveScalar(this DriveSettings settings, AudioVolumeCategory category)
        {
            if (settings == null)
                return 1f;
            return settings.GetCategoryScalar(AudioVolumeCategory.Master) * settings.GetCategoryScalar(category);
        }
    }
}


