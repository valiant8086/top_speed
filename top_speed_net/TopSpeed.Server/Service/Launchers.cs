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
    /// these exist because the alternative on a desktop is pressing enter on a program with no
    /// extension, which one file manager runs without a console, two ask about, and the most
    /// common one refuses outright.
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
                WriteDesktopEntry(directory);
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

        /// <summary>
        /// The freedesktop way of saying "this is a thing you can start", which is the only one
        /// every desktop understands.
        ///
        /// Terminal=true is the whole point of it. The server is a console program: started
        /// without one it runs perfectly well and holds the port with nowhere to type shutdown,
        /// which looks exactly like nothing having happened.
        /// </summary>
        private static void WriteDesktopEntry(string directory)
        {
            var path = Path.Combine(directory, "Start TopSpeed Server.desktop");
            if (File.Exists(path))
                return;

            File.WriteAllText(path, BuildDesktopEntry(directory));

            // GNOME wants one of these marked runnable before it will offer to launch it, on top
            // of asking the person to allow it once. The others do not care, and it costs
            // nothing to be the kind of file the strictest of them will consider.
            MakeRunnable(path);
        }

        /// <summary>Built apart from being written, for the same reason as the unit files are.</summary>
        public static string BuildDesktopEntry(string directory)
        {
            var executable = ServiceIdentity.ExecutablePathFor(directory);
            var folder = ServiceIdentity.DisplayPath(directory);

            var entry = new StringBuilder();
            entry.Append("[Desktop Entry]\n");
            entry.Append("Type=Application\n");
            entry.Append("Version=1.0\n");
            entry.Append("Name=Start TopSpeed Server\n");
            entry.Append("Comment=Run the TopSpeed multiplayer race server kept in this folder\n");
            entry.Append("Exec=\"").Append(executable).Append("\"\n");
            entry.Append("Path=").Append(folder).Append('\n');
            entry.Append("Terminal=true\n");
            entry.Append("Categories=Game;\n");
            return entry.ToString();
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
