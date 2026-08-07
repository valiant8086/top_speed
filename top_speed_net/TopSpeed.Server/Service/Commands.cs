using System;
using System.ComponentModel;
using System.Diagnostics;
using TopSpeed.Localization;
using TopSpeed.Server.Logging;

namespace TopSpeed.Server.Service
{
    internal enum ServiceAction
    {
        Status,
        Install,
        Uninstall,
        Start,
        Stop,
        Restart
    }

    /// <summary>
    /// Carries out a service action on behalf of whoever asked, obtaining the rights to do it
    /// if they are needed and this process does not have them.
    ///
    /// Elevation belongs here rather than deeper down because only a process a person launched
    /// themselves may ask for it. A service cannot show a consent prompt, so a running server
    /// must never be the thing that tries; the copy the owner started is.
    /// </summary>
    internal static class ServiceCommands
    {
        private const int ErrorCancelled = 1223;

        public static int Execute(ServiceAction action, string directory, bool startAutomatically)
        {
            var manager = ServiceManagers.ForCurrentPlatform();

            if (action == ServiceAction.Status)
            {
                ConsoleSink.WriteLine(Describe(manager.Query(directory), directory));
                return 0;
            }

            var result = action switch
            {
                ServiceAction.Install => manager.Install(directory, startAutomatically),
                ServiceAction.Uninstall => manager.Uninstall(directory),
                ServiceAction.Start => manager.Start(directory),
                ServiceAction.Stop => manager.Stop(directory),
                ServiceAction.Restart => Restart(manager, directory),
                _ => ServiceActionResult.Failed(string.Empty)
            };

            if (result.NeedsElevation && OperatingSystem.IsWindows())
                return Elevate(action, directory);

            ConsoleSink.WriteLine(result.Message);
            return result.Succeeded ? 0 : 1;
        }

        /// <summary>
        /// Runs this same program again for the one action, asking the system for administrator
        /// rights as it goes. The consent prompt is the system's own, so nothing here ever sees
        /// or asks for a password.
        /// </summary>
        private static int Elevate(ServiceAction action, string directory)
        {
            ConsoleSink.WriteLine(LocalizationService.Mark(
                "This needs administrator rights. Approve the prompt to continue."));

            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = ServiceIdentity.ExecutablePathFor(directory),
                    WorkingDirectory = directory,
                    // Both are required together: the verb is only honoured by the shell, and
                    // the shell is only used when the process is not started directly.
                    UseShellExecute = true,
                    Verb = "runas"
                };
                info.ArgumentList.Add(FlagFor(action));

                using var process = Process.Start(info);
                if (process == null)
                    return 1;

                process.WaitForExit();
                return process.ExitCode;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                ConsoleSink.WriteLine(LocalizationService.Mark("Cancelled. Nothing was changed."));
                return 1;
            }
            catch (Win32Exception ex)
            {
                ConsoleSink.WriteLine(ex.Message);
                return 1;
            }
        }

        /// <summary>
        /// Stops the service and starts it again, reporting both so that a stop which failed is
        /// not mistaken for a restart that worked. On the platforms this program does not drive
        /// directly, both halves are instructions, and both are worth showing.
        /// </summary>
        private static ServiceActionResult Restart(IServiceManager manager, string directory)
        {
            var stopped = manager.Stop(directory);
            if (!stopped.Succeeded)
                return stopped;

            var started = manager.Start(directory);
            return started.Succeeded
                ? ServiceActionResult.Ok(stopped.Message + "\n" + started.Message)
                : started;
        }

        public static string FlagFor(ServiceAction action)
        {
            return action switch
            {
                ServiceAction.Install => "--install-service",
                ServiceAction.Uninstall => "--uninstall-service",
                ServiceAction.Start => "--start-service",
                ServiceAction.Stop => "--stop-service",
                ServiceAction.Restart => "--restart-service",
                _ => "--service-status"
            };
        }

        public static string Describe(ServiceStatus status, string directory)
        {
            switch (status.State)
            {
                case ServiceInstallState.Unsupported:
                    return LocalizationService.Translate(LocalizationService.Mark(
                        "Installing a service on this system is done by hand. Choose install to have the file written and the commands shown."));

                case ServiceInstallState.NotInstalled:
                    return LocalizationService.Translate(LocalizationService.Mark("Not installed as a service."));

                case ServiceInstallState.Running:
                    return LocalizationService.Format(
                        LocalizationService.Mark("Installed as \"{0}\" and running. Starts with the machine: {1}."),
                        status.Name,
                        YesOrNo(status.StartsAutomatically));

                case ServiceInstallState.Stopped:
                    return LocalizationService.Format(
                        LocalizationService.Mark("Installed as \"{0}\" and stopped. Starts with the machine: {1}."),
                        status.Name,
                        YesOrNo(status.StartsAutomatically));

                default:
                    return LocalizationService.Format(
                        LocalizationService.Mark("Installed as \"{0}\"."),
                        status.Name);
            }
        }

        private static string YesOrNo(bool value)
        {
            return value
                ? LocalizationService.Translate(LocalizationService.Mark("yes"))
                : LocalizationService.Translate(LocalizationService.Mark("no"));
        }
    }
}
