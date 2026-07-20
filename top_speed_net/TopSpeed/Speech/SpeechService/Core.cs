using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using TopSpeed.Audio;
using TopSpeed.Input;
using TopSpeed.Localization;
using TopSpeed.Speech.Playback;
using TopSpeed.Speech.ScreenReaders;

namespace TopSpeed.Speech
{
    internal sealed partial class SpeechService : IGameSpeech
    {
        public enum SpeakFlag
        {
            None,
            NoInterrupt,
            NoInterruptButStop,
            Interruptable,
            InterruptableButStop
        }

        private readonly Stopwatch _watch = new Stopwatch();
        private readonly IScreenReader _screenReader;
        private readonly ScreenReaderWorker _screenReaderWorker;
        private readonly Player _player;
        private long _timeRequiredMs;
        private string _lastSpoken = string.Empty;
        private Func<bool>? _isInputHeld;
        private Action? _prepareForInterruptableSpeech;
        private volatile bool _screenReaderReady;
        private float _speechRate = 0.5f;
        private float _backendVolume = 1f;
        private bool _speechSuppressedUntilNextSpeak;

        public SpeechService(AudioManager audio, Func<bool>? isInputHeld = null, Action? prepareForInterruptableSpeech = null)
        {
            _player = new Player(audio ?? throw new ArgumentNullException(nameof(audio)));
            _isInputHeld = isInputHeld;
            _prepareForInterruptableSpeech = prepareForInterruptableSpeech;
            _screenReader = Factory.Create();
            _screenReaderWorker = new ScreenReaderWorker(_screenReader, _player);
            _screenReaderReady = InitializeScreenReader();
        }

        public bool IsAvailable => _screenReaderReady;

        public float ScreenReaderRateMs { get; set; }
        public SpeechOutputMode OutputMode { get; set; } = SpeechOutputMode.Speech;
        public bool ScreenReaderInterrupt { get; set; }

        public IReadOnlyList<SpeechBackendInfo> AvailableBackends
        {
            get
            {
                try
                {
                    return _screenReaderWorker.Invoke(reader => reader.AvailableBackends);
                }
                catch
                {
                    return Array.Empty<SpeechBackendInfo>();
                }
            }
        }

        public IReadOnlyList<SpeechVoiceInfo> AvailableVoices
        {
            get
            {
                try
                {
                    return _screenReaderWorker.Invoke(reader => reader.AvailableVoices);
                }
                catch
                {
                    return Array.Empty<SpeechVoiceInfo>();
                }
            }
        }

        public ulong? ActiveBackendId
        {
            get
            {
                try
                {
                    return _screenReaderWorker.Invoke(reader => reader.ActiveBackendId);
                }
                catch
                {
                    return null;
                }
            }
        }

        public SpeechCapabilities ScreenReaderCapabilities
        {
            get
            {
                try
                {
                    return _screenReaderWorker.Invoke(reader => reader.Capabilities);
                }
                catch
                {
                    return SpeechCapabilities.None;
                }
            }
        }

        // Backends that voice directly (Android TTS, AVSpeech, Speech Dispatcher)
        // never reach the game's speech output, so the volume has to be handed to
        // the backend itself. Prism normalizes volume to 0.0-1.0 across backends,
        // matching the scalar used for the game's own audio. No-ops on backends
        // without volume support, notably screen readers.
        public void SetBackendVolume(float volume)
        {
            // Remembered so it can be re-applied after a reinitialize: switching
            // backends builds a new backend that starts at Prism's default volume,
            // and speech can also silently reinitialize when it recovers.
            _backendVolume = Math.Max(0f, Math.Min(1f, volume));
            try
            {
                _screenReaderWorker.Invoke(reader =>
                {
                    ApplyBackendVolumeOnWorker(reader);
                    return 0;
                });
            }
            catch
            {
            }
        }

        public string? ScreenReaderBackendName
        {
            get
            {
                try
                {
                    return _screenReaderWorker.Invoke(reader => reader.ActiveBackendName);
                }
                catch
                {
                    return null;
                }
            }
        }

