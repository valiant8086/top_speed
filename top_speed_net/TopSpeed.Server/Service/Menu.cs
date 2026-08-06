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
        public static void Show(string directory, int port)
        {
            var manager = ServiceManagers.ForCurrentPlatform();

            while (true)
            {
                ConsoleSink.WriteLine(LocalizationService.Mark("Service:"));
                ConsoleSink.WriteLine(ServiceCommands.Describe(manager.Query(directory), directory));
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

                ServiceCommands.Execute(action, directory, port, startAutomatically: true);
            }
        }
    }
}
