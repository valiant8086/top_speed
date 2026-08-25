using System;
using TopSpeed.Runtime;
using TS.Sdl.Dialogs;

namespace TopSpeed.Windowing.Sdl
{
    /// <summary>
    /// Opens the desktop's own file chooser. macOS builds the chooser out of AppKit, and AppKit
    /// refuses to make a window anywhere but the main thread - it raises an Objective-C exception,
    /// which is not something managed code can catch, so the process aborts outright with no error
    /// and no log. The game asks for a file from its own loop thread, so every call has to be put
    /// on the thread that owns the window first.
    /// </summary>
    internal sealed class FileDialogService : IFileDialogs
    {
        private readonly WindowHost _window;

        public FileDialogService(WindowHost window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
        }

        public void PickAudioFile(Action<string?> onCompleted)
        {
            if (onCompleted == null)
                throw new ArgumentNullException(nameof(onCompleted));

            var filters = new[]
            {
                new DialogFileFilter("Audio files", "wav;ogg;mp3;flac;aac;m4a"),
                new DialogFileFilter("All files", "*")
            };

            // Only putting the chooser up is handed over; it opens without blocking, and SDL
            // reports the answer later.
            _window.MainThread.Invoke(() => FileDialogs.ShowOpenFile(
                result =>
                {
                    if (result == null || result.WasCancelled || result.Paths.Length == 0)
                    {
                        onCompleted(null);
                        return;
                    }

                    onCompleted(result.Paths[0]);
                },
                _window.NativeHandle,
                filters));
        }

        public void PickFolder(string? initialFolder, Action<string?> onCompleted)
        {
            if (onCompleted == null)
                throw new ArgumentNullException(nameof(onCompleted));

            _window.MainThread.Invoke(() => FileDialogs.ShowOpenFolder(
                result =>
                {
                    if (result == null || result.WasCancelled || result.Paths.Length == 0)
                    {
                        onCompleted(null);
                        return;
                    }

                    onCompleted(result.Paths[0]);
                },
                _window.NativeHandle,
                initialFolder,
                allowMany: false));
        }
    }
}

