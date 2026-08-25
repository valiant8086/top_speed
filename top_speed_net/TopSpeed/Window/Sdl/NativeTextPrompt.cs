using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using TopSpeed.Runtime;

namespace TopSpeed.Windowing.Sdl
{
    /// <summary>
    /// Asks the desktop for its own text entry window rather than collecting keys inside the game
    /// window. The SDL window is not built from desktop controls, so a screen reader has nothing to
    /// read inside it; a window the desktop put up is read normally, which gives the player review
    /// by character and word, cursor movement and correction rather than just typing.
    ///
    /// Linux only. macOS is deliberately left out - see <see cref="Probe"/>.
    /// </summary>
    internal static class NativeTextPrompt
    {
        // Whichever of these the desktop happens to have. zenity is the GTK one and the most
        // widely installed, kdialog is its KDE counterpart, and yad is a zenity fork that turns up
        // on installs that ship neither. All three are read by a screen reader, because all three
        // are built from ordinary desktop controls.
        private const string Zenity = "zenity";
        private const string KDialog = "kdialog";
        private const string Yad = "yad";

        private static readonly object ProbeLock = new object();
        private static bool _probed;
        private static string? _helper;

        public static bool IsAvailable => ResolveHelper() != null;

        /// <summary>
        /// Puts the prompt up and reports the answer later on a background thread. False when the
        /// desktop offers nothing to ask with, leaving the caller to collect keys itself.
        /// </summary>
        public static bool TryShow(
            string prompt,
            string? initialText,
            string title,
            Action<TextInputResult> onCompleted)
        {
            if (onCompleted == null)
                throw new ArgumentNullException(nameof(onCompleted));

            var helper = ResolveHelper();
            if (helper == null)
                return false;

            var start = BuildStart(helper, prompt ?? string.Empty, initialText ?? string.Empty, title);
            if (start == null)
                return false;

            Task.Run(() => onCompleted(Run(start)));
            return true;
        }

        private static TextInputResult Run(ProcessStartInfo start)
        {
            try
            {
                using var process = Process.Start(start);
                if (process == null)
                    return TextInputResult.CreateCancelled();

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                // The helpers all report a dismissed prompt by exiting non-zero.
                if (process.ExitCode != 0)
                    return TextInputResult.CreateCancelled();

                return TextInputResult.Submitted(output.TrimEnd('\n', '\r'));
            }
            catch (Exception)
            {
                // A helper that will not start is the same to the player as one that was dismissed.
                return TextInputResult.CreateCancelled();
            }
        }

        private static ProcessStartInfo? BuildStart(string helper, string prompt, string initialText, string title)
        {
            var start = new ProcessStartInfo
            {
                FileName = helper,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (helper == Zenity || helper == Yad)
            {
                start.ArgumentList.Add("--entry");
                start.ArgumentList.Add("--title");
                start.ArgumentList.Add(title);
                start.ArgumentList.Add("--text");
                start.ArgumentList.Add(prompt);
                start.ArgumentList.Add("--entry-text");
                start.ArgumentList.Add(initialText);
                return start;
            }

            if (helper == KDialog)
            {
                start.ArgumentList.Add("--title");
                start.ArgumentList.Add(title);
                start.ArgumentList.Add("--inputbox");
                start.ArgumentList.Add(prompt);
                start.ArgumentList.Add(initialText);
                return start;
            }

            return null;
        }

        private static string? ResolveHelper()
        {
            if (_probed)
                return _helper;

            lock (ProbeLock)
            {
                if (_probed)
                    return _helper;

                _helper = Probe();
                _probed = true;
                return _helper;
            }
        }

        private static string? Probe()
        {
            // The phone and tablet hosts have their own on-screen keyboard, and no shell to run.
            if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
                return null;

            // macOS asked with osascript's "display dialog", and the cost was too high. While the
            // dialog was up the game window stopped answering the window server, so macOS painted
            // it as not responding, and switching away and back could leave neither the dialog nor
            // the game reachable, with the game needing to be forced to quit. A prompt that can
            // strand the player is worse than one a screen reader cannot read, so macOS types into
            // the game window instead - see WindowHost.ShowTextInput.
            if (OperatingSystem.IsMacOS())
                return null;

            if (OperatingSystem.IsLinux())
                return FirstPresent(LinuxCandidates());

            return null;
        }

        // Ask with the desktop's own tool where we can tell which desktop it is, so the prompt
        // looks and behaves like everything else the player uses.
        private static string[] LinuxCandidates()
        {
            var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? string.Empty;
            if (desktop.IndexOf("KDE", StringComparison.OrdinalIgnoreCase) >= 0)
                return new[] { KDialog, Zenity, Yad };

            return new[] { Zenity, Yad, KDialog };
        }

        private static string? FirstPresent(string[] candidates)
        {
            for (var i = 0; i < candidates.Length; i++)
            {
                if (Exists(candidates[i]))
                    return candidates[i];
            }

            return null;
        }

        private static bool Exists(string command)
        {
            var path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path))
                return false;

            var directories = path!.Split(Path.PathSeparator);
            for (var i = 0; i < directories.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(directories[i]))
                    continue;

                try
                {
                    if (File.Exists(Path.Combine(directories[i], command)))
                        return true;
                }
                catch (Exception)
                {
                    // An unreadable PATH entry is simply not where the helper lives.
                }
            }

            return false;
        }
    }
}
