using TopSpeed.Runtime;

namespace TopSpeed.Windowing.Eto
{
    internal sealed class TextInputService : ITextInputService
    {
        private readonly WindowHost _window;

        public TextInputService(WindowHost window)
        {
            _window = window;
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
    }
}
