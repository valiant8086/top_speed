using System;
using System.Globalization;
using System.IO;

namespace TopSpeed.Server.Updates
{
    /// <summary>
    /// A file recording the last version handed to the updater, and when.
    ///
    /// It exists because everything else the scheduler knows dies with the process, and an install
    /// ends by ending the process. Two questions survive only if they are written down: how long
    /// ago an install was handed over, and which version it was.
    ///
    /// The time stops a server checking again the instant it comes back, which is a request nobody
    /// asked for and an answer already known. The version tells an install that worked from one
    /// that did not: a server which handed over a version and came back still older than it has
    /// found a build that does not do what its name says, and repeating that daily forever helps
    /// nobody.
    ///
    /// The timestamp is written into the file rather than read off the file's own modified time,
    /// which belongs to the filesystem and is rewritten by backup and sync tools without asking.
    /// </summary>
    internal static class UpdateInstallRecord
    {
        public const string FileName = ".last-update";

        /// <summary>
        /// How soon after handing over an install a check is considered redundant. Long enough to
        /// cover a restart, far short of the daily cycle, so nothing anybody does on purpose is
        /// ever refused: a reboot ten minutes later checks as usual.
        /// </summary>
        public static readonly TimeSpan CheckAgainNoSoonerThan = TimeSpan.FromMinutes(5);

        public static string PathIn(string directory)
        {
            return Path.Combine(directory, FileName);
        }

        /// <summary>
        /// Written once the asset is downloaded and the updater is running, immediately before the
        /// server stops for it. Anything that fails earlier never gets this far, so a version that
        /// could not be fetched is never recorded as one that was tried.
        /// </summary>
        public static void Write(string directory, string versionText)
        {
            var contents = versionText + "\n" +
                DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

            Attempt(() => File.WriteAllText(PathIn(directory), contents));
        }

        public static void Clear(string directory)
        {
            Attempt(() => File.Delete(PathIn(directory)));
        }

        /// <summary>
        /// Reads what was handed over last. False for a file that is missing, unreadable or says
        /// anything this does not understand, which leaves the server behaving as though no update
        /// had ever been installed here: the wrong answer costs one redundant check, where refusing
        /// to read it any other way would cost an update.
        /// </summary>
        public static bool TryRead(string directory, out ServerVersion version, out DateTime whenUtc)
        {
            version = default;
            whenUtc = default;

            try
            {
                var path = PathIn(directory);
                if (!File.Exists(path))
                    return false;

                var lines = File.ReadAllLines(path);
                if (lines.Length < 2 || !ServerVersion.TryParse(lines[0].Trim(), out version))
                    return false;

                if (!DateTime.TryParse(
                        lines[1].Trim(),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out var written))
                {
                    return false;
                }

                whenUtc = written.ToUniversalTime();
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
        /// Bookkeeping about an update is never worth failing an update over, so a folder that
        /// cannot be written to loses the record rather than the install.
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
