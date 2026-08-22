using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Eto.Drawing;
using Eto.Forms;
using TopSpeed.Input;
using TopSpeed.Localization;
using TopSpeed.Runtime;

namespace TopSpeed.Windowing.Eto
{
    internal sealed class WindowHost : IWindowHost, IKeyboardEventSource
    {
        // Long enough for a working Quit() to win the race, short enough that quitting does
        // not feel stalled on the platforms where it never will.
        private const int ForcedExitDelayMs = 500;
        private int _shutdownStarted;
        private readonly object _textInputLock = new object();
        private readonly Application _application;
        private readonly Form _window;
        private readonly Drawable _root;
        private TextBox? _inputBox;
        private bool _loadedRaised;
        private bool _submitPending;
        private bool _cancelPending;
        private bool _textInputActive;
        private string _submittedText = string.Empty;

        public event Action? Loaded;
        public event Action? Closed;
        public event Action<InputKey>? KeyDown;
        public event Action<InputKey>? KeyUp;

        public IntPtr NativeHandle { get; private set; }

        internal Form MainForm => _window;

        public WindowHost()
        {
            _application = ApplicationFactory.GetOrCreate();
            _window = new Form
            {
                Title = ResolveWindowTitle(),
                ClientSize = new Size(640, 360),
                Resizable = false,
                Maximizable = false,
                Minimizable = true
            };
            _window.Shown += OnShown;
            _window.Closed += OnClosed;
            _window.KeyDown += OnWindowKeyDown;
            _window.KeyUp += OnWindowKeyUp;
            _window.LostFocus += OnWindowLostFocus;
            _root = new Drawable
            {
                CanFocus = true
            };
            _root.KeyDown += OnWindowKeyDown;
            _root.KeyUp += OnWindowKeyUp;
            _window.Content = _root;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                TryInstallMacControlTabInterceptor();
                TryInstallMacQuitKeyInterceptor();
            }
        }

        public void Run()
        {
            _application.Run(_window);
        }

        public void RequestClose()
        {
            InvokeOnUi(() =>
            {
                try
                {
                    _window.Close();
                }
                catch
                {
                }

                // Eto.Mac hides the window for a programmatic Close() but never raises Form.Closed, so
                // waiting on that event means shutdown simply never happens: the loop keeps running and
                // the app lingers with no window, endable only from Force Quit. Drive it from here
                // instead. Windows and Linux do raise the event, and Shutdown only runs once either way.
                Shutdown();
            });
        }

