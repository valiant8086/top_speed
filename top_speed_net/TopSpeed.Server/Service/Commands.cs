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
                ServiceAction.Restart => manager.Restart(directory),
                _ => ServiceActionResult.Failed(string.Empty)
            };

            if (result.NeedsElevation && OperatingSystem.IsWindows())
                return Elevate(action, directory);

            ConsoleSink.WriteLine(result.Message);
            return result.Succeeded ? 0 : 1;
        }

        /// <summary>
        /// Runs this same program again for the one action, with the rights this one cannot
        /// have. A process cannot be granted them while it is running: on Windows the token is
        /// settled when a process is created, so a second process is the only way, and the
        /// consent prompt belongs to the system, meaning nothing here sees or asks for a
        /// password.
        ///
        /// That copy is hidden and reports by finishing. A window of its own would be a second
        /// place to read and a second thing to dismiss, for a job nobody needs to watch.
        /// </summary>
        private static int Elevate(ServiceAction action, string directory)
        {
            ConsoleSink.WriteLine(LocalizationService.Mark("This needs administrator rights."));

            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = ServiceIdentity.ExecutablePathFor(directory),
                    WorkingDirectory = directory,
                    // Both are required together: the verb is only honoured by the shell, and
                    // the shell is only used when the process is not started directly.
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                info.ArgumentList.Add(FlagFor(action));

                using var process = Process.Start(info);
                if (process == null)
                    return 1;

                process.WaitForExit();

                // What happened is read back off the service rather than carried between the
                // two processes. It needs no channel and cannot disagree with the truth, and it
                // is checked rather than taken on the exit code alone.
                var status = ServiceManagers.ForCurrentPlatform().Query(directory);
                var done = process.ExitCode == 0 && Achieved(action, status);

                ConsoleSink.WriteLine(done
                    ? Confirm(action, status, directory)
                    : LocalizationService.Translate(LocalizationService.Mark("That did not finish.")));

                return done ? 0 : 1;
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

        private static bool Achieved(ServiceAction action, ServiceStatus status)
        {
            return action switch
            {
                ServiceAction.Install => status.IsInstalled,
                ServiceAction.Uninstall => status.State == ServiceInstallState.NotInstalled,
                _ => true
            };
        }

        /// <summary>
        /// What to say once it is done.
        ///
        /// A removal has to say so in its own words. What it leaves behind is exactly the state
        /// of a folder that never had a service, so describing that state would answer a request
        /// to remove something with what sounds like a complaint that there was nothing there.
        /// Everything else is confirmed by describing where it now stands, which is both true
        /// and the thing somebody wanted to know.
        /// </summary>
        private static string Confirm(ServiceAction action, ServiceStatus status, string directory)
        {
            if (action == ServiceAction.Uninstall)
            {
                return LocalizationService.Translate(LocalizationService.Mark(
                    "The service has been removed. The server folder was left alone."));
            }

            return Describe(status, directory);
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
