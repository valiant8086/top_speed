using System;
using TopSpeed.Runtime;
using TopSpeed.Speech;

namespace TopSpeed.Game
{
    internal sealed partial class Game
    {
        private void BeginPromptTextInput(
            string prompt,
            string? initialValue,
            SpeechService.SpeakFlag speakFlag,
            bool speakBeforeInput,
            Action<TextInputResult> onCompleted)
        {
            if (onCompleted == null)
                throw new ArgumentNullException(nameof(onCompleted));

            if (_textInputPromptActive)
            {
                onCompleted(TextInputResult.CreateCancelled());
                return;
            }

            if (speakBeforeInput)
                _speech.Speak(prompt, speakFlag);

            _textInputPromptActive = true;
            _textInputPromptCallback = onCompleted;
            _input.Suspend();
            _textInput.ShowTextInput(prompt, initialValue);
        }

        private void UpdateTextInputPrompt()
        {
            if (!_textInputPromptActive)
                return;

            if (!_textInput.TryConsumeTextInput(out var result))
                return;

            var callback = _textInputPromptCallback;
            _textInputPromptCallback = null;
            _textInputPromptActive = false;
            _input.Resume();
            callback?.Invoke(result);
        }
    }
}

