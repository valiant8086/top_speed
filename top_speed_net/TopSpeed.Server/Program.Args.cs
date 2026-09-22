using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using TopSpeed.Server.Config;
using TopSpeed.Server.Control;
using TopSpeed.Server.Logging;
using TopSpeed.Server.Updates;

using TopSpeed.Localization;
namespace TopSpeed.Server
{
    internal static partial class Program
    {
        /// <summary>
        /// Null when the command line names no levels, which is what leaves the setting to
        /// decide rather than this quietly deciding for it.
        /// </summary>
        private static LogLevel? ParseLogLevels(string[] args)
        {
            return LogLevels.Parse(GetFirstArgumentValue(args, "--log-level", "--log"));
        }

        private static bool IsHelpRequested(string[] args)
        {
            foreach (var arg in args)
            {
                if (string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static void ShowHelp()
        {
            ConsoleSink.WriteLine(LocalizationService.Mark("TopSpeed.Server usage:"));
            ConsoleSink.WriteLine(LocalizationService.Mark("  TopSpeed.Server [options]"));
            ConsoleSink.WriteLine(string.Empty);
            ConsoleSink.WriteLine(LocalizationService.Mark("Options:"));
            ConsoleSink.WriteLine(LocalizationService.Mark("  --port <number>         Server port (1-65535)."));
            ConsoleSink.WriteLine(LocalizationService.Mark("  --max-players <number>  Max connected players (1-255)."));
            ConsoleSink.WriteLine(LocalizationService.Mark("  --motd <text>           Message of the day."));
            ConsoleSink.WriteLine(LocalizationService.Mark("  --log-level <levels>    Comma-separated levels: error,warning,info,debug,all."));
            ConsoleSink.WriteLine(LocalizationService.Mark("  --log <levels>          Alias for --log-level."));
            ConsoleSink.WriteLine(LocalizationService.Mark("  --log-file <path>       Output log file path (e.g. log.txt)."));
            ConsoleSink.WriteLine(LocalizationService.Mark("  -h, --help              Show this help."));
            ConsoleSink.WriteLine(string.Empty);
            ConsoleSink.WriteLine(LocalizationService.Mark("Running as a service:"));
            ConsoleSink.WriteLine(LocalizationService.Mark("  --service-status        Say whether this folder is installed as a service."));
            ConsoleSink.WriteLine(LocalizationService.Mark("  --install-service       Install this folder's server as a service."));
            ConsoleSink.WriteLine(LocalizationService.Mark("  --uninstall-service     Remove it again. The folder is left alone."));
            ConsoleSink.WriteLine(LocalizationService.Mark("  --start-service         Start the installed service."));
            ConsoleSink.WriteLine(LocalizationService.Mark("  --stop-service          Stop the installed service."));
            ConsoleSink.WriteLine(LocalizationService.Mark("  --restart-service       Stop it and start it again."));

            // Only where it is true, which is twice over.
            //
            // Not on Windows, where these ask for consent themselves and the server is not
            // expected to be elevated, so the sentence would be advice to do something that
            // platform neither needs nor offers.
            //
            // And not to a reader who is already root, which on a rented server or in a
            // container is simply the account there is. Every option above works for them as it
            // stands, so being told to reach for sudo is advice to solve a problem they do not
            // have, and being warned off running as root is advice against the only thing they
            // can do. A container often has no sudo installed to reach for either.
            //
            // Otherwise this is the one place both halves belong. Everywhere else each is said
            // at the moment it applies; here somebody is reading about the options rather than
            // having tripped over either, and the pair of them is the whole rule.
            if (!OperatingSystem.IsWindows() && !Environment.IsPrivilegedProcess)
            {
                ConsoleSink.WriteLine(string.Empty);
                ConsoleSink.WriteLine(Service.ServiceCommands.RootNeeded(
                    AppContext.BaseDirectory,
                    Service.ServiceAction.Install));
                ConsoleSink.WriteLine(Service.ServiceCommands.DoNotRunAsRoot());
            }
        }

        /// <summary>
        /// Each folder has its own service, so these never need to be told which one they mean.
        /// </summary>
        private static bool TryGetServiceAction(string[] args, out Service.ServiceAction action)
        {
            action = Service.ServiceAction.Status;
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--service-status":
                        action = Service.ServiceAction.Status;
                        return true;
                    case "--install-service":
                        action = Service.ServiceAction.Install;
                        return true;
                    case "--uninstall-service":
                        action = Service.ServiceAction.Uninstall;
                        return true;
                    case "--start-service":
                        action = Service.ServiceAction.Start;
                        return true;
                    case "--stop-service":
                        action = Service.ServiceAction.Stop;
                        return true;
                    case "--restart-service":
                        action = Service.ServiceAction.Restart;
                        return true;
                }
            }

            return false;
        }


        private static string? GetArgumentValue(string[] args, string key)
        {
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (string.Equals(arg, key, StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length)
                        return args[i + 1];
                    return null;
                }

                if (arg.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                    return arg.Substring(key.Length + 1);
            }

            return null;
        }

        private static string? GetFirstArgumentValue(string[] args, params string[] keys)
        {
            if (keys == null || keys.Length == 0)
                return null;

            for (var i = 0; i < keys.Length; i++)
            {
                var value = GetArgumentValue(args, keys[i]);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return null;
        }

        /// <summary>
        /// Attaching is the default when the executable is simply run and a server is already
        /// here, since that is what double clicking it in a file manager should do. Anything
        /// that configures a server is taken as meaning to start one.
        /// </summary>
        private static bool IsExplicitStart(string[] args)
        {
            if (args.Length == 0)
                return false;

            for (var i = 0; i < args.Length; i++)
            {
                if (IsAttachArgument(args[i]))
                    return false;
            }

            return true;
        }

        private static bool IsAttachRequested(string[] args)
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (IsAttachArgument(args[i]))
                    return true;
            }

            return false;
        }

