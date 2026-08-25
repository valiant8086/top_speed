namespace TopSpeed.Runtime
{
    internal interface ITextInputService
    {
        /// <param name="prompt">Labels the entry for a screen reader when the host opens a
        /// window of its own. Hosts that type into the game window ignore it, because the game
        /// has already spoken it.</param>
        void ShowTextInput(string prompt, string? initialText);
        void HideTextInput();
        bool TryConsumeTextInput(out TextInputResult result);
    }
}
