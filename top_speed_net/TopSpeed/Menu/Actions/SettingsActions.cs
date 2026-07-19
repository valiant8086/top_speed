using System;
using System.Collections.Generic;
using TopSpeed.Input;
using TopSpeed.Speech;

namespace TopSpeed.Menu
{
    internal interface IMenuSettingsActions
    {
        string GetLanguageName();
        IReadOnlyList<SpeechBackendInfo> GetSpeechBackends();
        IReadOnlyList<SpeechVoiceInfo> GetSpeechVoices();
        SpeechCapabilities GetSpeechCapabilities();
        void ChangeLanguage();
        void ShowRestoreDefaultsDialog();
        void RestoreDefaults();
        void RecalibrateScreenReaderRate();
        void CheckForUpdates();
        void ShowAboutDialog();
        void OpenGameGuide();
        void OpenTrackCreationGuide();
        void OpenVehicleCreationGuide();
        void OpenChangeLogFile();
        void ShowLatestChanges();
        void SetDevice(InputDeviceMode mode);
        void SetSpeechBackend(ulong? backendId);
        void SetScreenReaderInterrupt(bool enabled);
        void SetSpeechMode(SpeechOutputMode mode);
        void SetSpeechVoice(string? voiceName);
        void SetSpeechRate(float rate);
        void SetUseUpdateProxy(bool enabled);
        void EditUpdateProxyUrl();
        void UpdateSetting(Action update);
    }
}

