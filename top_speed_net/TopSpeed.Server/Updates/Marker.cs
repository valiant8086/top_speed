using System;
using System.IO;

namespace TopSpeed.Server.Updates
{
    /// <summary>
    /// A file that exists only while an update is being written into the folder.
    ///
    /// systemd and launchd start the server again themselves once it exits, and neither can be
    /// told to wait for something that is not a process. Their units run a short wait ahead of
    /// the server, and this is what that wait watches: while the file is there the folder may be
    /// half of one version and half of another, and a server started on that loads whichever
    /// halves were in place at the moment it began.
    ///
    /// Removed from three places, because the wait costs a minute if it is left behind and the
    /// updater is the one part of the folder an update never replaces: a folder installed before
    /// this existed keeps an updater that has never heard of it. Clearing it at startup as well
    /// means that costs one slow start once rather than every start forever.
    /// </summary>
    internal static class UpdateMarker
    {
        public const string FileName = ".updating";

        public static string PathIn(string directory)
        {
            return Path.Combine(directory, FileName);
        }

        /// <summary>
        /// Raised before the server hands off, rather than by the updater once it is running.
        /// The manager starts counting from the moment the server exits, and an updater that has
        /// not reached its first line yet would not have raised anything to find.
        /// </summary>
        public static void Raise(string directory)
        {
            Attempt(() => File.WriteAllText(PathIn(directory), string.Empty));
        }

        public static void Clear(string directory)
        {
            Attempt(() => File.Delete(PathIn(directory)));
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
