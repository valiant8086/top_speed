using System;

namespace TopSpeed.Server.Config
{
    internal enum StartupUpdateMode
    {
        Off,
        Notify,
        Auto
    }

    internal static class StartupUpdateModes
    {
        public const string Off = "off";
        public const string Notify = "notify";
        public const string Auto = "auto";

        public static readonly string[] All = { Off, Notify, Auto };

        public static StartupUpdateMode Parse(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return StartupUpdateMode.Off;

            var trimmed = value.Trim();
            if (string.Equals(trimmed, Notify, StringComparison.OrdinalIgnoreCase))
                return StartupUpdateMode.Notify;
            if (string.Equals(trimmed, Auto, StringComparison.OrdinalIgnoreCase))
                return StartupUpdateMode.Auto;

            return StartupUpdateMode.Off;
        }

        public static string ToTag(StartupUpdateMode mode)
        {
            return mode switch
            {
                StartupUpdateMode.Notify => Notify,
                StartupUpdateMode.Auto => Auto,
                _ => Off
            };
        }

        public static string Normalize(string? value)
        {
            return ToTag(Parse(value));
        }
    }
}
