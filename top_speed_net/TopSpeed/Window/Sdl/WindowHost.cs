using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using TopSpeed.Localization;
using TopSpeed.Runtime;
using TS.Sdl;
using TS.Sdl.Events;
using TS.Sdl.Input;
using SdlRuntime = TS.Sdl.Runtime;
using SdlWindow = TS.Sdl.Video.Window;
using SdlWindowFlags = TS.Sdl.Video.WindowFlags;

namespace TopSpeed.Windowing.Sdl
{
    internal sealed class WindowHost : IWindowHost, ITextInputService, IGestureEventSource, ITouchZoneGestureEventSource, ITouchZoneTouchEventSource, IControllerEventSource
    {
        private static readonly InitFlags RequiredInit = InitFlags.Video | InitFlags.Events | InitFlags.Sensor;
        private readonly object _sync = new object();
        private readonly TouchZoneRouter _touchZoneRouter;
        private readonly MainThreadDispatcher _mainThread;
        private readonly Queue<TextInputResult> _textResults;
        private readonly StringBuilder _textInputBuffer;
        private IntPtr _window;
        private uint _windowId;
        private bool _initialized;
        private bool _loadedRaised;
        private bool _running;
        private bool _closeRequested;
        private bool _textInputActive;
        private bool _nativePromptActive;
        private bool _disposed;

        public event Action? Loaded;
        public event Action? Closed;
        public event Action<GestureEvent>? GestureRaised;
        public event Action<TouchZoneGestureEvent>? TouchZoneGestureRaised;
        public event Action<TouchZoneTouchEvent>? TouchZoneTouchRaised;
        public event Action<ControllerEvent>? ControllerEventRaised;

        public IntPtr NativeHandle => _window;

        /// <summary>Runs work on the thread that owns this window. See <see cref="MainThreadDispatcher"/>.</summary>
        public MainThreadDispatcher MainThread => _mainThread;

        public WindowHost()
        {
            var recognizer = new GestureRecognizer(BuildGestureOptions());
            _touchZoneRouter = new TouchZoneRouter(recognizer);
            _touchZoneRouter.TouchRaised += OnTouchZoneTouchRaised;
            _touchZoneRouter.GestureRaised += OnTouchZoneGestureRaised;
            _mainThread = new MainThreadDispatcher();
            _textResults = new Queue<TextInputResult>();
            _textInputBuffer = new StringBuilder(128);
        }

        public void Run()
        {
            EnsureWindow();
            if (_disposed)
                return;

            _running = true;
            _closeRequested = false;
            if (!_loadedRaised)
            {
                _loadedRaised = true;
                Loaded?.Invoke();
            }

            while (_running && !_closeRequested && !_disposed)
            {
                PumpEvents();
                _mainThread.Drain();
                _touchZoneRouter.Update();
                Thread.Sleep(4);
            }

            _running = false;
            _mainThread.Stop();
            Closed?.Invoke();
        }

        public void RequestClose()
        {
            _closeRequested = true;
            _running = false;
        }

        public void SetTouchZones(IReadOnlyList<TouchZone> zones)
        {
            if (zones == null)
                throw new ArgumentNullException(nameof(zones));

            _touchZoneRouter.ClearZones();
            for (var i = 0; i < zones.Count; i++)
                _touchZoneRouter.SetZone(zones[i]);
        }

        public void ClearTouchZones()
        {
            _touchZoneRouter.ClearZones();
        }

        public void ShowTextInput(string prompt, string? initialText)
        {
            // Prefer a window the desktop puts up, so a screen reader can read and edit it. Only
            // when the desktop offers nothing do we fall back to collecting keys in the game window,
            // which a screen reader cannot see into.
            if (NativeTextPrompt.TryShow(prompt, initialText, ResolveWindowTitle(), OnNativePromptCompleted))
            {
                lock (_sync)
                {
                    _textInputBuffer.Clear();
                    _textInputActive = false;
                    _nativePromptActive = true;
                }

                return;
            }

            lock (_sync)
            {
                _textInputBuffer.Clear();
                if (!string.IsNullOrEmpty(initialText))
                    _textInputBuffer.Append(initialText);
                _textInputActive = true;
                _nativePromptActive = false;
            }

            if (_window == IntPtr.Zero)
                return;

            Keyboard.StartTextInput(
                _window,
                new TextInputOptions
                {
                    Type = TextInputType.Text,
                    Capitalization = Capitalization.Sentences,
                    AutoCorrect = true,
                    MultiLine = true
                });
        }

        public void HideTextInput()
        {
            lock (_sync)
            {
                _textInputActive = false;
                if (_nativePromptActive)
                    return;
            }

            if (_window == IntPtr.Zero)
                return;

            Keyboard.ClearComposition(_window);
            Keyboard.StopTextInput(_window);
        }

        public bool TryConsumeTextInput(out TextInputResult result)
        {
            lock (_sync)
            {
                if (_textResults.Count == 0)
                {
                    result = default;
                    return false;
                }

                result = _textResults.Dequeue();
                return true;
            }
        }

