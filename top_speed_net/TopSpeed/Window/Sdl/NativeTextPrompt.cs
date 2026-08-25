using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using TopSpeed.Runtime;

namespace TopSpeed.Windowing.Sdl
{
    /// <summary>
    /// Asks the desktop for its own text entry window rather than collecting keys inside the game
    /// window. The SDL window is not built from desktop controls, so a screen reader has nothing to
    /// read inside it; a window the desktop put up is read normally, which gives the player review
    /// by character and word, cursor movement and correction rather than just typing.
    /// </summary>
    internal static class NativeTextPrompt
    {
        // Same helper SDL falls back to for its own file dialogs on Linux.
        private const string LinuxHelper = "zenity";
        private const string MacHelper = "osascript";

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

            Task.Run(() => onCompleted(Run(start, helper)));
            return true;
        }

        private static TextInputResult Run(ProcessStartInfo start, string helper)
        {
            try
            {
                using var process = Process.Start(start);
                if (process == null)
                    return TextInputResult.CreateCancelled();

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                // Both helpers report a dismissed prompt by exiting non-zero.
                if (process.ExitCode != 0)
                    return TextInputResult.CreateCancelled();

                var text = helper == MacHelper ? ParseAppleScriptAnswer(output) : output.TrimEnd('\n', '\r');
                return TextInputResult.Submitted(text ?? string.Empty);
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

            if (helper == LinuxHelper)
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

            if (helper == MacHelper)
            {
                start.ArgumentList.Add("-e");
                start.ArgumentList.Add(
                    "display dialog " + Quote(prompt) +
                    " default answer " + Quote(initialText) +
                    " with title " + Quote(title));
                return start;
            }

            return null;
        }

        // osascript takes source rather than arguments, so anything the player could type has to be
        // escaped back into an AppleScript string literal.
        private static string Quote(string value)
        {
            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c == '\\' || c == '"')
                    builder.Append('\\');
                if (c == '\n' || c == '\r')
                {
                    builder.Append(' ');
                    continue;
                }

                builder.Append(c);
            }

            builder.Append('"');
            return builder.ToString();
        }

        // osascript answers as "button returned:OK, text returned:whatever the player typed".
        private static string ParseAppleScriptAnswer(string output)
        {
            const string Marker = "text returned:";
            var index = output.IndexOf(Marker, StringComparison.Ordinal);
            if (index < 0)
                return string.Empty;

            return output.Substring(index + Marker.Length).TrimEnd('\n', '\r');
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

            if (OperatingSystem.IsMacOS())
                return Exists(MacHelper) ? MacHelper : null;

            if (OperatingSystem.IsLinux())
                return Exists(LinuxHelper) ? LinuxHelper : null;

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
