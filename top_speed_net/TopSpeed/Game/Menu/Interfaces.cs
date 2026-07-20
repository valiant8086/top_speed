using System;
using System.Collections.Generic;
using TopSpeed.Core;
using TopSpeed.Data;
using TopSpeed.Input;
using TopSpeed.Menu;
using TopSpeed.Localization;
using TopSpeed.Speech;

namespace TopSpeed.Game
{
    internal sealed partial class Game
    {
        void IMenuAudioActions.SaveMusicVolume(float volume) => SaveMusicVolume(volume);
        void IMenuAudioActions.ApplyAudioSettings() => ApplyAudioSettings();
        string IMenuAudioActions.GetVoiceInputDeviceLabel() => GetVoiceInputDeviceLabel();
        void IMenuAudioActions.ChooseVoiceInputDevice() => ChooseVoiceInputDevice();

        void IMenuDriveActions.QueueDriveStart(DriveMode mode) => QueueDriveStart(mode);

        void IMenuServerActions.StartServerDiscovery() => _multiplayerCoordinator.StartServerDiscovery();
        void IMenuServerActions.OpenSavedServersManager() => _multiplayerCoordinator.OpenSavedServersManager();
        void IMenuServerActions.BeginManualServerEntry() => _multiplayerCoordinator.BeginManualServerEntry();
        void IMenuServerActions.BeginServerPortEntry() => _multiplayerCoordinator.BeginServerPortEntry();
        void IMenuServerActions.BeginDefaultCallSignEntry() => _multiplayerCoordinator.BeginDefaultCallSignEntry();
        void IMenuServerActions.NextChatCategory() => _multiplayerCoordinator.NextChatCategory();
        void IMenuServerActions.PreviousChatCategory() => _multiplayerCoordinator.PreviousChatCategory();

        void IMenuUiActions.SpeakMessage(string text) => _speech.Speak(text);
        void IMenuUiActions.ShowMessageDialog(string title, string caption, IReadOnlyList<string> items) => ShowMessageDialog(title, caption, items);
        void IMenuUiActions.ShowChoiceDialog(string title, string? caption, IReadOnlyDictionary<int, string> items, bool cancelable, string? cancelLabel, Action<ChoiceDialogResult>? onResult)
            => ShowChoiceDialog(title, caption, items, cancelable, cancelLabel, onResult);
        void IMenuUiActions.SpeakNotImplemented() => _speech.Speak(LocalizationService.Mark("Not implemented yet."));

        string IMenuSettingsActions.GetLanguageName() => CurrentLanguageName();
        IReadOnlyList<SpeechBackendInfo> IMenuSettingsActions.GetSpeechBackends() => _speech.AvailableBackends;
        ulong? IMenuSettingsActions.GetActiveSpeechBackendId() => _speech.ActiveBackendId;
        string? IMenuSettingsActions.GetActiveSpeechBackendName() => _speech.ScreenReaderBackendName;
        IReadOnlyList<SpeechVoiceInfo> IMenuSettingsActions.GetSpeechVoices() => _speech.AvailableVoices;
        SpeechCapabilities IMenuSettingsActions.GetSpeechCapabilities() => _speech.ScreenReaderCapabilities;
        void IMenuSettingsActions.ChangeLanguage() => ChangeLanguage();
        void IMenuSettingsActions.ShowRestoreDefaultsDialog() => ShowRestoreDefaultsDialog();
        void IMenuSettingsActions.RestoreDefaults() => RestoreDefaults();
        void IMenuSettingsActions.RecalibrateScreenReaderRate() => StartCalibrationSequence("options_speech");
        void IMenuSettingsActions.CheckForUpdates() => StartManualUpdateCheck();
        void IMenuSettingsActions.ShowAboutDialog() => ShowAboutDialog();
        void IMenuSettingsActions.OpenGameGuide() => OpenGameGuide();
        void IMenuSettingsActions.OpenTrackCreationGuide() => OpenTrackCreationGuide();
        void IMenuSettingsActions.OpenVehicleCreationGuide() => OpenVehicleCreationGuide();
        void IMenuSettingsActions.OpenChangeLogFile() => OpenChangeLogFile();
        void IMenuSettingsActions.ShowLatestChanges() => StartLatestChangesFetch();
        void IMenuSettingsActions.SetDevice(InputDeviceMode mode) => SetDevice(mode);
        void IMenuSettingsActions.SetSpeechBackend(ulong? backendId) => SetSpeechBackend(backendId);
        void IMenuSettingsActions.SetScreenReaderInterrupt(bool enabled) => SetScreenReaderInterrupt(enabled);
        void IMenuSettingsActions.SetSpeechMode(SpeechOutputMode mode) => SetSpeechMode(mode);
        void IMenuSettingsActions.SetSpeechVoice(string? voiceName) => SetSpeechVoice(voiceName);
        void IMenuSettingsActions.SetSpeechRate(float rate) => SetSpeechRate(rate);
        void IMenuSettingsActions.SetUseUpdateProxy(bool enabled) => SetUseUpdateProxy(enabled);
        void IMenuSettingsActions.EditUpdateProxyUrl() => EditUpdateProxyUrl();
        void IMenuSettingsActions.UpdateSetting(Action update) => UpdateSetting(update);

        void IMenuMappingActions.BeginMapping(InputMappingMode mode, DriveIntent action) => _inputMapping.BeginMapping(mode, action);
        void IMenuMappingActions.BeginShortcutMapping(string groupId, string actionId, string displayName) => _shortcutMapping.BeginMapping(groupId, actionId, displayName);
        string IMenuMappingActions.FormatMappingValue(DriveIntent action, InputMappingMode mode) => _inputMapping.FormatMappingValue(action, mode);
        void IMenuMappingActions.ResetMappings(InputMappingMode mode) => ResetMappings(mode);
    }
}



