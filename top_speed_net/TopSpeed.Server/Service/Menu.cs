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
    /// The service menu, deliberately free of any dependency on a loaded configuration so that
    /// the same code serves both places it is reached from: a server showing its own options,
    /// and a window attached to a server that cannot do this for itself.
    /// </summary>
    internal static class ServiceMenu
    {
        /// <param name="stopLocalServer">
        /// How to stop the server this menu is running inside, when there is one. Null from a
        /// window attached to a server elsewhere, which has no server of its own to stop.
        /// </param>
        public static void Show(string directory, Action? stopLocalServer = null)
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
                ConsoleSink.WriteLine(LocalizationService.Mark("2. Remove"));
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

                if (ServiceRuntime.IsRunningAsService)
                {
                    // The prompt would have nowhere to appear. Whoever is reading this is at a
                    // machine and can do it from there.
                    ConsoleSink.WriteLine(LocalizationService.Mark(
                        "This server is the service, so it cannot install or control itself. Run the server program from its own folder and use its service menu, which can ask for the rights this needs."));
                    continue;
                }

                if (action == ServiceAction.Start
                    && stopLocalServer != null
                    && Control.ControlTransport.EndpointExists(directory))
                {
                    if (StopThisServerAndStartTheService(directory, stopLocalServer))
                        return;

                    continue;
                }

                ServiceCommands.Execute(action, directory, startAutomatically: true);
            }
        }

        /// <summary>
        /// Hands the starting over to a copy that will outlive this one, then stops this server.
        ///
        /// This is the only way the service can ever be started from here. Only one server may
        /// run from a folder, and reaching this menu at all means one is running, so the folder
        /// is always occupied by the very process being asked to free it. It cannot stop itself
        /// and then watch what happens, so something else has to do the watching.
        ///
        /// The order matters. The copy is launched first, and only if it actually started, so
        /// that refusing the rights prompt leaves the running server exactly as it was rather
        /// than stopping it for a start that was never going to happen.
        ///
        /// Returns true when this server is on its way down and the menu should stop.
        /// </summary>
        private static bool StopThisServerAndStartTheService(string directory, Action stopLocalServer)
        {
            ConsoleSink.WriteLine(LocalizationService.Mark(
                "Only one server can run from this folder, so this one has to stop before the service can start."));

            if (!Confirm(LocalizationService.Translate(LocalizationService.Mark(
                "Stop this server now and start the service? (y/n)"))))
            {
                ConsoleSink.WriteLine(LocalizationService.Mark("Left running. The service was not started."));
                return false;
            }

            if (!ServiceCommands.LaunchDetached(ServiceAction.StartWhenFree, directory))
                return false;

            ConsoleSink.WriteLine(LocalizationService.Mark(
                "This server is stopping. The service will start by itself once it has, and a separate window will say whether it did."));

            stopLocalServer();
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
