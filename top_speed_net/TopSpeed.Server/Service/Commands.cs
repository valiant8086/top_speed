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
                    "The service has been removed."));
            }

            return Describe(status, directory);
        }

        /// <summary>
        /// The one thing there is to say on a system where the service can only be reached with
        /// root: what to run, spelled out ready to be copied.
        ///
        /// It serves three moments — a service command typed without sudo, the service menu, and
        /// the refusal when the server itself is started as root — because all three are the same
        /// person about to have the same trouble, and one sentence they recognise the second time
        /// is worth more than three that each say it differently.
        ///
        /// The command is built here rather than written inside the sentence. sudo is spelled the
        /// same in every language and a path has no translation, so carrying either into a
        /// translated string is how one comes back rewritten in a form only a stranger's machine
        /// would refuse. The path is absolute and quoted so it can be pasted from anywhere.
        /// </summary>
        public static string RootNeeded(string directory, ServiceAction action)
        {
            var command = "  sudo \"" + ServiceIdentity.ExecutablePathFor(directory) + "\" " + FlagFor(action);

            return LocalizationService.Format(
                LocalizationService.Mark("Installing, removing or controlling the service needs root. Run it with sudo:\n{0}\nThe server itself does not need sudo and should not be given it: run as root it leaves files in this folder that your own account cannot replace when it updates."),
                command);
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
                // Asking would mean running systemctl or launchctl, whose answer is only ever
                // wanted by a person, who can run it themselves and read more than this could
                // repeat back. Saying so is more use than guessing.
                case ServiceInstallState.Unsupported:
                    return LocalizationService.Translate(LocalizationService.Mark(
                        "Whether this folder is installed as a service is answered by the system rather than by the server: run systemctl status on Linux, or launchctl print on macOS, with the name the install reported."));

                case ServiceInstallState.NotInstalled:
                    return LocalizationService.Translate(LocalizationService.Mark("Not installed as a service."));

                case ServiceInstallState.Running:
                    return LocalizationService.Format(
                        LocalizationService.Mark("Installed as \"{0}\" and running. Starts with the machine: {1}."),
                        status.Name,
                        YesOrNo(status.StartsAutomatically));

                // Anything else is one of the paused states, which this service never enters:
                // starting and stopping are already reported as running and stopped, and pausing
                // was never offered. Were one to arrive, it is a service not serving anybody, so
                // stopped is the true reading as well as the only one worth a sentence.
                default:
                    return LocalizationService.Format(
                        LocalizationService.Mark("Installed as \"{0}\" and stopped. Starts with the machine: {1}."),
                        status.Name,
                        YesOrNo(status.StartsAutomatically));
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
