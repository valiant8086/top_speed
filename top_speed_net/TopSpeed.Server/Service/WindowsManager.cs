using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using TopSpeed.Localization;

namespace TopSpeed.Server.Service
{
    /// <summary>
    /// Drives the Windows service manager through its own API rather than by running sc.exe.
    ///
    /// Not for secrecy, since nothing here is secret, but because sc.exe is driven entirely by
    /// the shape of its command line: the space after "binPath=" is load bearing, quoting a
    /// path that contains spaces inside an argument that is itself quoted is a well known trap,
    /// and a mistake shows up as a service that registers happily and then fails to start. The
    /// API takes the path as a value, so none of that can happen, and it reports why it failed.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class WindowsServiceManager : IServiceManager
    {
        private const string RunAsAccount = @"NT AUTHORITY\LocalService";

        public ServiceStatus Query(string directory)
        {
            var name = ServiceIdentity.NameFor(directory);
            try
            {
                using var controller = new ServiceController(name);
                var state = controller.Status switch
                {
                    ServiceControllerStatus.Running => ServiceInstallState.Running,
                    ServiceControllerStatus.StartPending => ServiceInstallState.Running,
                    ServiceControllerStatus.Stopped => ServiceInstallState.Stopped,
                    ServiceControllerStatus.StopPending => ServiceInstallState.Stopped,
                    _ => ServiceInstallState.Unknown
                };

                return new ServiceStatus(state, name, controller.StartType == ServiceStartMode.Automatic);
            }
            catch (InvalidOperationException)
            {
                // The only way the manager reports "no such service" is by throwing.
                return new ServiceStatus(ServiceInstallState.NotInstalled, name, false);
            }
            catch (Win32Exception)
            {
                return new ServiceStatus(ServiceInstallState.NotInstalled, name, false);
            }
        }

        public ServiceActionResult Install(string directory, bool startAutomatically)
        {
            if (ServiceIdentity.IsProtectedLocation(directory, out var location))
            {
                return ServiceActionResult.Failed(LocalizationService.Format(
                    LocalizationService.Mark("This server cannot be installed as a service from {0}. The server updates itself in place, so it must run from a folder it can write to, and giving a service write access inside a protected location would let anything that can write there run as that service later. Move the server folder somewhere else, such as a folder you created yourself, and install it from there."),
                    location ?? string.Empty));
            }

            var executable = ServiceIdentity.ExecutablePathFor(directory);
            if (!File.Exists(executable))
            {
                return ServiceActionResult.Failed(LocalizationService.Format(
                    LocalizationService.Mark("The server program was not found at {0}."),
                    executable));
            }

            if (!IsElevated())
                return ServiceActionResult.RequiresElevation(NeedsAdministrator());

            var name = ServiceIdentity.NameFor(directory);
            var manager = IntPtr.Zero;
            var service = IntPtr.Zero;
            try
            {
                manager = OpenSCManagerW(null, null, SC_MANAGER_CONNECT | SC_MANAGER_CREATE_SERVICE);
                if (manager == IntPtr.Zero)
                    return ServiceActionResult.Failed(LastErrorMessage());

                service = CreateServiceW(
                    manager,
                    name,
                    ServiceIdentity.DisplayNameFor(directory),
                    SERVICE_ALL_ACCESS,
                    SERVICE_WIN32_OWN_PROCESS,
                    startAutomatically ? SERVICE_AUTO_START : SERVICE_DEMAND_START,
                    SERVICE_ERROR_NORMAL,
                    ServiceIdentity.CommandLineFor(directory),
                    null,
                    IntPtr.Zero,
                    null,
                    // No password, and none is possible: this account has none to steal, and
                    // nothing has to be stored anywhere for the service to start again.
                    RunAsAccount,
                    null);

                if (service == IntPtr.Zero)
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ERROR_SERVICE_EXISTS)
                    {
                        return ServiceActionResult.Failed(LocalizationService.Translate(LocalizationService.Mark(
                            "A service is already installed for this folder.")));
                    }

                    if (error == ERROR_SERVICE_MARKED_FOR_DELETE)
                    {
                        return ServiceActionResult.Failed(LocalizationService.Translate(LocalizationService.Mark(
                            "A service for this folder is still being removed. Close the Services window if it is open, then try again.")));
                    }

                    return ServiceActionResult.Failed(ErrorMessage(error));
                }

                SetDescription(service, ServiceIdentity.DescriptionFor(directory));
                SetRestartOnFailure(service);
                if (startAutomatically)
                    SetDelayedStart(service);

                var granted = GrantServiceAccountAccess(directory);
                if (granted != null)
                    return ServiceActionResult.Failed(granted);

                var installed = LocalizationService.Format(
                    LocalizationService.Mark("Installed as service \"{0}\", running as {1}."),
                    name,
                    RunAsAccount);

                if (!AllowInteractiveUsersToStartAndStop(service))
                {
                    installed += "\n" + LocalizationService.Translate(LocalizationService.Mark(
                        "Starting and stopping it will need administrator rights, because this account could not grant them here."));
                }

                // Nothing is said here about a server already running from this folder.
                // Installing registers the service to start with the machine and starts nothing
                // now, so a server running at this moment is neither in the way nor relevant,
                // and starting the service later stops it as part of doing so.
                return ServiceActionResult.Ok(installed);
            }
            finally
            {
                if (service != IntPtr.Zero)
                    CloseServiceHandle(service);
                if (manager != IntPtr.Zero)
                    CloseServiceHandle(manager);
            }
        }

        public ServiceActionResult Uninstall(string directory)
        {
            var name = ServiceIdentity.NameFor(directory);

            // Asked before the rights are, so that being told nothing is installed does not
            // cost a consent prompt. Querying needs no privilege; removing does.
            var status = Query(directory);
            if (status.State == ServiceInstallState.NotInstalled)
            {
                return ServiceActionResult.Failed(LocalizationService.Translate(LocalizationService.Mark(
                    "No service is installed for this folder.")));
            }

            if (!IsElevated())
                return ServiceActionResult.RequiresElevation(NeedsAdministrator());

            // Removing a running service leaves it registered until the last handle closes and
            // the machine reboots, so it is stopped first and the caller is spared a service
            // that is neither present nor gone.
            if (status.State == ServiceInstallState.Running)
            {
                var stopped = Stop(directory);
                if (!stopped.Succeeded)
                    return stopped;
            }

            var manager = IntPtr.Zero;
            var service = IntPtr.Zero;
            try
            {
                manager = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
                if (manager == IntPtr.Zero)
                    return ServiceActionResult.Failed(LastErrorMessage());

                service = OpenServiceW(manager, name, DELETE);
                if (service == IntPtr.Zero)
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ERROR_SERVICE_DOES_NOT_EXIST)
                    {
                        return ServiceActionResult.Failed(LocalizationService.Translate(LocalizationService.Mark(
                            "No service is installed for this folder.")));
                    }

                    return ServiceActionResult.Failed(ErrorMessage(error));
                }

                if (!DeleteService(service))
                    return ServiceActionResult.Failed(LastErrorMessage());

                return ServiceActionResult.Ok(LocalizationService.Format(
                    LocalizationService.Mark("Removed service \"{0}\". The server folder was left alone."),
                    name));
            }
            finally
            {
                if (service != IntPtr.Zero)
                    CloseServiceHandle(service);
                if (manager != IntPtr.Zero)
                    CloseServiceHandle(manager);
            }
        }

        public ServiceActionResult Start(string directory)
        {
            var name = ServiceIdentity.NameFor(directory);
            try
            {
                using var controller = new ServiceController(name);
                if (controller.Status == ServiceControllerStatus.Running)
                {
                    return ServiceActionResult.Ok(LocalizationService.Translate(LocalizationService.Mark(
                        "The service is already running.")));
                }

                // Checked here rather than left to fail. Only one server may run from a folder,
                // and the service enforces that by finding the endpoint taken and exiting at
                // once. The manager can only report that as a start that did not complete,
                // which says nothing about the reason, and the reason is nearly always that the
                // person installing it is sitting in front of the very server in the way.
                if (Control.ControlTransport.EndpointExists(directory))
                {
                    return ServiceActionResult.Failed(LocalizationService.Translate(LocalizationService.Mark(
                        "A server is already running from this folder, so the service cannot start as well. Stop that server first, using its shutdown command if it is the one you are reading this in, and then start the service.")));
                }

                controller.Start();
                controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                return ServiceActionResult.Ok(LocalizationService.Translate(LocalizationService.Mark(
                    "The service is running.")));
            }
            catch (InvalidOperationException ex) when (IsAccessDenied(ex))
            {
                return ServiceActionResult.RequiresElevation(NeedsAdministrator());
            }
            catch (InvalidOperationException)
            {
                return ServiceActionResult.Failed(LocalizationService.Translate(LocalizationService.Mark(
                    "No service is installed for this folder.")));
            }
            catch (System.ServiceProcess.TimeoutException)
            {
                return ServiceActionResult.Failed(LocalizationService.Translate(LocalizationService.Mark(
                    "The service did not finish starting. Check the server log in its folder.")));
            }
        }

        public ServiceActionResult Stop(string directory)
        {
            var name = ServiceIdentity.NameFor(directory);
            try
            {
                using var controller = new ServiceController(name);
                if (controller.Status == ServiceControllerStatus.Stopped)
                {
                    return ServiceActionResult.Ok(LocalizationService.Translate(LocalizationService.Mark(
                        "The service is already stopped.")));
                }

                controller.Stop();
                controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(40));
                return ServiceActionResult.Ok(LocalizationService.Translate(LocalizationService.Mark(
                    "The service is stopped.")));
            }
            catch (InvalidOperationException ex) when (IsAccessDenied(ex))
            {
                return ServiceActionResult.RequiresElevation(NeedsAdministrator());
            }
            catch (InvalidOperationException)
            {
                return ServiceActionResult.Failed(LocalizationService.Translate(LocalizationService.Mark(
                    "No service is installed for this folder.")));
            }
            catch (System.ServiceProcess.TimeoutException)
            {
                return ServiceActionResult.Failed(LocalizationService.Translate(LocalizationService.Mark(
                    "The service did not stop in time.")));
            }
        }

        /// <summary>
        /// Lets the account the service runs as write to the server folder.
        ///
        /// The server keeps its settings, its log and its own updates here, and LocalService is
        /// not granted anything in an ordinary folder by default. Without this the service
        /// starts and then fails at the first thing it tries to save.
        /// </summary>
        private static string? GrantServiceAccountAccess(string directory)
        {
            try
            {
                var info = new DirectoryInfo(ControlEndpointDirectory(directory));
                var security = info.GetAccessControl();
                security.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null),
                    FileSystemRights.Modify,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
                info.SetAccessControl(security);
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return LocalizationService.Translate(LocalizationService.Mark(
                    "The service was installed, but this account could not give it write access to the server folder. The service will not be able to save settings or updates until that is granted."));
            }
            catch (IOException ex)
            {
                return ex.Message;
            }
        }

        private static string ControlEndpointDirectory(string directory)
        {
            return ServiceIdentity.DisplayPath(directory);
        }

        /// <summary>
        /// Lets whoever is logged on start and stop this service without becoming an
        /// administrator first.
        ///
        /// Windows grants ordinary accounts only the right to look at a service; starting and
        /// stopping one belongs to administrators. That is a sensible default for services
        /// somebody else installed, and a poor fit for a portable game server whose owner is
        /// the person sitting at the machine. Done here because this is the moment the rights
        /// to change it exist, and the alternative is a consent prompt every time.
        ///
        /// Stopping is given away with a clear conscience: any interactive account can already
        /// attach to this server and type shutdown, so this only makes the service manager
        /// agree with what the control channel has allowed all along. Starting is no more than
        /// running what was already registered, under the account it was registered with.
        ///
        /// What is deliberately not granted is the right to change the registration. That one
        /// is not divided by field: it would also rewrite which program runs and which account
        /// runs it, needing no password to name a more privileged one, and it is the reason the
        /// service is not allowed to rewrite its own label either.
        /// </summary>
        private static bool AllowInteractiveUsersToStartAndStop(IntPtr service)
        {
            try
            {
                if (!QueryServiceObjectSecurity(service, SecurityInfos.DiscretionaryAcl, Array.Empty<byte>(), 0, out var needed)
                    && Marshal.GetLastWin32Error() != ERROR_INSUFFICIENT_BUFFER)
                {
                    return false;
                }

                var current = new byte[needed];
                if (!QueryServiceObjectSecurity(service, SecurityInfos.DiscretionaryAcl, current, needed, out _))
                    return false;

                var descriptor = new RawSecurityDescriptor(current, 0);
                var acl = descriptor.DiscretionaryAcl;
                if (acl == null)
                    return false;

                // Appended rather than inserted at the front, so that any rule already refusing
                // somebody is still considered first and this cannot quietly overrule it.
                acl.InsertAce(acl.Count, new CommonAce(
                    AceFlags.None,
                    AceQualifier.AccessAllowed,
                    StartStopRights,
                    new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
                    isCallback: false,
                    opaque: null));
                descriptor.DiscretionaryAcl = acl;

                var updated = new byte[descriptor.BinaryLength];
                descriptor.GetBinaryForm(updated, 0);
                return SetServiceObjectSecurity(service, SecurityInfos.DiscretionaryAcl, updated);
            }
            catch (Exception)
            {
                // Worth reporting but never worth failing an install over: the service works,
                // it just asks for rights when it is started or stopped.
                return false;
            }
        }

        private static void SetDescription(IntPtr service, string description)
        {
            var text = Marshal.StringToHGlobalUni(description);
            try
            {
                var info = new SERVICE_DESCRIPTION { lpDescription = text };
                ChangeServiceConfig2W(service, SERVICE_CONFIG_DESCRIPTION, ref info);
            }
            finally
            {
                Marshal.FreeHGlobal(text);
            }
        }

        /// <summary>
        /// Has the manager start the server again if it exits unexpectedly. This is also what
        /// lets the server update itself later: it can stop, knowing something will bring it
        /// back, without needing the privilege to start a service.
        /// </summary>
        private static void SetRestartOnFailure(IntPtr service)
        {
            const int actionCount = 3;
            var actions = Marshal.AllocHGlobal(Marshal.SizeOf<SC_ACTION>() * actionCount);
            try
            {
                for (var i = 0; i < actionCount; i++)
                {
                    Marshal.StructureToPtr(
                        new SC_ACTION { Type = SC_ACTION_RESTART, Delay = RestartDelayMilliseconds },
                        actions + (Marshal.SizeOf<SC_ACTION>() * i),
                        false);
                }

                var failure = new SERVICE_FAILURE_ACTIONS
                {
                    dwResetPeriod = 86400,
                    lpRebootMsg = IntPtr.Zero,
                    lpCommand = IntPtr.Zero,
                    cActions = actionCount,
                    lpsaActions = actions
                };

                ChangeServiceConfig2W(service, SERVICE_CONFIG_FAILURE_ACTIONS, ref failure);
            }
            finally
            {
                Marshal.FreeHGlobal(actions);
            }

            // Without this, the actions above only ever apply to a service that crashed, and a
            // server that stopped tidily to apply an update would stay stopped no matter what
            // exit code it reported. This is the "enable actions for stops with errors" box in
            // the services window, and it is what makes an update able to bring itself back.
            var flag = new SERVICE_FAILURE_ACTIONS_FLAG { fFailureActionsOnNonCrashFailures = true };
            ChangeServiceConfig2W(service, SERVICE_CONFIG_FAILURE_ACTIONS_FLAG, ref flag);
        }

        /// <summary>
        /// Starts shortly after boot rather than during it. A race server has nothing useful to
        /// do until the network is up, and starting late costs nobody anything.
        /// </summary>
        private static void SetDelayedStart(IntPtr service)
        {
            var info = new SERVICE_DELAYED_AUTO_START_INFO { fDelayedAutostart = true };
            ChangeServiceConfig2W(service, SERVICE_CONFIG_DELAYED_AUTO_START_INFO, ref info);
        }

        public static bool IsElevated()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool IsAccessDenied(Exception ex)
        {
            return ex.InnerException is Win32Exception win32 && win32.NativeErrorCode == ERROR_ACCESS_DENIED;
        }

        private static string NeedsAdministrator()
        {
            return LocalizationService.Translate(LocalizationService.Mark(
                "Installing and controlling services needs administrator rights."));
        }

        private static string LastErrorMessage()
        {
            return ErrorMessage(Marshal.GetLastWin32Error());
        }

        private static string ErrorMessage(int error)
        {
            if (error == ERROR_ACCESS_DENIED)
                return NeedsAdministrator();

            return new Win32Exception(error).Message;
        }

        private const int SC_MANAGER_CONNECT = 0x0001;
        private const int SC_MANAGER_CREATE_SERVICE = 0x0002;
        private const int SERVICE_ALL_ACCESS = 0xF01FF;
        private const int DELETE = 0x00010000;
        private const int SERVICE_WIN32_OWN_PROCESS = 0x00000010;
        private const int SERVICE_AUTO_START = 0x00000002;
        private const int SERVICE_DEMAND_START = 0x00000003;
        private const int SERVICE_ERROR_NORMAL = 0x00000001;
        private const int SERVICE_CONFIG_DESCRIPTION = 1;
        private const int SERVICE_CONFIG_FAILURE_ACTIONS = 2;
        private const int SERVICE_CONFIG_DELAYED_AUTO_START_INFO = 3;
        private const int SERVICE_CONFIG_FAILURE_ACTIONS_FLAG = 4;

        /// <summary>
        /// Long enough for an update to finish rewriting the folder before the manager starts
        /// the server again.
        ///
        /// The updater waits for the old server to go, unpacks over the top of it, and leaves
        /// the starting to the manager. Starting too early is the one outcome worth engineering
        /// against: the new server locks its own files, the updater's remaining writes fail, and
        /// what is left is a folder holding two versions rather than a clean failure. A realistic
        /// unpack is seconds, so two minutes is roughly ten times over, and the cost of being
        /// generous is only a slower comeback from a crash.
        /// </summary>
        private const int RestartDelayMilliseconds = 120000;
        private const int SC_ACTION_RESTART = 1;
        private const int ERROR_ACCESS_DENIED = 5;
        private const int ERROR_INSUFFICIENT_BUFFER = 122;

        /// <summary>
        /// Start, stop, and enough to see which of the two it currently is. Notably absent is
        /// anything that changes the registration.
        /// </summary>
        private const int StartStopRights = SERVICE_QUERY_STATUS | SERVICE_START | SERVICE_STOP | READ_CONTROL;

        private const int SERVICE_QUERY_STATUS = 0x0004;
        private const int SERVICE_START = 0x0010;
        private const int SERVICE_STOP = 0x0020;
        private const int READ_CONTROL = 0x00020000;
        private const int ERROR_SERVICE_DOES_NOT_EXIST = 1060;
        private const int ERROR_SERVICE_MARKED_FOR_DELETE = 1072;
        private const int ERROR_SERVICE_EXISTS = 1073;

        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_DESCRIPTION
        {
            public IntPtr lpDescription;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_DELAYED_AUTO_START_INFO
        {
            [MarshalAs(UnmanagedType.Bool)]
            public bool fDelayedAutostart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_FAILURE_ACTIONS_FLAG
        {
            [MarshalAs(UnmanagedType.Bool)]
            public bool fFailureActionsOnNonCrashFailures;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SC_ACTION
        {
            public int Type;
            public int Delay;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_FAILURE_ACTIONS
        {
            public int dwResetPeriod;
            public IntPtr lpRebootMsg;
            public IntPtr lpCommand;
            public int cActions;
            public IntPtr lpsaActions;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenSCManagerW(string? machineName, string? databaseName, int access);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenServiceW(IntPtr manager, string serviceName, int access);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateServiceW(
            IntPtr manager,
            string serviceName,
            string displayName,
            int desiredAccess,
            int serviceType,
            int startType,
            int errorControl,
            string binaryPath,
            string? loadOrderGroup,
            IntPtr tagId,
            string? dependencies,
            string? accountName,
            string? password);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteService(IntPtr service);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryServiceObjectSecurity(
            IntPtr service,
            SecurityInfos securityInformation,
            byte[] descriptor,
            uint bufferSize,
            out uint bytesNeeded);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetServiceObjectSecurity(
            IntPtr service,
            SecurityInfos securityInformation,
            byte[] descriptor);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseServiceHandle(IntPtr handle);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ChangeServiceConfig2W(IntPtr service, int infoLevel, ref SERVICE_DESCRIPTION info);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ChangeServiceConfig2W(IntPtr service, int infoLevel, ref SERVICE_FAILURE_ACTIONS info);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ChangeServiceConfig2W(IntPtr service, int infoLevel, ref SERVICE_DELAYED_AUTO_START_INFO info);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ChangeServiceConfig2W(IntPtr service, int infoLevel, ref SERVICE_FAILURE_ACTIONS_FLAG info);
    }
}
