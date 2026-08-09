using System;
using System.Threading;
using TopSpeed.Localization;
using TopSpeed.Server.Commands;
using TopSpeed.Server.Control;
using TopSpeed.Server.Logging;

namespace TopSpeed.Server.Service
{
    /// <summary>
    /// Facts about how this process stands in relation to the service, which several parts of
    /// the program need and none of them owns.
    /// </summary>
    internal static class ServiceRuntime
    {
        /// <summary>
        /// Whether a service manager is running this process rather than a person.
        ///
        /// It decides whether a console session is offered, whether the updater may start the
        /// program again once it has replaced it, and whether this process may ask for rights.
        /// A managed process cannot: there is no desktop for a consent prompt to appear on.
        /// </summary>
        public static bool IsRunningAsService { get; set; }

        /// <summary>
        /// Whether the server is stopping in order to be started again, which is what applying
        /// an update means. A service manager treats a stop as final unless it is told
        /// otherwise, so this is the difference between a server that updates itself and one
        /// that stays down until somebody notices.
        /// </summary>
        public static bool StoppingToRestart { get; set; }

        /// <summary>
        /// Whether this window is giving the folder up so the service can have it. The server it
        /// was talking to, or running, stops first; once everything is released the service is
        /// started and this window connects to it.
        /// </summary>
        public static bool HandingOverToService { get; set; }
    }

    /// <summary>
    /// Everything that can be asked about this folder's service, from wherever it is asked.
    ///
    /// Three places ask: a server running in its own window, a window attached to a server
    /// elsewhere, and a command line carrying a flag. They differ in one respect, which is
    /// whether they can stop the server currently holding the folder, so that is the one thing
    /// handed in. Deliberately free of any dependency on loaded configuration, since two of the
    /// three have none.
    /// </summary>
    internal static class ServiceConsole
    {
        /// <param name="countPlayers">
        /// How many players would be disconnected by stopping the server, when that can be
        /// known. A server running in this process can say; a window attached to one elsewhere
        /// cannot, and passes nothing.
        /// </param>
        public static void Run(string arguments, string directory, Action? stopHostingServer, Func<int>? countPlayers = null)
        {
            var verb = (arguments ?? string.Empty).Trim();
            if (verb.Length == 0)
            {
                ShowMenu(directory, stopHostingServer, countPlayers);
                return;
            }

            if (!TryParseVerb(verb, out var action))
            {
                ConsoleSink.WriteLineFormat(
                    LocalizationService.Mark("\"{0}\" is not something the service command understands."),
                    verb);
                ConsoleSink.WriteLine(LocalizationService.Mark(
                    "Use: service, or service with one of install, uninstall, start, stop, restart, status."));
                return;
            }

            Perform(action, directory, stopHostingServer, countPlayers);
        }

        private static bool TryParseVerb(string verb, out ServiceAction action)
        {
            switch (verb.ToLowerInvariant())
            {
                case "install":
                    action = ServiceAction.Install;
                    return true;
                case "uninstall":
                    action = ServiceAction.Uninstall;
                    return true;
                case "start":
                    action = ServiceAction.Start;
                    return true;
                case "stop":
                    action = ServiceAction.Stop;
                    return true;
                case "restart":
                    action = ServiceAction.Restart;
                    return true;
                case "status":
                    action = ServiceAction.Status;
                    return true;
                default:
                    action = ServiceAction.Status;
                    return false;
            }
        }