        private static bool IsAttachArgument(string arg)
        {
            return string.Equals(arg, "--attach", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The installer writes this into the service registration, so its presence is exact.
        /// Guessing from whether the process looks interactive is unreliable on .NET and would
        /// misjudge a server started from a scheduled task or a wrapper.
        /// </summary>
        private static bool IsServiceMode(string[] args)
        {
            // The flag means "something else is managing me", which is as true of a systemd
            // unit or a launchd job as of a Windows service, so it is read on every platform.
            // It decides whether a console session is offered and whether the updater may start
            // the program again once it has replaced it. Only the branch that runs the Windows
            // service host is Windows only.
            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--service", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The first thing an attaching client is shown, so it answers the question somebody
        /// attaching to a server left running unattended actually has.
        ///
        /// Whether this is the service is part of that answer rather than a detail. It decides
        /// what closing the window does, whether the server comes back with the machine, and
        /// which of two servers on one machine has been reached, and the window on its own has
        /// no way to tell.
        /// </summary>
        private static string DescribeServerStatus(ServerSettings settings)
        {
            var release = ServerUpdateConfig.CurrentVersion.ToMachineString();
            var updates = StartupUpdateModes.Normalize(settings.StartupUpdateMode);

            return Service.ServiceRuntime.IsRunningAsService
                ? LocalizationService.Format(
                    LocalizationService.Mark("Attached to service {0}, port {1}, update checking {2}."),
                    release, settings.Port, updates)
                : LocalizationService.Format(
                    LocalizationService.Mark("Attached to TopSpeed Server {0}, port {1}, update checking {2}."),
                    release, settings.Port, updates);
        }

        private static string BuildLogFilePath(string configuredPath)
        {
            var trimmed = configuredPath.Trim().Trim('"');
            if (Path.IsPathRooted(trimmed))
                return trimmed;

            return Path.GetFullPath(trimmed, AppContext.BaseDirectory);
        }

        private static void ApplyArgumentOverrides(ServerSettings settings, string[] args, Logger logger)
        {
            if (TryGetIntArg(args, "--port", out var port))
            {
                if (port >= 1 && port <= 65535)
                    settings.Port = port;
                else
                    logger.Warning(LocalizationService.Mark("Invalid --port value. Using configured port."));
            }

            if (TryGetIntArg(args, "--max-players", out var maxPlayers))
            {
                if (maxPlayers >= 1 && maxPlayers <= byte.MaxValue)
                    settings.MaxPlayers = maxPlayers;
                else
                    logger.Warning(LocalizationService.Mark("Invalid --max-players value. Using configured max players."));
            }

            var motd = GetArgumentValue(args, "--motd");
            if (!string.IsNullOrWhiteSpace(motd))
                settings.Motd = motd.Trim();
        }

        private static bool TryGetIntArg(string[] args, string key, out int value)
        {
            value = 0;
            var raw = GetArgumentValue(args, key);
            if (string.IsNullOrWhiteSpace(raw))
                return false;
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }
}



