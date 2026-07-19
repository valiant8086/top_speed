using System.Collections.Generic;
using TopSpeed.Speech.Playback;

namespace TopSpeed.Speech.ScreenReaders
{
    internal interface IScreenReader
    {
        IReadOnlyList<SpeechBackendInfo> AvailableBackends { get; }
        IReadOnlyList<SpeechVoiceInfo> AvailableVoices { get; }
        ulong? PreferredBackendId { get; set; }
        ulong? ActiveBackendId { get; }
        string? PreferredVoiceName { get; set; }
        SpeechCapabilities Capabilities { get; }
        string? ActiveBackendName { get; }
        bool Initialize();
        bool IsLoaded();
        bool Speak(string text, bool interrupt = true);
        bool IsSpeaking();
        void Close();
        float GetVolume();
        void SetVolume(float volume);
        float GetRate();
        void SetRate(float rate);
        bool HasSpeech();
        void TrySAPI(bool trySapi);
        void PreferSAPI(bool preferSapi);
        string? DetectScreenReader();
        bool Output(string text, bool interrupt = true);
        bool HasBraille();
        bool Braille(string text);
        bool Silence();
        void BindPlayer(IPlayer? player);
    }
}
