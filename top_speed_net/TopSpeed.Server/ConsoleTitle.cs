using System;
using System.IO;
using System.Runtime.InteropServices;
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
        /// Whether the window belongs to this program alone.
        ///
        /// A title outlives the program that set it, so renaming a window somebody else opened
        /// leaves their shell wearing our name for the rest of its life. Being the only process
        /// attached to the console is what tells the two apart: a copy started from a file
        /// manager has a console of its own, and a copy run from a prompt shares that prompt's.
        ///
        /// Windows only, and deliberately. Elsewhere a terminal is almost always one somebody
        /// else opened, and a service has no window at all.
        /// </summary>
        private static bool OwnsWindow()
        {
            if (!OperatingSystem.IsWindows())
                return false;

            try
            {
                return AttachedProcessCount() == 1;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }

        [SupportedOSPlatform("windows")]
        private static uint AttachedProcessCount()
        {
            // Two is the interesting answer and anything above it means the same thing, so the
            // buffer only has to be big enough to tell one from more than one.
            var processes = new uint[2];
            return GetConsoleProcessList(processes, (uint)processes.Length);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);
    }
}
