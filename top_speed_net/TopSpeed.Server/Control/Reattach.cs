using System;
using System.IO;
using System.Threading;
using TopSpeed.Localization;
using TopSpeed.Runtime;
using TopSpeed.Server.Updates;

namespace TopSpeed.Server.Control
{
    /// <summary>
    /// What an attached window does on Linux and macOS when its server leaves to update: waits
    /// for the update to finish and the server to come back, then becomes the updated program,
    /// attached again, in the same window.
    ///
    /// Becoming it rather than reconnecting matters. This process is the old version, and the
    /// updater has already replaced the file it was started from. Nothing stops an old process
    /// relaying lines of text to a new server, but nothing keeps it right either, and there is
    /// no reason to stay old when the new one is a step away. On Unix a running program's files
    /// are not locked, so waiting costs nothing; Windows cannot do this at all, because the
    /// files the updater needs to replace stay locked for as long as this process exists.
    /// </summary>
    internal static class ControlReattach
    {
        /// <summary>How often to look, while waiting.</summary>
        private static readonly TimeSpan Glance = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Past the point everything else treats an update as abandoned, plus room for a server
        /// to start once the update is done.
        /// </summary>
        private static readonly TimeSpan Patience = UpdateMarker.AssumeAbandonedAfter + TimeSpan.FromSeconds(30);

        /// <summary>
        /// Does not return when it succeeds: the process is replaced. Returning means the server
        /// did not come back in time, or this platform cannot become another program.
        /// </summary>
        public static void WaitAndAttachAgain(string directory)
        {
            Say(LocalizationService.Mark("The server is updating. This window will attach to it again when it is back."));

            var deadline = DateTime.UtcNow + Patience;

            // First the update: the marker is cleared once the files are in place.
            while (DateTime.UtcNow < deadline && UpdateMarker.UpdateIsUnderWay(directory, out _))
                Thread.Sleep(Glance);

            // Then the server, which takes a moment to start and to bind its endpoint. The
            // endpoint file is what says it has, but only a fresh one: a server that was killed
            // leaves its file behind, and a new server replaces that file when it binds, so the
            // file counts from the moment the update finished and not before.
            var updateFinishedAt = DateTime.UtcNow - Glance;
            var endpoint = ControlEndpoint.SocketPathFor(directory);
            while (DateTime.UtcNow < deadline && !EndpointIsFresh(endpoint, updateFinishedAt))
                Thread.Sleep(Glance);

            if (EndpointIsFresh(endpoint, updateFinishedAt))
            {
                // Bound is not yet listening; a moment covers the gap.
                Thread.Sleep(Glance);

                var program = Environment.ProcessPath
                    ?? Path.Combine(directory, RuntimeAssetResolver.ResolveExecutableFileName(ServerUpdateConfig.Default.ServerEntryName));
                UpdateHandoff.TryBecome(program, "--attach");
            }

            Say(LocalizationService.Mark("The server did not come back. Run the program again to check on it."));
        }

        private static bool EndpointIsFresh(string path, DateTime since)
        {
            try
            {
                return File.Exists(path) && File.GetLastWriteTimeUtc(path) >= since;
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
