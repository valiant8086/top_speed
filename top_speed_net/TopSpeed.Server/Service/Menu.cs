using System;
using TopSpeed.Localization;
using TopSpeed.Server.Commands;
using TopSpeed.Server.Logging;

namespace TopSpeed.Server.Service
{
    /// <summary>
    /// Whether this process is the service itself, as opposed to a copy somebody launched.
    ///
    /// It matters because a service cannot show a consent prompt. Asked to install or remove
    /// itself it would either fail silently or leave a window nobody can see waiting for an
    /// answer, so it says who should be asked instead.
    /// </summary>
    internal static class ServiceRuntime
    {
        public static bool IsRunningAsService { get; set; }
    }

    /// <summary>
    /// Everything somebody can ask for about this folder's service, from wherever they ask it.
    ///
    /// There are three places: a server running in its own window, a window attached to a
    /// server elsewhere, and a plain command line carrying one of the flags. They differ in
    /// exactly one respect, which is who is able to stop the server currently holding the
    /// folder, so that is the one thing handed in. Everything else is shared, because three
    /// copies of "start the service" would be three things to keep in step and only one of them
    /// would ever get tested.
    ///
    /// Deliberately free of any dependency on loaded configuration, since two of those three
    /// callers have none.
    /// </summary>
    internal static class ServiceConsole
    {
        /// <param name="stopHostingServer">
        /// How to stop the server holding this folder, when the caller can offer anything.
        /// A server in its own window stops itself; a window attached to one asks it to stop;
        /// a bare command line passes null, having neither a server of its own nor a
        /// connection to ask through.
        /// </param>
        public static void Run(string arguments, string directory, Action? stopHostingServer)
        {
            var verb = (arguments ?? string.Empty).Trim();
            if (verb.Length == 0)
            {
                ShowMenu(directory, stopHostingServer);
                return;
            }

            if (!TryParseVerb(verb, out var action))
            {
                ConsoleSink.WriteLineFormat(
                    LocalizationService.Mark("\"{0}\" is not something the service command understands."),
                    verb);
                ConsoleSink.WriteLine(LocalizationService.Mark(
                    "Use: service, or service with one of install, uninstall, start, stop, status."));
                return;
            }

            Perform(action, directory, stopHostingServer);
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
                case "status":
                    action = ServiceAction.Status;
                    return true;
                default:
                    action = ServiceAction.Status;
                    return false;
            }
        }

        private static void ShowMenu(string directory, Action? stopHostingServer)
        {
            var manager = ServiceManagers.ForCurrentPlatform();

            while (true)
            {
                ConsoleSink.WriteLine(LocalizationService.Mark("Service:"));
                ConsoleSink.WriteLine(ServiceCommands.Describe(manager.Query(directory), directory));

                // Read every time round rather than registered once, so it is right after
                // somebody changes it. The service list deliberately does not carry the port
                // for exactly this reason: a registration is written once and never revisited.
                var port = ServiceIdentity.ReadConfiguredPort(directory);
                if (port > 0)
                    ConsoleSink.WriteLineFormat(LocalizationService.Mark("Configured port: {0}."), port);
                ConsoleSink.WriteLine(LocalizationService.Mark("1. Install"));
                ConsoleSink.WriteLine(LocalizationService.Mark("2. Uninstall"));
                ConsoleSink.WriteLine(LocalizationService.Mark("3. Start"));
                ConsoleSink.WriteLine(LocalizationService.Mark("4. Stop"));
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
                    default:
                        ConsoleSink.WriteLine(LocalizationService.Mark("That is not one of the choices."));
                        continue;
                }

                if (Perform(action, directory, stopHostingServer))
                    return;
            }
        }

        /// <summary>
        /// Carries out one action. Returns true when the caller should stop asking for more,
        /// which happens when the server this is running inside is on its way down.
        /// </summary>
        private static bool Perform(ServiceAction action, string directory, Action? stopHostingServer)
        {
            if (ServiceRuntime.IsRunningAsService)
            {
                // Reachable only if something typed this straight at a service, since a window
                // attached to one answers the service command itself rather than passing it on.
                // The prompt would have nowhere to appear, so say who can be asked instead.
                ConsoleSink.WriteLine(LocalizationService.Mark(
                    "This server is the service, so it cannot install or control itself. Run the server program from its own folder and use its service menu, which can ask for the rights this needs."));
                return false;
            }

            if (action == ServiceAction.Start
                && stopHostingServer != null
                && Control.ControlTransport.EndpointExists(directory))
            {
                return StopTheServerAndStartTheService(directory, stopHostingServer);
            }

            ServiceCommands.Execute(action, directory, startAutomatically: true);
            return false;
        }

        /// <summary>
        /// Hands the starting over to a copy that will outlive the server being stopped.
        ///
        /// Only one server may run from a folder, and asking for a start from anywhere that can
        /// offer a stop means one is running, so the folder is held by the very thing being
        /// asked to free it. Nothing can both go away and watch what happens next, so a separate
        /// copy waits for the folder and does the starting.
        ///
        /// The order matters. The copy is launched first, and the server is stopped only once it
        /// actually started, so refusing the rights prompt leaves a running server exactly as it
        /// was rather than stopping it for a start that was never going to happen.
        /// </summary>
        private static bool StopTheServerAndStartTheService(string directory, Action stopHostingServer)
        {
            ConsoleSink.WriteLine(LocalizationService.Mark(
                "Only one server can run from this folder, so the one running now has to stop before the service can start."));

            if (!Confirm(LocalizationService.Translate(LocalizationService.Mark(
                "Stop it now and start the service? (y/n)"))))
            {
                ConsoleSink.WriteLine(LocalizationService.Mark("Left running. The service was not started."));
                return false;
            }

            if (!ServiceCommands.LaunchDetached(ServiceAction.StartWhenFree, directory))
                return false;

            ConsoleSink.WriteLine(LocalizationService.Mark(
                "The server is stopping. The service will start by itself once it has, and a separate window will say whether it did."));

            stopHostingServer();
            return true;
        }

        private static bool Confirm(string question)
        {
            if (!CommandInput.TryReadLine(question, out var answer))
                return false;

            var text = answer.Trim();

            // Anything that is not plainly yes is treated as no, because the cost of
            // misreading it is a server stopped by somebody who did not ask for that.
            return string.Equals(text, "y", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }
}
