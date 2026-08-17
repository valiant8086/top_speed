using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using TopSpeed.Localization;
using TopSpeed.Server.Logging;

namespace TopSpeed.Server.Updates
{
    /// <summary>
    /// How a server started from a terminal on Linux or macOS gets out of the way for an update
    /// and comes back into the same terminal afterwards.
    ///
    /// The obvious arrangement does not work, and did not: start the updater, exit, and let the
    /// updater run the new server. The shell that launched the server is waiting on exactly one
    /// process id, and the moment that id exits the shell takes the terminal's foreground back.
    /// Anything started afterwards is a background process, and a background process that reads
    /// the terminal is refused by the kernel. The server that came back therefore had a terminal
    /// it was never allowed to read: it printed its banner, printed a prompt, and then waited
    /// silently for somebody to attach, looking for all the world as though it had died while
    /// still holding the port.
    ///
    /// So this replaces the process instead of ending it. Exec keeps the process id, which means
    /// the shell is still waiting on us and the terminal never changes hands. What now occupies
    /// that id is a shell script that runs the updater, waits for it to finish, and execs the new
    /// server, which therefore arrives in the foreground of the terminal it belongs to.
    ///
    /// Three things fall out of that which are worth having:
    ///
    /// The updater no longer needs a process to wait for. Exec has already replaced the server,
    /// so its executable is not running any more and can be replaced; the whole --pid handshake
    /// is unnecessary on this path.
    ///
    /// The updater's own output lands in the terminal, because it is running in the foreground of
    /// it. Until now it wrote nowhere at all unless asked for a log, which is exactly the record
    /// we have twice wanted and not had.
    ///
    /// And it survives being done repeatedly. Every update takes the same route, so a server left
    /// running for months stays in the window it started in.
    ///
    /// Windows is not part of this. It has no way to replace a process, so the id the console is
    /// waiting on always dies, and with no job control a second reader of the console would race
    /// the shell for keystrokes with nothing to arbitrate. There the updater opens a new console
    /// window, which is visible and works, and a service is the better answer for anybody who
    /// minds.
    /// </summary>
    internal static class UpdateHandoff
    {
        /// <summary>
        /// How long the script waits for the update to finish before giving up and starting the
        /// server anyway. Taken from the marker rather than chosen here, because how long that
        /// file is worth believing is one question and had grown three answers.
        /// </summary>
        private static int WaitSeconds => (int)UpdateMarker.AssumeAbandonedAfter.TotalSeconds;

        private static string? _pending;

        /// <summary>Whether an update wants this process replaced on its way out.</summary>
        public static bool IsPending => _pending != null;

        /// <summary>
        /// Records what to become. Deliberately not acted on here: exec unwinds nothing, so it
        /// has to happen after the server has released its port, its control socket and its log,
        /// which is a good deal later than the moment the update is decided on.
        /// </summary>
        public static void Prepare(string script)
        {
            _pending = script;
        }

        /// <summary>
        /// Becomes the script, and does not return when that works.
        ///
        /// It returns when it does not: no libc to call, or no shell to become. That leaves a
        /// folder claiming an update that was never started, since the marker is raised before
        /// this and the updater is only launched by the script. So the claim is withdrawn and
        /// said out loud, which is recoverable — the update is simply not installed, and asking
        /// again works — where silence would leave every later start reporting that an update
        /// did not finish.
        /// </summary>
        public static void Complete(string root)
        {
            var script = _pending;
            if (script == null || OperatingSystem.IsWindows())
                return;

            _pending = null;

            try
            {
                // A null entry terminates the argument vector, which is what execv reads to know
                // where the arguments end.
                Execute("/bin/sh", new[] { "/bin/sh", "-c", script, null });
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }

            UpdateMarker.Clear(root);
            ConsoleSink.WriteLine(LocalizationService.Mark(
                "The update could not be started, so nothing was changed. Try again with update."));
        }

        /// <summary>
        /// The script the process becomes.
        ///
        /// Built apart from being run so it can be read without a terminal to run it in. Every
        /// path is quoted: a folder with a space in its name is ordinary, and an unquoted one
        /// produces a command that fails while looking correct.
        /// </summary>
        public static string BuildScript(
            string root,
            string updaterPath,
            string zipPath,
            string serverEntryName,
            string updaterEntryName,
            string serverPath)
        {
            var script = new StringBuilder();

            // Everything after this is relative to the folder being updated, including the
            // marker the wait watches for.
            script.Append("cd ").Append(Quote(root)).Append(" || exit 1\n");

            // Told not to start anything itself. Coming back is this script's job, and an updater
            // that also started one would leave two servers racing for the same port.
            script.Append(Quote(updaterPath))
                .Append(" --zip ").Append(Quote(zipPath))
                .Append(" --dir ").Append(Quote(root))
                .Append(" --game ").Append(Quote(serverEntryName))
                .Append(" --skip ").Append(Quote(updaterEntryName))
                .Append(" --no-restart\n");

            // The updater removes the marker when it finishes. Bounded, because one that died
            // without removing it would otherwise mean a folder that never starts a server again.
            script.Append("i=0\n");
            script.Append("while [ -e ").Append(Quote(UpdateMarker.FileName))
                .Append(" ] && [ $i -lt ")
                .Append(WaitSeconds.ToString(CultureInfo.InvariantCulture))
                .Append(" ]; do sleep 1; i=$((i+1)); done\n");

            // Exec rather than run, so the new server inherits this process id too and the shell
            // that started all this is still waiting on the thing it thinks it is waiting on.
            script.Append("exec ").Append(Quote(serverPath)).Append('\n');

            return script.ToString();
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("$", "\\$", StringComparison.Ordinal)
                .Replace("`", "\\`", StringComparison.Ordinal) + "\"";
        }

        [DllImport("libc", EntryPoint = "execv", SetLastError = true)]
        private static extern int Execute(string path, string?[] argv);
    }
}