        public float SpeechRate
        {
            get => _speechRate;
            set
            {
                var clamped = Math.Max(0f, Math.Min(1f, value));
                _speechRate = clamped;
                ApplySpeechRate();
            }
        }

        public ulong? PreferredBackendId
        {
            get
            {
                try
                {
                    return _screenReaderWorker.Invoke(reader => reader.PreferredBackendId);
                }
                catch
                {
                    return null;
                }
            }
            set
            {
                try
                {
                    _screenReaderReady = _screenReaderWorker.Invoke(reader =>
                    {
                        if (reader.PreferredBackendId == value)
                            return _screenReaderReady;

                        TrySilence(reader);
                        reader.PreferredBackendId = value;
                        return ReinitializeScreenReaderOnWorker(reader);
                    });
                }
                catch
                {
                    _screenReaderReady = false;
                }
            }
        }

        public string? PreferredVoiceName
        {
            get
            {
                try
                {
                    return _screenReaderWorker.Invoke(reader => reader.PreferredVoiceName);
                }
                catch
                {
                    return null;
                }
            }
            set
            {
                try
                {
                    _screenReaderWorker.Invoke(reader =>
                    {
                        if (string.Equals(reader.PreferredVoiceName, value, StringComparison.OrdinalIgnoreCase))
                            return 0;

                        // Stop the current voice before switching. Otherwise the
                        // in-flight utterance keeps playing on the now-previous
                        // voice and the new announcement can't interrupt it, so
                        // arrowing across voices forces you to hear each name in full.
                        TrySilence(reader);
                        reader.PreferredVoiceName = value;
                        return 0;
                    });
                }
                catch
                {
                }
            }
        }

        public void BindInputProbe(Func<bool> isInputHeld)
        {
            _isInputHeld = isInputHeld;
        }

        public void BindInterruptPreparation(Action prepareForInterruptableSpeech)
        {
            _prepareForInterruptableSpeech = prepareForInterruptableSpeech;
        }

        public void Speak(string text)
        {
            SpeakInternal(text, SpeakFlag.None, allowConfiguredInterrupt: true);
        }

        public void Speak(string text, SpeakFlag flag)
        {
            SpeakInternal(text, flag, allowConfiguredInterrupt: false);
        }

        private void SpeakInternal(string text, SpeakFlag flag, bool allowConfiguredInterrupt)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            _speechSuppressedUntilNextSpeak = false;

            var shouldInterruptCurrent = flag == SpeakFlag.NoInterruptButStop || flag == SpeakFlag.InterruptableButStop;
            var interruptSpeech = shouldInterruptCurrent || (allowConfiguredInterrupt && ScreenReaderInterrupt);
            if (interruptSpeech)
                // Skip the standalone backend stop: the Speak below passes interrupt=true,
                // which flushes atomically. Issuing a separate stop first races SAPI's async
                // stop against the new utterance and occasionally swallows it (no speech).
                Purge(silenceBackend: false);

            text = text.Trim();
            text = LocalizationService.Translate(text);
            _lastSpoken = text;

            var spoke = false;
            if (_screenReaderReady)
            {
                spoke = TrySpeakWithScreenReader(text, interruptSpeech);
                if (spoke)
                    StartSpeakTimer(text);
            }

            if (!spoke)
                return;

            if (flag == SpeakFlag.None
                || flag == SpeakFlag.NoInterrupt
                || flag == SpeakFlag.NoInterruptButStop)
                return;

            var interruptable = flag == SpeakFlag.Interruptable || flag == SpeakFlag.InterruptableButStop;
            if (interruptable)
                PrepareForInterruptableSpeech();

            while (IsSpeaking())
            {
                if (interruptable && IsInputHeld())
                {
                    Purge();
                    break;
                }

                Thread.Sleep(10);
            }
        }

        public bool IsSpeaking()
        {
            if (_speechSuppressedUntilNextSpeak)
                return false;

            if (_watch.IsRunning)
                return _watch.ElapsedMilliseconds < _timeRequiredMs;

            if (!_screenReaderReady)
                return false;

            try
            {
                return _screenReaderWorker.Invoke(reader => reader.IsSpeaking());
            }
            catch
            {
                return false;
            }
        }

