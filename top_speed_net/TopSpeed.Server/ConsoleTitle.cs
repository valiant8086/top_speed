using System;
using System.IO;
using System.Runtime.Versioning;

namespace TopSpeed.Server
{
    /// <summary>
    /// Names the window this program is running in, when the window is its own to name.
    ///
    /// Two servers on one machine mean two windows that otherwise look identical in the task
    /// bar and read identically to a screen reader, and a window attached to a server looks
    /// like the server itself. A title says which is which without anything being typed.
    /// </summary>
    internal static class ConsoleTitle
    {
        /// <summary>The product, which is not translated because it is a name.</summary>
        public const string Product = "TopSpeed Server";

        public static void Set(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || !OwnsWindow())
                return;

            try
            {
                Console.Title = text;
            }
            catch (IOException)
            {
            }
            catch (PlatformNotSupportedException)
            {
            }
        }

        /// <summary>
        /// Whether the window was opened to run this program, rather than one we are a guest in.
        ///
        /// A title outlives the program that set it, so renaming a window somebody else opened
        /// leaves their shell wearing our name for the rest of its life.
        ///
        /// Windows names a console it creates after the file of the program it created it for,
        /// and a shell that owns its own console has already named that console after itself. So
        /// finding our own path in the title is Windows saying this window exists to run us, and
        /// finding anything else means somebody else was here first.
        ///
        /// Not by counting who is attached to the console, which is the obvious way and is
        /// wrong: a screen reader attaches to a console in order to read it, so on the machines
        /// this program is written for the count is never one, and the windows that would go
        /// unnamed are exactly the ones whose names are read aloud.
        ///
        /// Windows only, and deliberately. Elsewhere a terminal is almost always one somebody
        /// else opened, and a service has no window at all.
        /// </summary>
        private static bool OwnsWindow()
        {
            if (!OperatingSystem.IsWindows())
                return false;

            var self = Environment.ProcessPath;
            return !string.IsNullOrEmpty(self)
                && string.Equals(CurrentTitle(), self, StringComparison.OrdinalIgnoreCase);
        }

        [SupportedOSPlatform("windows")]
        private static string? CurrentTitle()
        {
            try
            {
                return Console.Title;
            }
            catch (IOException)
            {
                return null;
            }
            catch (PlatformNotSupportedException)
            {
                return null;
            }
        }
    }
}