        private static void ShowMenu(string directory, Action? stopHostingServer, Func<int>? countPlayers)
        {
            var manager = ServiceManagers.ForCurrentPlatform();

            while (true)
            {
                ConsoleSink.WriteLine(LocalizationService.Mark("Service:"));
                ConsoleSink.WriteLine(ServiceCommands.Describe(manager.Query(directory), directory));

                // Read each time round rather than registered once, so it is right after
                // somebody changes it.
                var port = ServiceIdentity.ReadConfiguredPort(directory);
                if (port > 0)
                    ConsoleSink.WriteLineFormat(LocalizationService.Mark("Configured port: {0}."), port);

                ConsoleSink.WriteLine(LocalizationService.Mark("1. Install"));
                ConsoleSink.WriteLine(LocalizationService.Mark("2. Uninstall"));
                ConsoleSink.WriteLine(LocalizationService.Mark("3. Start"));
                ConsoleSink.WriteLine(LocalizationService.Mark("4. Stop"));
                ConsoleSink.WriteLine(LocalizationService.Mark("5. Restart"));
                ConsoleSink.WriteLine(LocalizationService.Mark("0. Back"));

                if (!CommandInput.TryReadLine(LocalizationService.Translate(LocalizationService.Mark("Enter option number:")), out var raw))
                    return;

                var choice = raw.Trim();
                if (choice.Length == 0)
                    continue;

                if (string.Equals(choice, "0", StringComparison.Ordinal))
                    return;

                ServiceAction action;
                switch (choice)
                {
                    case "1":
                        action = ServiceAction.Install;
                        break;
                    case "2":
                        action = ServiceAction.Uninstall;
                        break;
                    case "3":
                        action = ServiceAction.Start;
                        break;
                    case "4":
                        action = ServiceAction.Stop;
                        break;
                    case "5":
                        action = ServiceAction.Restart;
                        break;
                    default:
                        ConsoleSink.WriteLine(LocalizationService.Mark("That is not one of the choices."));
                        continue;
                }

                if (Perform(action, directory, stopHostingServer, countPlayers))
                    return;
            }
        }

        /// <summary>
        /// Carries out one action. Returns true when the caller should stop asking for more,
        /// which happens when the folder is being handed over and this window is on its way to
        /// becoming a connection to the service.
        /// </summary>
        private static bool Perform(ServiceAction action, string directory, Action? stopHostingServer, Func<int>? countPlayers)
        {
            if (ServiceRuntime.IsRunningAsService)
            {
                // Reached only by something speaking to a service directly, since a window
                // attached to one answers the service command itself rather than passing it on.
                // A consent prompt would have nowhere to appear, so say who can be asked.
                ConsoleSink.WriteLine(LocalizationService.Mark(
                    "This server is the service, so it cannot install or control itself. Run the server program from its own folder and use its service menu, which can ask for the rights this needs."));
                return false;
            }

            var startingUp = action == ServiceAction.Start || action == ServiceAction.Restart;
            if (startingUp)
            {
                var status = ServiceManagers.ForCurrentPlatform().Query(directory);

                // Asked before anything is offered, because stopping a server is only worth
                // discussing when there is a service to hand the folder to and it is not already
                // holding it. Both of these answer in one line and neither is worth a question.
                var pointless = status.State == ServiceInstallState.NotInstalled
                    || (action == ServiceAction.Start && status.State == ServiceInstallState.Running);

                if (!pointless && stopHostingServer != null && ControlTransport.EndpointExists(directory))
                    return HandOverToService(directory, stopHostingServer, countPlayers);
            }

            ServiceCommands.Execute(action, directory, startAutomatically: true);
            return false;
        }

        /// <summary>
        /// Gives the folder to the service and stays to talk to it.
        ///
        /// Only one server may run from a folder, so starting the service means the server
        /// holding it has to stop. This window does not: it stops being a server, or stops
        /// talking to one, and once everything is released the service is started and this same
        /// window connects to it. The actual handover happens once the caller has unwound and
        /// the folder is genuinely free, which is why this only asks and marks.
        ///
        /// Asking at all is reserved for when somebody would notice, and when this window is in
        /// a position to say so.
        /// </summary>
        private static bool HandOverToService(string directory, Action stopHostingServer, Func<int>? countPlayers)
        {
            if (!AgreedToDisconnectPlayers(countPlayers))
            {
                ConsoleSink.WriteLine(LocalizationService.Mark("Left running. The service was not started."));
                return false;
            }

            ServiceRuntime.HandingOverToService = true;
            ConsoleSink.WriteLine(LocalizationService.Mark(
                "Stopping the server, then starting the service. This window will connect to the service once it is up."));

            stopHostingServer();
            return true;
        }

        /// <summary>
        /// Whether to go ahead, asked only when there is somebody to be asked about.
        ///
        /// A window attached to a server elsewhere cannot count who is on it, and a question that
        /// cannot name what it would interrupt is a guess dressed as care. Stopping the service
        /// outright disconnects exactly the same players and asks nothing, so a guess here would
        /// not even be the strict path, only the noisier one.
        /// </summary>
        private static bool AgreedToDisconnectPlayers(Func<int>? countPlayers)
        {
            var players = countPlayers?.Invoke() ?? 0;
            if (players <= 0)
                return true;

            return Confirm(LocalizationService.Format(
                LocalizationService.Mark("{0} players are connected and will be disconnected. Stop the server and start the service? (y/n)"),
                players));
        }