        public void Purge()
        {
            Purge(silenceBackend: true);
        }

        private void Purge(bool silenceBackend)
        {
            _watch.Reset();
            _timeRequiredMs = 0;
            _speechSuppressedUntilNextSpeak = true;

            if (!silenceBackend || !_screenReaderReady)
                return;

            try
            {
                _screenReaderWorker.Invoke(reader =>
                {
                    TrySilence(reader);
                    return 0;
                });
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            Purge();

            try
            {
                _screenReaderWorker.Dispose();
            }
            catch
            {
            }

            _player.Dispose();
        }

        private bool InitializeScreenReader()
        {
            try
            {
                return _screenReaderWorker.Invoke(ReinitializeScreenReaderOnWorker);
            }
            catch
            {
                return false;
            }
        }

        private bool ReinitializeScreenReaderOnWorker(IScreenReader reader)
        {
            TrySilence(reader);
            TryClose(reader);

            reader.TrySAPI(true);
            reader.PreferSAPI(false);

            var initialized = false;
            try
            {
                initialized = reader.Initialize();
            }
            catch
            {
                initialized = false;
            }

            if (initialized)
            {
                ApplySpeechRateOnWorker(reader);
                ApplyBackendVolumeOnWorker(reader);
            }

            return initialized;
        }

        private bool TrySpeakWithScreenReader(string text, bool interrupt)
        {
            var outputMode = OutputMode;
            try
            {
                return _screenReaderWorker.Invoke(reader =>
                {
                    if (!_screenReaderReady)
                        _screenReaderReady = ReinitializeScreenReaderOnWorker(reader);

                    if (!_screenReaderReady)
                        return false;

                    try
                    {
                        switch (outputMode)
                        {
                            case SpeechOutputMode.Braille:
                                if (reader.Braille(text))
                                    return true;

                                if (reader.Output(text, interrupt))
                                    return true;

                                return reader.Speak(text, interrupt);

                            case SpeechOutputMode.SpeechAndBraille:
                                if (reader.Output(text, interrupt))
                                    return true;

                                if (reader.Speak(text, interrupt))
                                {
                                    reader.Braille(text);
                                    return true;
                                }

                                return reader.Braille(text);

                            default:
                                if (reader.Speak(text, interrupt))
                                    return true;

                                return reader.Output(text, interrupt);
                        }
                    }
                    catch
                    {
                        return false;
                    }
                });
            }
            catch
            {
                return false;
            }
        }

        private void StartSpeakTimer(string text)
        {
            if (ScreenReaderRateMs <= 0f)
            {
                _watch.Reset();
                _timeRequiredMs = 0;
                return;
            }

            var words = CountWords(text);
            _timeRequiredMs = (long)(words * ScreenReaderRateMs);
            _watch.Reset();
            _watch.Start();
        }

        private static int CountWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            return text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        private bool IsInputHeld()
        {
            try
            {
                return _isInputHeld != null && _isInputHeld();
            }
            catch
            {
                return false;
            }
        }

        private void PrepareForInterruptableSpeech()
        {
            try
            {
                _prepareForInterruptableSpeech?.Invoke();
            }
            catch
            {
            }
        }

        private void ApplySpeechRate()
        {
            if (!_screenReaderReady)
                return;

            try
            {
                _screenReaderWorker.Invoke(reader =>
                {
                    ApplySpeechRateOnWorker(reader);
                    return 0;
                });
            }
            catch
            {
            }
        }

        private void ApplySpeechRateOnWorker(IScreenReader reader)
        {
            try
            {
                reader.SetRate(_speechRate);
            }
            catch
            {
            }
        }

        private void ApplyBackendVolumeOnWorker(IScreenReader reader)
        {
            try
            {
                reader.SetVolume(_backendVolume);
            }
            catch
            {
            }
        }

        private static void TryClose(IScreenReader reader)
        {
            try
            {
                reader.Close();
            }
            catch
            {
            }
        }

        private static void TrySilence(IScreenReader reader)
        {
            try
            {
                reader.Silence();
            }
            catch
            {
            }
        }
    }
}
