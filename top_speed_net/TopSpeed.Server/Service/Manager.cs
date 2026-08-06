using System;

namespace TopSpeed.Server.Service
{
    internal enum ServiceInstallState
    {
        /// <summary>No service is registered for this folder.</summary>
        NotInstalled,

        Stopped,

        Running,

        /// <summary>Registered, but the manager will not say more than that right now.</summary>
        Unknown,

        /// <summary>This platform has no service manager we drive directly.</summary>
        Unsupported
    }

    internal sealed class ServiceStatus
    {
        public ServiceStatus(ServiceInstallState state, string name, bool startsAutomatically)
        {
            State = state;
            Name = name ?? string.Empty;
            StartsAutomatically = startsAutomatically;
        }

        public ServiceInstallState State { get; }

        public string Name { get; }

        public bool StartsAutomatically { get; }

        public bool IsInstalled => State == ServiceInstallState.Stopped
            || State == ServiceInstallState.Running
            || State == ServiceInstallState.Unknown;
    }

    /// <summary>
    /// The outcome of asking for a change. Carries the text to show either way, because what
    /// went wrong is usually more useful than a boolean, and on the platforms we do not drive
    /// directly the "failure" is a set of instructions worth reading.
    /// </summary>
    internal sealed class ServiceActionResult
    {
        private ServiceActionResult(bool succeeded, string message, bool needsElevation)
        {
            Succeeded = succeeded;
            Message = message ?? string.Empty;
            NeedsElevation = needsElevation;
        }

        public bool Succeeded { get; }

        public string Message { get; }

        /// <summary>The action is possible but this process is not allowed to do it.</summary>
        public bool NeedsElevation { get; }

        public static ServiceActionResult Ok(string message) => new ServiceActionResult(true, message, false);

        public static ServiceActionResult Failed(string message) => new ServiceActionResult(false, message, false);

        public static ServiceActionResult RequiresElevation(string message) => new ServiceActionResult(false, message, true);
    }

    /// <summary>
    /// Installing and controlling this folder's server as a service of the host system.
    ///
    /// One implementation drives the Windows service manager directly. The others write the
    /// unit or job file the system expects and say what to run, because there is no equivalent
    /// of a consent prompt on those systems and a program that tries to elevate itself is worse
    /// than one that tells you exactly what it wants done.
    /// </summary>
    internal interface IServiceManager
    {
        ServiceStatus Query(string directory);

        ServiceActionResult Install(string directory, bool startAutomatically);

        ServiceActionResult Uninstall(string directory);

        ServiceActionResult Start(string directory);

        ServiceActionResult Stop(string directory);
    }

    internal static class ServiceManagers
    {
        public static IServiceManager ForCurrentPlatform()
        {
            if (OperatingSystem.IsWindows())
                return new WindowsServiceManager();

            return new UnixServiceManager();
        }
    }
}
