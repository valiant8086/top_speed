using System;
using System.Collections.Generic;

namespace TopSpeed.Server.Logging
{
    [Flags]
    internal enum LogLevel
    {
        None = 0,
        Error = 1 << 0,
        Warning = 1 << 1,
        Info = 1 << 2,
        Debug = 1 << 3,
        All = Error | Warning | Info | Debug
    }

    /// <summary>
    /// The vocabulary the --log-level option and the logLevel setting share, so that a level
    /// named in one of them means the same thing in the other.
    /// </summary>
    internal static class LogLevels
    {
        /// <summary>
        /// What a server logs when nobody has said otherwise: everything it does that somebody
        /// might read afterwards, without the running commentary debug adds.
        /// </summary>
        public const LogLevel Default = LogLevel.Error | LogLevel.Warning | LogLevel.Info;

        /// <summary>
        /// What the settings menu offers. Any combination can still be written into the setting
        /// or passed on the command line; these are only the three worth having a menu for.
        /// </summary>
        public static readonly string[] Presets = { "error,warning", "error,warning,info", "all" };

        /// <summary>
        /// Reads a comma separated list. Null when the text names no level this understands,
        /// which is what lets a caller fall back to whatever it would have used anyway.
        /// </summary>
        public static LogLevel? Parse(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var levels = LogLevel.None;
            var parts = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                switch (part.Trim().ToLowerInvariant())
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

            return levels == LogLevel.None ? null : levels;
        }

        /// <summary>
        /// The levels named as the option names them, and never translated. What a menu shows,
        /// what the settings file holds and what --log-level takes are the same words, so that
        /// reading one of them teaches the others rather than only itself.
        /// </summary>
        public static string Format(LogLevel levels)
        {
            if (levels == LogLevel.None)
                return "none";
            if (levels == LogLevel.All)
                return "all";

            var parts = new List<string>();
            if ((levels & LogLevel.Error) != 0)
                parts.Add("error");
            if ((levels & LogLevel.Warning) != 0)
                parts.Add("warning");
            if ((levels & LogLevel.Info) != 0)
                parts.Add("info");
            if ((levels & LogLevel.Debug) != 0)
                parts.Add("debug");
            return parts.Count == 0 ? "none" : string.Join(",", parts);
        }

        /// <summary>Text as it is written in the settings file, which is text as it is shown.</summary>
        public static string Normalize(string? value)
        {
            return Format(Parse(value) ?? Default);
        }
    }
}
