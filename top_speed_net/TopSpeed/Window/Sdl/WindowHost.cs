using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using TopSpeed.Localization;
using TopSpeed.Runtime;
using TopSpeed.Windowing.Sdl.Cocoa;
using TS.Sdl;
using TS.Sdl.Events;
using TS.Sdl.Input;
using SdlRenderer = TS.Sdl.Video.Renderer;
using SdlRuntime = TS.Sdl.Runtime;
using SdlWindow = TS.Sdl.Video.Window;
using SdlWindowFlags = TS.Sdl.Video.WindowFlags;

namespace TopSpeed.Windowing.Sdl
{
    internal sealed class WindowHost : IWindowHost, ITextInputService, IGestureEventSource, ITouchZoneGestureEventSource, ITouchZoneTouchEventSource, IControllerEventSource
    {
        private static readonly InitFlags RequiredInit = InitFlags.Video | InitFlags.Events | InitFlags.Sensor;
        private const string AllowLibdecorHint = "SDL_VIDEO_WAYLAND_ALLOW_LIBDECOR";
        // Every pump would be several Cocoa calls per frame for something that changes only when a
        // window opens or closes; a sixteenth of a second is far below noticing and costs nothing.
        private const int FocusCheckPumps = 15;
        private const int IdleMilliseconds = 4;
        private const double IdleSeconds = IdleMilliseconds / 1000.0;
        private readonly object _sync = new object();
        private readonly TouchZoneRouter _touchZoneRouter;
        private readonly MainThreadDispatcher _mainThread;
        private readonly Queue<TextInputResult> _textResults;
        private readonly StringBuilder _textInputBuffer;
        private IntPtr _window;
        private IntPtr _renderer;
        private uint _windowId;
        private bool _initialized;
        private bool _loadedRaised;
        private bool _running;
        private bool _closeRequested;
        private bool _textInputActive;
        private bool _nativePromptActive;
        private bool _macPromptActive;
        private MacTextPrompt? _macPrompt;
        private int _focusCheckPumps;
        private bool _focusRestoreTried;
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
                KeepKeyboardFocus();
                Idle();
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
            // macOS puts a real Cocoa text field inside this window, the way the Windows build
            // puts a text box inside its form, so a screen reader reads and edits it in place.
            if (TryShowMacPrompt(prompt, initialText))
                return;

            // Elsewhere, prefer a window the desktop puts up, so a screen reader can read and edit
            // it. Only when the desktop offers nothing do we fall back to collecting keys in the
            // game window, which a screen reader cannot see into.
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

            // Collecting the keys here says nothing back, which is only bearable as a last resort.
            // The planned replacement is for the game to read the field out itself, and to be worth
            // having it needs the whole set a screen reader would give: the character moved onto for
            // left and right, the field or history for up and down, what backspace and delete
            // removed, the line ends for home and end, and whole words for control plus left or
            // right. That is also the point to add the setting for choosing this over the window the
            // desktop or Cocoa puts up.
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
            bool nativePromptActive;
            lock (_sync)
            {
                _textInputActive = false;
                nativePromptActive = _nativePromptActive;
            }

            HideMacPrompt();

            // A window the desktop put up closes itself; there is nothing here to take away.
            if (nativePromptActive)
                return;

            if (_window == IntPtr.Zero)
                return;

            Keyboard.ClearComposition(_window);
            Keyboard.StopTextInput(_window);
        }

        // Runs on the window's own thread, from the pump loop. See MacWindowFocus: something Cocoa
        // put over the window can leave it with nothing listening to the keyboard, which reads as
        // the game hanging and beeping until the player switches away and back.
        // The pause between pumps, spent on macOS running the run loop rather than asleep. Asking
        // SDL for events empties the event queue and nothing else, and a screen reader does not ask
        // its questions that way: it asks over the accessibility interface, and those arrive as run
        // loop sources. A loop that only drains events never answers them.
        //
        // Not only while our text field is up, which was the first attempt at this. The moment worth
        // answering most is the one just after the field goes away, when the window takes the
        // keyboard back and the screen reader has something to say about it - and by then the field
        // is already gone, so anything watching for it has stopped watching. Left to a sleep, that
        // announcement waited for whatever next happened to run the loop, and arrived stuck to the
        // front of it.
        //
        // Everywhere else this is what a sleep was: it lasts the same time and gives the time back
        // as soon as there is something to do.
        private void Idle()
        {
            if (MacRunLoop.Spin(IdleSeconds))
                return;

            Thread.Sleep(IdleMilliseconds);
        }