        // Runs the close handlers exactly once, however the window went away, then ends the process.
        private void Shutdown()
        {
            if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
                return;

            // Synchronous, and everything that matters is already durable: settings are written when
            // they change rather than saved on the way out, so this is stopping the loop and releasing
            // audio, speech and input devices.
            try
            {
                Closed?.Invoke();
            }
            catch
            {
            }

            // Armed before Quit() rather than after, because Quit() runs on the UI thread and does not
            // reliably return here - anything queued behind it would never start. Where Quit() works
            // the process is gone long before this fires; where it does not, this is what ends it.
            _ = Task.Run(async () =>
            {
                await Task.Delay(ForcedExitDelayMs).ConfigureAwait(false);
                Environment.Exit(0);
            });

            try
            {
                (Application.Instance ?? _application).Quit();
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            _window.Shown -= OnShown;
            _window.Closed -= OnClosed;
            _window.KeyDown -= OnWindowKeyDown;
            _window.KeyUp -= OnWindowKeyUp;
            _window.LostFocus -= OnWindowLostFocus;
            _root.KeyDown -= OnWindowKeyDown;
            _root.KeyUp -= OnWindowKeyUp;
            DisposeInputBoxControl();
            _window.Dispose();
        }

        internal void ShowTextInput(string? initialText)
        {
            lock (_textInputLock)
            {
                _submittedText = string.Empty;
                _submitPending = false;
                _cancelPending = false;
            }

            InvokeOnUi(() =>
            {
                EnsureInputBox();
                if (_inputBox == null)
                    return;

                _inputBox.Text = initialText ?? string.Empty;
                _inputBox.Visible = true;
                _inputBox.Enabled = true;
                _root.Content = _inputBox;
                _textInputActive = true;
                ReleaseAllKeys();
                _inputBox.Focus();
            });
        }

        internal void HideTextInput()
        {
            InvokeOnUi(() =>
            {
                HideInputBox();
                _root.Focus();
            });
        }

        internal bool TryConsumeTextInput(out TextInputResult result)
        {
            lock (_textInputLock)
            {
                if (_submitPending)
                {
                    _submitPending = false;
                    result = TextInputResult.Submitted(_submittedText);
                    return true;
                }

                if (_cancelPending)
                {
                    _cancelPending = false;
                    result = TextInputResult.CreateCancelled();
                    return true;
                }
            }

            result = default;
            return false;
        }

        private void OnShown(object? sender, EventArgs e)
        {
            if (!_loadedRaised)
            {
                _loadedRaised = true;
                NativeHandle = ResolveNativeHandle(_window);
                _root.Focus();
                Loaded?.Invoke();
            }
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            Shutdown();
        }

        private void OnWindowKeyDown(object? sender, KeyEventArgs e)
        {
            if (_textInputActive)
                return;
            EmitKeyDown(e.KeyData);

            // The game consumed this key. Without marking it handled, Eto forwards the event to the
            // native view's default keyDown, it walks the responder chain unclaimed, and macOS plays
            // the alert beep — on every arrow key, all race long.
            e.Handled = true;
        }

        private void OnWindowKeyUp(object? sender, KeyEventArgs e)
        {
            // Key-ups are processed even while the text input box is active: swallowing
            // them is how the key that opened the prompt ends up latched down forever in
            // the event-driven keyboard device.
            EmitKeyUp(e.KeyData);
            if (!_textInputActive)
                e.Handled = true;
        }

        private void OnInputKeyDown(object? sender, KeyEventArgs e)
        {
            if (_inputBox == null)
                return;

            if (EtoKeyMap.MatchesEnter(e.KeyData))
            {
                lock (_textInputLock)
                {
                    _submittedText = _inputBox.Text ?? string.Empty;
                    _submitPending = true;
                }

                HideTextInput();
                e.Handled = true;
                return;
            }

            if (EtoKeyMap.MatchesEscape(e.KeyData))
            {
                lock (_textInputLock)
                    _cancelPending = true;

                HideTextInput();
                e.Handled = true;
                return;
            }

            // Let the native text box handle all edit/navigation keys while active.
        }

        private void OnInputKeyUp(object? sender, KeyEventArgs e)
        {
            if (_inputBox == null)
                return;
        }

        private void OnWindowLostFocus(object? sender, EventArgs e)
        {
            ReleaseAllKeys();
        }

        private void EmitKeyDown(Keys keyData)
        {
            if (EtoKeyMap.TryMap(keyData, out var key))
                KeyDown?.Invoke(key);
        }

        private void EmitKeyUp(Keys keyData)
        {
            if (EtoKeyMap.TryMap(keyData, out var key))
                KeyUp?.Invoke(key);
        }

        // Key-ups delivered while another window is key (a file dialog, the text input
        // box) never reach this window, so the event-driven keyboard device would keep
        // those keys latched down forever — and a permanently "held" key re-triggers its
        // shortcut every time its modifiers come back down. Whenever key-ups may have
        // gone elsewhere, report every key released so none can stay stuck.
        internal void ReleaseAllKeys()
        {
            for (var i = 1; i < 256; i++)
                KeyUp?.Invoke((InputKey)i);
        }

        private void InvokeOnUi(Action action)
        {
            var app = Application.Instance ?? _application;
            try
            {
                app.Invoke(action);
            }
            catch
            {
                app.AsyncInvoke(action);
            }
        }

        private void EnsureInputBox()
        {
            if (_inputBox != null)
                return;

            _inputBox = new TextBox
            {
                Visible = false
            };
            _inputBox.KeyDown += OnInputKeyDown;
            _inputBox.KeyUp += OnInputKeyUp;
        }

        private void HideInputBox()
        {
            if (_inputBox == null)
                return;

            _textInputActive = false;
            _inputBox.Visible = false;
            _inputBox.Enabled = false;
            _root.Content = null;
            ReleaseAllKeys();
        }

        private void DisposeInputBoxControl()
        {
            if (_inputBox == null)
                return;

            HideInputBox();
            _inputBox.KeyDown -= OnInputKeyDown;
            _inputBox.KeyUp -= OnInputKeyUp;
            _inputBox.Dispose();
            _inputBox = null;
        }

        // Cocoa routes Control-modified key presses through the key-equivalent chain
        // (window tabbing, key-view-loop navigation) before normal key dispatch, so
        // Control+Tab never reaches this window's KeyDown and panel switching is dead on
        // macOS. MacControlTabInterceptor sees the event first via a local NSEvent
        // monitor and feeds it to the game. It is only compiled into osx builds (it needs
        // MonoMac, which only the Mac Eto platform references), so it is looked up by
        // name here; on other platforms the lookup finds nothing and this is a no-op.
        private void TryInstallMacControlTabInterceptor()
        {
            try
            {
                var type = Type.GetType("TopSpeed.Windowing.Eto.MacControlTabInterceptor");
                var install = type?.GetMethod("Install", BindingFlags.Public | BindingFlags.Static);
                install?.Invoke(null, new object[]
                {
                    (Func<IntPtr>)(() => NativeHandle),
                    (Func<bool>)(() => !_textInputActive),
                    (Action<InputKey>)(key => KeyDown?.Invoke(key)),
                    (Action<InputKey>)(key => KeyUp?.Invoke(key))
                });
            }
            catch
            {
                // Best effort: without the monitor the game still runs, only Control+Tab
                // stays unavailable because Cocoa consumes it.
            }
        }

        // Command+Q reaches the main menu's Quit item before any window sees it, and this app has no
        // menu bar for it to reach, so the shortcut does nothing. MacQuitKeyInterceptor catches the
        // press first and asks for the same close the Exit item and Escape at the main menu use. Same
        // reflection lookup and the same osx-only compilation as the Control+Tab monitor above.
        private void TryInstallMacQuitKeyInterceptor()
        {
            try
            {
                var type = Type.GetType("TopSpeed.Windowing.Eto.MacQuitKeyInterceptor");
                var install = type?.GetMethod("Install", BindingFlags.Public | BindingFlags.Static);
                install?.Invoke(null, new object[]
                {
                    (Func<IntPtr>)(() => NativeHandle),
                    (Action)RequestClose
                });
            }
            catch
            {
                // Best effort: without the monitor the game still runs and still quits from the menu,
                // Command+Q just stays inert as it is today.
            }
        }

        private static string ResolveWindowTitle()
        {
            var title = LocalizationService.Translate(LocalizationService.Mark("Top Speed"));
            return string.IsNullOrWhiteSpace(title) ? "Top Speed" : title;
        }

        private static IntPtr ResolveNativeHandle(Form window)
        {
            try
            {
                var controlObject = window.ControlObject;
                if (controlObject == null)
                    return IntPtr.Zero;

                var handleProperty = controlObject.GetType().GetProperty("Handle");
                if (handleProperty == null)
                    return IntPtr.Zero;

                var value = handleProperty.GetValue(controlObject);
                return value is IntPtr handle ? handle : IntPtr.Zero;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }
    }
}
