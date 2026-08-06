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

        private static bool IsTakeoverRequested(string[] args)
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--takeover", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool IsAttachArgument(string arg)
        {
            return string.Equals(arg, "--attach", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "--takeover", StringComparison.OrdinalIgnoreCase);
        }

        private static void PauseIfConsoleWillVanish(ControlClientOutcome outcome)
        {
            // Only when this process created the window, so a prompt launched from an existing
            // terminal is not made to wait for a keypress nobody asked for.
            if (outcome == ControlClientOutcome.SessionEnded || !ControlClient.OwnsConsoleWindow())
                return;

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



