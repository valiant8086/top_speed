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
        /// </summary>
        private static readonly TimeSpan AssumeAbandonedAfter = TimeSpan.FromMinutes(5);

        public static string PathIn(string directory)
        {
            return Path.Combine(directory, FileName);
        }

        /// <summary>
        /// Raised as the updater is started, rather than by the updater itself, so that it is
        /// already there when the server exits. A manager starts counting from that moment, and
        /// an updater that has not reached its first line yet would have nothing to show for it.
        /// </summary>
        public static void Raise(string directory, int updaterProcessId)
        {
            Attempt(() => File.WriteAllText(
                PathIn(directory),
                updaterProcessId.ToString(CultureInfo.InvariantCulture)));
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
        /// </summary>
        public static bool UpdateIsUnderWay(string directory)
        {
            var path = PathIn(directory);

            try
            {
                if (!File.Exists(path))
                    return false;

                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > AssumeAbandonedAfter)
                    return false;

                if (!int.TryParse(File.ReadAllText(path).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
                    return false;

                return UpdaterIsRunning(pid);
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
        private static bool UpdaterIsRunning(int processId)
        {
            if (processId <= 0)
                return false;

            try
            {
                using var process = Process.GetProcessById(processId);
                return !process.HasExited
                    && process.ProcessName.StartsWith("Updater", StringComparison.OrdinalIgnoreCase);
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
