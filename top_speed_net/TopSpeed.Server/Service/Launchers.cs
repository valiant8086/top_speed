using System;
using System.IO;
using System.Text;

namespace TopSpeed.Server.Service
{
    /// <summary>
    /// Ways to start the server that do not involve a terminal, written into its own folder.
    ///
    /// They are written here rather than shipped in the download because the only thing that
    /// makes them work is an absolute path, and a file in an archive cannot know where somebody
    /// will unpack it. The server does know: it is running from there.
    ///
    /// Nothing here is required. A server started from a terminal works as it always did, and
    /// this exists because the alternative on a desktop is pressing enter on a program with no
    /// extension, which every file manager treats differently.
    ///
    /// A script is the whole of it. A .desktop entry was tried and removed: it is the only form
    /// every desktop claims to understand, but GNOME will not run one until the person who
    /// downloaded it marks it trusted by hand, that trust is stored per user rather than in the
    /// file, and so it cannot be shipped working. What it did instead was fail silently, which is
    /// worse than not being there.
    /// </summary>
    internal static class Launchers
    {
        /// <summary>
        /// Written once and then left alone, including when it has been edited. Somebody who
        /// changed one of these meant to, and a launcher that quietly restores itself every time
        /// the server starts is a file nobody can get rid of.
        /// </summary>
        public static void WriteIfMissing(string directory)
        {
            if (OperatingSystem.IsWindows())
                return;

            try
            {
                if (OperatingSystem.IsMacOS())
                {
                    WriteScript(Path.Combine(directory, "Start Server.command"));
                    return;
                }

                WriteScript(Path.Combine(directory, "start-server.sh"));
            }
            catch (IOException)
            {
                // A folder that cannot be written to has bigger problems than this, and none of
                // them are worth refusing to start a server over.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <summary>
        /// Finds its own folder rather than trusting the one it is run from, because a file
        /// manager is entitled to run it from anywhere and usually does.
        /// </summary>
        private static void WriteScript(string path)
        {
            if (File.Exists(path))
                return;

            File.WriteAllText(path, BuildScript());
            MakeRunnable(path);
        }

        /// <summary>Built apart from being written, so it can be read without a desktop to run it on.</summary>
        public static string BuildScript()
        {
            var script = new StringBuilder();
            script.Append("#!/bin/sh\n");
            script.Append("# Starts the TopSpeed server kept in this folder.\n");
            script.Append("cd \"$(dirname \"$0\")\" || exit 1\n");
            script.Append("exec ./TopSpeed.Server\n");
            return script.ToString();
        }

        private static void MakeRunnable(string path)
        {
            if (OperatingSystem.IsWindows())
                return;

            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }
}
