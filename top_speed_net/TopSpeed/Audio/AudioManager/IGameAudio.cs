using System;

namespace TopSpeed.Audio
{
    internal interface IGameAudio : IDisposable
    {
        void SetMasterVolume(float volume);
        void SetSpeechVolume(float volume);
        void StartUpdateThread(int intervalMs = 8);
        void StopUpdateThread();
        void PlayTriangleTone(double frequencyHz, int durationMs, float volume = 0.35f);
    }
}