        /// <summary>
        /// Starts the service now that the folder is free, and connects this window to it.
        ///
        /// Called once nothing is left holding the folder, which is the whole reason it is not
        /// done where it was asked for: a server cannot release what it is using and watch what
        /// happens next in the same breath.
        /// </summary>
        public static int CompleteHandover(string directory)
        {
            // Being asked to stop and being stopped are not the same moment. A service refuses to
            // start while the folder's endpoint is still held, and cannot be started at all while
            // the manager is still stopping the last one, so both of those would be reported as a
            // handover that failed when all that was wrong was the timing.
            if (!WaitForFolderToBeFree(directory, TimeSpan.FromSeconds(30)))
            {
                ConsoleSink.WriteLine(LocalizationService.Mark(
                    "The server has not finished stopping, so the service was not started."));
                return 1;
            }

            // Through the same route as a start asked for directly, rather than straight at the
            // manager. A service registered before this program granted interactive accounts the
            // right to start it can still only be started with administrator rights, and that
            // route is the one that knows how to ask for them.
            if (ServiceCommands.Execute(ServiceAction.Start, directory, startAutomatically: true) != 0)
                return NothingRunningHere();

            return Reattach(directory, TimeSpan.FromSeconds(60));
        }

        /// <summary>
        /// Waits for the new server to be ready and connects to it.
        ///
        /// A service reports itself running the moment its process is up, which is before that
        /// process has read its settings and opened anything, so the first few attempts finding
        /// nothing there is the normal course of events rather than a failure. Only the endpoint
        /// answers the question being asked, so it is tried until it does or until long enough
        /// has passed that something is genuinely wrong.
        /// </summary>
        private static int Reattach(string directory, TimeSpan limit)
        {
            ConsoleSink.WriteLine(LocalizationService.Mark("Waiting for the service to be ready..."));

            var deadline = DateTime.UtcNow + limit;
            while (DateTime.UtcNow < deadline)
            {
                if (ControlTransport.EndpointExists(directory))
                {
                    // Anything other than nothing being there has been reported by the client
                    // itself and will not come right by asking again.
                    var outcome = ControlClient.Run(directory);
                    if (outcome != ControlClientOutcome.NoServerRunning)
                        return outcome == ControlClientOutcome.SessionEnded ? 0 : 1;
                }

                Thread.Sleep(500);
            }

            if (ServiceManagers.ForCurrentPlatform().Query(directory).State == ServiceInstallState.Running)
            {
                // Worth telling apart from nothing running: the players are fine and only this
                // window missed out, so the remedy is to attach again rather than to start
                // anything.
                ConsoleSink.WriteLine(LocalizationService.Mark(
                    "The service is running, but this window could not attach to it. Run the server program from this folder again to attach."));
                return 1;
            }

            return NothingRunningHere();
        }

        private static int NothingRunningHere()
        {
            // Said plainly because the folder was given up on the strength of this working. The
            // remedy is the ordinary one, and saying so is better than leaving somebody to
            // wonder whether anything is still serving players.
            ConsoleSink.WriteLine(LocalizationService.Mark(
                "No server is running from this folder now. Run the server program again to start one."));
            return 1;
        }

        /// <summary>
        /// Waits for the old server to let the folder go. The endpoint is the plainest statement
        /// of that: it is gone once the server holding it has finished stopping, whoever stopped
        /// it and however it was asked.
        /// </summary>
        private static bool WaitForFolderToBeFree(string directory, TimeSpan limit)
        {
            var deadline = DateTime.UtcNow + limit;
            while (DateTime.UtcNow < deadline)
            {
                if (!ControlTransport.EndpointExists(directory))
                    return true;

                Thread.Sleep(250);
            }

            return false;
        }

        private static bool Confirm(string question)
        {
            if (!CommandInput.TryReadLine(question, out var answer))
                return false;

            var text = answer.Trim();

            // Anything that is not plainly yes is a no, because the cost of misreading it is a
            // server stopped by somebody who did not ask for that.
            return string.Equals(text, "y", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }
}
