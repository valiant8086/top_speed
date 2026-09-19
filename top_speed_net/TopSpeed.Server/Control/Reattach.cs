using System;
using System.Globalization;
using System.IO;
using System.Text;
using TopSpeed.Localization;
using TopSpeed.Runtime;
using TopSpeed.Server.Updates;

namespace TopSpeed.Server.Control
{
    /// <summary>
    /// What an attached window does on Linux and macOS when its server leaves to update: becomes
    /// a shell script that waits for the update to finish and the server to come back, and then
    /// becomes the updated program, attached again, in the same window.
    ///
    /// It becomes a shell rather than waiting as itself, and the reason is the same one that
    /// makes the server become a shell on its way out. The files the updater is about to rewrite
    /// are the ones this process is running from, and the runtime maps them straight from the
    /// folder; the updater rewrites each file in place, and a process whose mapped file is
    /// truncated underneath it dies with a bus error. Unix does not lock a running program's
    /// files, which is exactly why nothing stops that from happening. /bin/sh lives elsewhere,
    /// so a shell can wait through the rewrite untouched, and exec keeps the process id, so the
    /// terminal never changes hands. Windows cannot do this at all: there the files are locked
    /// for as long as this process exists, so the window has to leave, and does.
    /// </summary>
    internal static class ControlReattach
    {
        /// <summary>
        /// How long the script waits for the update to finish before giving up: the point past
        /// which everything else treats the marker as abandoned, so nothing waits for the other.
        /// </summary>
        private static int UpdateWaitSeconds => (int)UpdateMarker.AssumeAbandonedAfter.TotalSeconds;

        /// <summary>How long to wait for the server to bind once the update is done.</summary>
        private const int ServerWaitSeconds = 60;

        /// <summary>
        /// Does not return when it succeeds: the process is replaced. Returning means this
        /// platform cannot become another program, or has no shell to become.
        /// </summary>
        public static void WaitAndAttachAgain(string directory)
        {
            Say(LocalizationService.Mark("The server is updating. This window will attach to it again when it is back."));

            var program = Environment.ProcessPath
                ?? Path.Combine(directory, RuntimeAssetResolver.ResolveExecutableFileName(ServerUpdateConfig.Default.ServerEntryName));

            UpdateHandoff.TryBecome("/bin/sh", "-c", BuildScript(directory, program));

            Say(LocalizationService.Mark("The server did not come back. Run the program again to check on it."));
        }

        /// <summary>
        /// The script the window becomes. Built apart from being run so it can be read without a
        /// terminal to run it in.
        /// </summary>
        public static string BuildScript(string directory, string program)
        {
            var script = new StringBuilder();
            script.Append("cd ").Append(UpdateHandoff.Quote(directory)).Append(" || exit 1\n");

            // The update first: the marker is cleared once the files are in place. Bounded, as
            // every wait on this file is.
            script.Append("i=0\n");
            script.Append("while [ -e ").Append(UpdateHandoff.Quote(UpdateMarker.FileName))
                .Append(" ] && [ $i -lt ").Append(UpdateWaitSeconds.ToString(CultureInfo.InvariantCulture))
                .Append(" ]; do sleep 1; i=$((i+1)); done\n");

            // Then the server, which takes a moment to start and to bind its endpoint. The
            // endpoint file says it has, but only a fresh one: a server that was killed leaves
            // its file behind, and a new server replaces that file when it binds, so only a file
            // newer than the moment the update finished counts.
            script.Append("_since=$(mktemp) || exit 1\n");
            script.Append("j=0\n");
            script.Append("while ! [ ").Append(UpdateHandoff.Quote(ControlEndpoint.SocketFileName))
                .Append(" -nt \"$_since\" ] && [ $j -lt ").Append(ServerWaitSeconds.ToString(CultureInfo.InvariantCulture))
                .Append(" ]; do sleep 1; j=$((j+1)); done\n");
            script.Append("rm -f \"$_since\"\n");

            // Bound is a moment ahead of listening, and a missed connection here is a window
            // that says the server is not running when it is.
            script.Append("sleep 1\n");

            // Exec rather than run, so the attached window keeps this process id to the end.
            script.Append("exec ").Append(UpdateHandoff.Quote(program)).Append(" --attach\n");
            return script.ToString();
        }

        private static void Say(string text)
        {
            try
            {
                Console.WriteLine(LocalizationService.Translate(text));
            }
            catch (IOException)
            {
            }
        }
    }
}
