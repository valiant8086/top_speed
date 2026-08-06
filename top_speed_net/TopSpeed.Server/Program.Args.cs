using System;
using System.Globalization;
using System.IO;
using TopSpeed.Server.Config;
using TopSpeed.Server.Control;
using TopSpeed.Server.Logging;
using TopSpeed.Server.Updates;

using TopSpeed.Localization;
namespace TopSpeed.Server
{
    internal static partial class Program
    {
        private static LogLevel ParseLogLevels(string[] args)
        {
            var value = GetFirstArgumentValue(args, "--log-level", "--log");
            if (string.IsNullOrWhiteSpace(value))
                return LogLevel.Error | LogLevel.Warning | LogLevel.Info;

            var levels = LogLevel.None;
            var parts = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var token = part.Trim().ToLowerInvariant();
                switch (token)
                {
                    case "error":
                        levels |= LogLevel.Error;
                        break;
                    case "warning":
                        levels |= LogLevel.Warning;
                        break;
                    case "info":
                        levels |= LogLevel.Info;
                        break;
                    case "debug":
                        levels |= LogLevel.Debug;
                        break;
                    case "all":
                        levels = LogLevel.All;
                        break;
                }
            }

            return levels == LogLevel.None
                ? LogLevel.Error | LogLevel.Warning | LogLevel.Info
                : levels;
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
            ConsoleSink.WriteLine(LocalizationService.Mark("Running as a service (this folder only):"));
            ConsoleSink.WriteLine(LocalizationService.Mark("  --service-status        Say whether this folder is installed as a service."));
            ConsoleSink.WriteLine(LocalizationService.Mark("  --install-service       Install this folder's server as a service."));
            ConsoleSink.WriteLine(LocalizationService.Mark("  --uninstall-service     Remove it again. The folder is left alone."));
            ConsoleSink.WriteLine(LocalizationService.Mark("  --start-service         Start the installed service."));
            ConsoleSink.WriteLine(LocalizationService.Mark("  --stop-service          Stop the installed service."));
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
                    case "--start-service-when-free":
                        action = Service.ServiceAction.StartWhenFree;
                        return true;
                    case "--stop-service":
                        action = Service.ServiceAction.Stop;
                        return true;
                }
            }

            return false;
        }


        private static string FormatLogLevels(LogLevel levels)
        {
            if (levels == LogLevel.None)
                return LocalizationService.Translate(LocalizationService.Mark("none"));
            if (levels == LogLevel.All)
                return LocalizationService.Translate(LocalizationService.Mark("all"));

            var parts = new System.Collections.Generic.List<string>();
            if ((levels & LogLevel.Error) != 0)
                parts.Add(LocalizationService.Translate(LocalizationService.Mark("error")));
            if ((levels & LogLevel.Warning) != 0)
                parts.Add(LocalizationService.Translate(LocalizationService.Mark("warning")));
            if ((levels & LogLevel.Info) != 0)
                parts.Add(LocalizationService.Translate(LocalizationService.Mark("info")));
            if ((levels & LogLevel.Debug) != 0)
                parts.Add(LocalizationService.Translate(LocalizationService.Mark("debug")));
            return parts.Count == 0
                ? LocalizationService.Translate(LocalizationService.Mark("none"))
                : string.Join(",", parts);
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
            if (!OperatingSystem.IsWindows())
                return false;

            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--service", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Holds the window open until somebody has read what is on it. A window launched from
        /// a file manager closes the instant this process exits, taking the last message with
        /// it before it can be read or spoken.
        ///
        /// This waits rather than pausing for a set time, and waits every time rather than only
        /// when it believes it owns the window. Both of those were tried and neither survived
        /// contact with a real machine: how long a message takes to read is not ours to guess,
        /// and asking Windows how many processes share this console answers wrongly under an
        /// elevated launch, which is exactly when somebody is attaching to a service. An extra
        /// keypress when running from an existing prompt is a far smaller cost than a message
        /// nobody can read. Redirected input reaches end of stream at once, so this never
        /// delays a script.
        /// </summary>
        private static void PauseBeforeClosing()
        {
            ConsoleSink.WriteLine(LocalizationService.Mark("Press Enter to close this window."));
            try
            {
                Console.ReadLine();
            }
            catch (IOException)
            {
            }
        }

        /// <summary>
        /// The first thing an attaching client is shown, so it answers the question somebody
        /// attaching to a server left running unattended actually has.
        /// </summary>
        private static string DescribeServerStatus(ServerSettings settings)
        {
            return LocalizationService.Format(
                LocalizationService.Mark("Connected to TopSpeed Server {0}, port {1}, update checking {2}."),
                ServerUpdateConfig.CurrentVersion.ToMachineString(),
                settings.Port,
                StartupUpdateModes.Normalize(settings.StartupUpdateMode));
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



