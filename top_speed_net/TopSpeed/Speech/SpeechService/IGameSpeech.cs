using System.Collections.Generic;
using System;
using TopSpeed.Input;

namespace TopSpeed.Speech
{
    internal interface IGameSpeech : IDisposable
    {
        float ScreenReaderRateMs { get; set; }
        float SpeechRate { get; set; }
        SpeechOutputMode OutputMode { get; set; }
        bool ScreenReaderInterrupt { get; set; }
        IReadOnlyList<SpeechBackendInfo> AvailableBackends { get; }
        IReadOnlyList<SpeechVoiceInfo> AvailableVoices { get; }
        ulong? PreferredBackendId { get; set; }
        ulong? ActiveBackendId { get; }
        void SetBackendVolume(float volume);
        string? PreferredVoiceName { get; set; }
        SpeechCapabilities ScreenReaderCapabilities { get; }
        string? ScreenReaderBackendName { get; }
        void Speak(string text);
        void Speak(string text, SpeechService.SpeakFlag flag);
    }
}
