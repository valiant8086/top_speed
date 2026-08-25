using System;
using System.Windows.Forms;
using TopSpeed.Runtime;

namespace TopSpeed.Windowing.WinForms
{
    internal sealed class WindowHost : IWindowHost, ITextInputService
    {
        private readonly GameWindow _window;

        public event Action? Loaded;
        public event Action? Closed;

        public IntPtr NativeHandle => _window.Handle;

        public WindowHost()
        {
            _window = new GameWindow();
            _window.Load += OnLoaded;
            _window.FormClosed += OnClosed;
        }

        public void Run()
        {
            Application.Run(_window);
        }

        public void RequestClose()
        {
            if (_window.IsDisposed)
                return;

            if (_window.InvokeRequired)
            {
                _window.BeginInvoke((Action)(() =>
                {
                    if (!_window.IsDisposed)
                        _window.Close();
                }));
                return;
            }

            _window.Close();
        }

        public void ShowTextInput(string prompt, string? initialText)
        {
            _window.ShowTextInput(initialText);
        }

        public void HideTextInput()
        {
            _window.HideTextInput();
        }

        public bool TryConsumeTextInput(out TextInputResult result)
        {
            return _window.TryConsumeTextInput(out result);
        }

        public void Dispose()
        {
            _window.Load -= OnLoaded;
            _window.FormClosed -= OnClosed;
            _window.Dispose();
        }

        private void OnLoaded(object? sender, EventArgs e)
        {
            Loaded?.Invoke();
        }

        private void OnClosed(object? sender, FormClosedEventArgs e)
        {
            Closed?.Invoke();
        }
    }
}