        // Runs on a background thread once the desktop's prompt closes. Queued like any other
        // result so the game picks it up on its own thread.
        private void OnNativePromptCompleted(TextInputResult result)
        {
            lock (_sync)
            {
                _nativePromptActive = false;
                _textResults.Enqueue(result);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _running = false;
            _closeRequested = true;
            _mainThread.Stop();
            HideTextInput();
            _touchZoneRouter.TouchRaised -= OnTouchZoneTouchRaised;
            _touchZoneRouter.GestureRaised -= OnTouchZoneGestureRaised;
            _touchZoneRouter.Dispose();

            if (_window != IntPtr.Zero)
            {
                SdlWindow.Destroy(_window);
                _window = IntPtr.Zero;
                _windowId = 0;
            }

            if (_initialized)
            {
                SdlRuntime.QuitSubSystem(RequiredInit);
                _initialized = false;
            }
        }

        private void EnsureWindow()
        {
            if (_initialized)
                return;

#if NET10_0_OR_GREATER
            if (OperatingSystem.IsIOS() && !IOSLauncher.IsOnMainThread())
                throw new InvalidOperationException("SDL initialization on iOS must run on the main thread.");
#endif

            SdlRuntime.SetMainReady();
            if (!SdlRuntime.InitSubSystem(RequiredInit) && (SdlRuntime.WasInit(RequiredInit) & RequiredInit) != RequiredInit)
                throw new InvalidOperationException($"Unable to initialize SDL runtime: {SdlRuntime.GetError()}");

            _window = SdlWindow.Create(
                ResolveWindowTitle(),
                width: 640,
                height: 360,
                SdlWindowFlags.Resizable | SdlWindowFlags.HighPixelDensity);
            if (_window == IntPtr.Zero)
                throw new InvalidOperationException($"Unable to create SDL window: {SdlRuntime.GetError()}");

            SdlWindow.Show(_window);
            _windowId = SdlWindow.GetId(_window);
            _initialized = true;
        }

        private void PumpEvents()
        {
            var routeControllerEvents = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();
            while (SdlRuntime.PollEvent(out var value))
            {
                if (routeControllerEvents && ControllerEvents.TryConvert(value, out var controllerEvent) && controllerEvent.Source != ControllerEventSource.Sensor)
                    ControllerEventRaised?.Invoke(controllerEvent);

                switch ((EventType)value.Type)
                {
                    case EventType.Quit:
                        RequestClose();
                        break;

                    case EventType.FingerDown:
                    case EventType.FingerMotion:
                    case EventType.FingerUp:
                    case EventType.FingerCanceled:
                        _touchZoneRouter.Process(value);
                        break;

                    case EventType.TextInput:
                        HandleTextInputEvent(value.TextInput);
                        break;

                    case EventType.KeyDown:
                        HandleTextInputKeyDown(value.Keyboard);
                        break;
                }
            }
        }

        private void HandleTextInputEvent(TextInputEvent value)
        {
            if (!IsWindowMatch(value.WindowId))
                return;

            lock (_sync)
            {
                if (!_textInputActive)
                    return;

                var text = value.Text;
                if (!string.IsNullOrEmpty(text))
                    _textInputBuffer.Append(text);
            }
        }

        private void HandleTextInputKeyDown(KeyboardEvent value)
        {
            if (!IsWindowMatch(value.WindowId))
                return;

            lock (_sync)
            {
                if (!_textInputActive)
                    return;

                switch (value.Scancode)
                {
                    case Scancode.Backspace:
                        if (_textInputBuffer.Length > 0)
                            _textInputBuffer.Remove(_textInputBuffer.Length - 1, 1);
                        return;

                    case Scancode.Return:
                    case Scancode.KpEnter:
                        _textResults.Enqueue(TextInputResult.Submitted(_textInputBuffer.ToString()));
                        _textInputActive = false;
                        break;

                    case Scancode.Escape:
                    case Scancode.ACBack:
                        _textResults.Enqueue(TextInputResult.CreateCancelled());
                        _textInputActive = false;
                        break;

                    default:
                        return;
                }
            }

            Keyboard.ClearComposition(_window);
            Keyboard.StopTextInput(_window);
        }

        private bool IsWindowMatch(uint eventWindowId)
        {
            return eventWindowId == 0 || _windowId == 0 || _windowId == eventWindowId;
        }

        private void OnTouchZoneGestureRaised(TouchZoneGestureEvent value)
        {
            GestureRaised?.Invoke(value.Gesture);
            TouchZoneGestureRaised?.Invoke(value);
        }

        private void OnTouchZoneTouchRaised(TouchZoneTouchEvent value)
        {
            TouchZoneTouchRaised?.Invoke(value);
        }

        private static string ResolveWindowTitle()
        {
            var title = LocalizationService.Translate(LocalizationService.Mark("Top Speed"));
            return string.IsNullOrWhiteSpace(title) ? "Top Speed" : title;
        }

        private static GestureOptions BuildGestureOptions()
        {
            var options = new GestureOptions();
            if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
            {
                options.SwipeMinDistance = 0.06f;
                options.SwipeMinVelocity = 0.3f;
                options.TapMove = 0.025f;
                options.DoubleTapMove = 0.05f;
            }
            return options;
        }
    }
}
