using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace TopSpeed.Server.Updates
{
    /// <summary>
    /// A file that exists only while an update is being written into the folder, holding the
    /// process id of the updater doing the writing.
    ///
    /// It answers two questions. systemd and launchd cannot be told to wait for something that
    /// is not a process, so their units wait for this file to go away before starting the server
    /// on a folder that may still be half of each version. And a person who runs the program
    /// during an update, expecting to attach, reads it and leaves rather than locking the files
    /// out from under the updater.
    ///
    /// The id is what makes the second use safe. Existence alone cannot tell an update in
    /// progress from one whose updater died, and refusing to start on a file nobody will ever
    /// remove would wedge the folder for good.
    /// </summary>
    internal static class UpdateMarker
    {
        public const string FileName = ".updating";

        /// <summary>
        /// Longer than any unpack and shorter than anybody's patience. Past it the file is
        /// treated as abandoned however alive the process looks, so that an updater which hung
        /// rather than died costs a wait rather than a folder that can never be started again.
        ///
        /// Everything that waits for this marker waits this long, which is why it is not private.
        /// The systemd unit, the launchd job and the handoff a console update becomes each sit in
        /// a loop watching for the file to go, and each used to carry its own number: sixty
        /// seconds in the two unit files and two minutes in the handoff, against the five minutes
        /// this says the file is worth believing. A service could therefore start a server while
        /// an update it should still have been waiting for was legitimately in progress.
        /// </summary>
        public static readonly TimeSpan AssumeAbandonedAfter = TimeSpan.FromMinutes(5);

        public static string PathIn(string directory)
        {
            return Path.Combine(directory, FileName);
        }

        /// <summary>Written on the second line when the updater will open a window itself.</summary>
        private const string WindowReturnsTag = "window-returns";

        /// <summary>
        /// Written on the third line when the process named is not the updater but the server
        /// that raised the marker, which becomes the updater by replacing itself. It is one
        /// process id wearing several names in turn, so it cannot be judged by its name.
        /// </summary>
        private const string BecomesTheUpdaterTag = "becomes-the-updater";

        /// <summary>
        /// Raised as the updater is started, rather than by the updater itself, so that it is
        /// already there when the server exits. A manager starts counting from that moment, and
        /// an updater that has not reached its first line yet would have nothing to show for it.
        ///
        /// What comes back afterwards is recorded because only the server knows it. An update to
        /// a server somebody started themselves ends with the updater opening it again, and an
        /// update to a service ends with a service nobody can see. Advice to run the program
        /// again is right for one and collides with the other.
        /// </summary>
        public static void Raise(string directory, int updaterProcessId, bool windowComesBackByItself)
        {
            Raise(directory, updaterProcessId, windowComesBackByItself, becomesTheUpdater: false);
        }

        /// <summary>
        /// Raised by a server about to replace itself with the update rather than start one. The
        /// id recorded is its own, and it keeps that id through every program it becomes on the
        /// way, so the marker is judged by whether it is alive and not by what it is called at
        /// the moment anybody looks. Judged by name it read as no update at all for most of the
        /// update, including the moment an attached window lost its connection and asked.
        /// </summary>
        public static void RaiseForHandoff(string directory, int ownProcessId)
        {
            Raise(directory, ownProcessId, windowComesBackByItself: true, becomesTheUpdater: true);
        }

        private static void Raise(string directory, int processId, bool windowComesBackByItself, bool becomesTheUpdater)
        {
            var contents = processId.ToString(CultureInfo.InvariantCulture);
            contents += "\n" + (windowComesBackByItself ? WindowReturnsTag : string.Empty);
            if (becomesTheUpdater)
                contents += "\n" + BecomesTheUpdaterTag;

            Attempt(() => File.WriteAllText(PathIn(directory), contents));
        }

        /// <summary>Removes it, reporting whether there was one to remove.</summary>
        public static bool Clear(string directory)
        {
            var path = PathIn(directory);
            var existed = false;
            Attempt(() =>
            {
                existed = File.Exists(path);
                File.Delete(path);
            });

            return existed;
        }

        /// <summary>
        /// Whether an update is being written right now, as opposed to having been abandoned
        /// partway. Only ever answered yes for a file that is recent and whose updater is still
        /// there to finish the job.
        ///
        /// <paramref name="windowComesBackByItself"/> says whether the update ends with the
        /// updater opening the server again, which decides whether somebody should be told to
        /// run it again or to leave it alone. False when the file does not say, which is a file
        /// written before this was recorded and cannot also be recent and running.
        /// </summary>
        public static bool UpdateIsUnderWay(string directory, out bool windowComesBackByItself)
        {
            windowComesBackByItself = false;
            var path = PathIn(directory);

            try
            {
                if (!File.Exists(path))
                    return false;

                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > AssumeAbandonedAfter)
                    return false;

                var lines = File.ReadAllLines(path);
                if (lines.Length == 0 ||
                    !int.TryParse(lines[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
                    return false;

                var becomesTheUpdater = lines.Length > 2
                    && string.Equals(lines[2].Trim(), BecomesTheUpdaterTag, StringComparison.Ordinal);
                if (!(becomesTheUpdater ? ProcessIsAlive(pid) : UpdaterIsRunning(pid)))
                    return false;

                windowComesBackByItself = lines.Length > 1
                    && string.Equals(lines[1].Trim(), WindowReturnsTag, StringComparison.Ordinal);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// The name is checked as well as the number because process ids are handed out again
        /// once they are free, and a stranger wearing the same one would otherwise keep a folder
        /// shut for as long as it happened to run.
        /// </summary>
        /// <summary>
        /// For the marker a server raises about itself. Its id cannot have been handed to a
        /// stranger while it is still the one in the chain, and a chain that died leaves a file
        /// that goes stale on the same clock as any other.
        /// </summary>
        private static bool ProcessIsAlive(int processId)
        {
            if (processId <= 0)
                return false;

            try
            {
                using var process = Process.GetProcessById(processId);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static bool UpdaterIsRunning(int processId)
        {
            if (processId <= 0)
                return false;

            try
            {
                using var process = Process.GetProcessById(processId);
                return !process.HasExited
                    && (process.ProcessName.StartsWith(ServerUpdateConfig.Default.UpdaterEntryName, StringComparison.OrdinalIgnoreCase)
                        || process.ProcessName.StartsWith(ServerUpdateConfig.LegacyUpdaterEntryName, StringComparison.OrdinalIgnoreCase));
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
        }

        /// <summary>
        /// Bookkeeping about an update is never worth failing an update over, so a folder that
        /// cannot be written to loses the wait rather than the install.
        /// </summary>
        private static void Attempt(Action action)
        {
            try
            {
                action();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