        private void KeepKeyboardFocus()
        {
            if (!MacTextPrompt.IsSupported || _window == IntPtr.Zero)
                return;

            if (_focusCheckPumps++ < FocusCheckPumps)
                return;
            _focusCheckPumps = 0;

            // Our own text field is meant to hold the keyboard while it is up, so leave it be.
            lock (_sync)
            {
                if (_macPromptActive)
                {
                    _focusRestoreTried = false;
                    return;
                }
            }

            if (!MacWindowFocus.HasLostKeyboardFocus(_window))
            {
                _focusRestoreTried = false;
                return;
            }

            // Try once per episode. If Cocoa will not give the keyboard back there is no sense
            // asking sixteen times a second, and a screen reader would announce every attempt.
            if (_focusRestoreTried)
                return;

            _focusRestoreTried = true;
            MacWindowFocus.RestoreKeyboardFocus(_window);
        }

        // Cocoa work has to happen on the thread that owns the window, so both of these go through
        // the dispatcher the window loop drains.
        private bool TryShowMacPrompt(string prompt, string? initialText)
        {
            if (!MacTextPrompt.IsSupported || _window == IntPtr.Zero)
                return false;

            // Set before the field goes up, so a player who presses Return straight away cannot be
            // answered before this is marked active and have the answer overwritten.
            lock (_sync)
            {
                _textInputBuffer.Clear();
                _textInputActive = false;
                _macPromptActive = true;
            }

            var shown = _mainThread.Invoke(() =>
            {
                _macPrompt ??= new MacTextPrompt(OnMacPromptCompleted);
                return _macPrompt.Show(_window, prompt, initialText);
            });

            if (!shown)
            {
                lock (_sync)
                    _macPromptActive = false;
            }

            return shown;
        }

        private void HideMacPrompt()
        {
            bool active;
            lock (_sync)
            {
                active = _macPromptActive;
                _macPromptActive = false;
            }

            if (!active || _macPrompt == null)
                return;

            var prompt = _macPrompt;
            _mainThread.Invoke(() => prompt.Hide());
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

        // Runs on the window's own thread, from Cocoa, when the player finishes with the field.
        // The field has already taken itself away by this point.
        private void OnMacPromptCompleted(TextInputResult result)
        {
            lock (_sync)
            {
                _macPromptActive = false;
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

            if (_renderer != IntPtr.Zero)
            {
                SdlRenderer.Destroy(_renderer);
                _renderer = IntPtr.Zero;
            }

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

            // Wayland has no title bar of its own, so SDL draws one with libdecor, which loads GTK
            // to do it. The window is blank and exists only to hold the keyboard, so a title bar
            // buys nothing, while GTK arriving in the process brings the accessibility bridge in
            // beside the screen reader and complains about its own objects while it does. Read
            // when the video subsystem starts, so it has to be set first.
            if (OperatingSystem.IsLinux())
                SdlRuntime.SetHint(AllowLibdecorHint, "0");

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

            // A shown window is not necessarily a window the desktop will display. Wayland maps a
            // toplevel only once a buffer has been committed to it, so without this the window is
            // listed by the compositor, never appears, and never takes the keyboard - which takes
            // the keys with it, since SDL reports them to the focused window. Nothing is drawn
            // beyond a cleared frame; the game has no visuals of its own.
            _renderer = SdlRenderer.Create(_window);
            PresentFrame();

            _windowId = SdlWindow.GetId(_window);
            _initialized = true;
        }

        /// <summary>
        /// Redraws the one frame the window ever has. Silent when there is no renderer: a desktop
        /// that shows an undrawn window loses nothing by it, and one that cannot make a renderer at
        /// all is no worse off than before.
        /// </summary>
        private void PresentFrame()
        {
            if (_renderer == IntPtr.Zero)
                return;

            SdlRenderer.Present(_renderer);
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

                    case EventType.WindowExposed:
                        PresentFrame();
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
