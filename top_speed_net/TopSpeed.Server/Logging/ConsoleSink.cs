using System;
using TopSpeed.Localization;
using TopSpeed.Server.Commands;

namespace TopSpeed.Server.Logging
{
    internal static class ConsoleSink
    {
        /// <summary>
        /// Goes to whichever session is active, which may be the server's own console window
        /// or somebody attached over the control connection. Translation happens here so a
        /// session only ever deals in finished text.
        /// </summary>
        public static bool WriteLine(string text)
        {
            return CommandSessions.WriteLine(LocalizationService.Translate(text));
        }

        public static bool WriteLineFormat(string template, params object[] args)
        {
            return WriteLine(LocalizationService.Format(template, args));
        }
    }
}
