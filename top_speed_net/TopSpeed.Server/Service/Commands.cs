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

        /// <summary>
        /// Wait for the server holding this folder to go, then start the service.
        ///
        /// Only one server may run from a folder, and every way of reaching the service menu
        /// has one running: launching the program starts one, and otherwise it attaches to the
        /// one already there. Starting the service therefore always means stopping something
        /// first, and something has to outlive it to do the starting.
        /// </summary>
        StartWhenFree
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

            if (action == ServiceAction.StartWhenFree)
                WaitForFolderToBeFree(directory);

            var result = action switch
            {
                ServiceAction.Install => manager.Install(directory, startAutomatically),
                ServiceAction.Uninstall => manager.Uninstall(directory),
                ServiceAction.Start or ServiceAction.StartWhenFree => manager.Start(directory),
                ServiceAction.Stop => manager.Stop(directory),
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
        /// Waits for whatever server owns this folder to let go of it.
        ///
        /// The endpoint is the thing being waited on rather than a process handle or a delay,
        /// because it is the same thing the starting service will find in its way. It exists
        /// for exactly as long as a server holds the folder and disappears when that process
        /// ends however it ends, including being killed, so there is no window in which this
        /// believes the folder is free while it is not.
        ///
        /// Bounded, because a server that refuses to stop must not leave a window waiting for
        /// it forever. Giving up here simply means the start below reports the folder as busy,
        /// which is true and recoverable.
        /// </summary>
        private static void WaitForFolderToBeFree(string directory)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(45);
            while (DateTime.UtcNow < deadline)
            {
                if (!Control.ControlTransport.EndpointExists(directory))
                {
                    // The endpoint is gone, but the process that held it may still be finishing.
                    // A moment here costs nothing and saves a needless failure.
                    Thread.Sleep(500);
                    return;
                }

                Thread.Sleep(250);
            }
        }

        /// <summary>
        /// Starts another copy of this program to carry out one action and does not wait for it.
        ///
        /// Used when the thing being asked for is the stopping of this very process, which
        /// cannot both go away and observe what happens next. Whatever is launched here has its
        /// own window to report into and waits to be read before closing.
        ///
        /// Returns false when the rights were refused, so the caller can leave the running
        /// server alone rather than stopping it for a start that was never going to happen.
        /// </summary>
        public static bool LaunchDetached(ServiceAction action, string directory)
        {
            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = ServiceIdentity.ExecutablePathFor(directory),
                    WorkingDirectory = directory,
                    UseShellExecute = true
                };
                info.ArgumentList.Add(FlagFor(action));

                // Only asked for when it is actually missing, so an elevated console does not
                // raise a prompt it has no need of.
                if (OperatingSystem.IsWindows() && !WindowsServiceManager.IsElevated())
                {
                    info.Verb = "runas";
                    ConsoleSink.WriteLine(LocalizationService.Mark(
                        "This needs administrator rights. Approve the prompt to continue."));
                }

                using var process = Process.Start(info);
                return process != null;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                ConsoleSink.WriteLine(LocalizationService.Mark("Cancelled. Nothing was changed."));
                return false;
            }
            catch (Win32Exception ex)
            {
                ConsoleSink.WriteLine(ex.Message);
                return false;
            }
        }

        public static string FlagFor(ServiceAction action)
        {
            return action switch
            {
                ServiceAction.Install => "--install-service",
                ServiceAction.Uninstall => "--uninstall-service",
                ServiceAction.Start => "--start-service",
                ServiceAction.StartWhenFree => "--start-service-when-free",
                ServiceAction.Stop => "--stop-service",
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
