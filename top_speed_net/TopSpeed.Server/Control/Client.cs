using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using TopSpeed.Localization;

namespace TopSpeed.Server.Control
{
    internal enum ControlClientOutcome
    {
        /// <summary>Nothing is running here. The caller should start a server normally.</summary>
        NoServerRunning,

        /// <summary>A session ran and has now ended.</summary>
        SessionEnded,

        /// <summary>A server is running but would not give us a session.</summary>
        Refused,

        Failed
    }

    /// <summary>
    /// The other half of the control channel: connects to a server already running from this
    /// folder and hands its console over to it.
    /// </summary>
    internal static class ControlClient
    {
        public static ControlClientOutcome Run(string directory)
        {
            var result = ControlTransport.TryConnect(directory, TimeSpan.FromSeconds(3), out var stream);
            switch (result)
            {
                case ControlConnectResult.NotRunning:
                    return ControlClientOutcome.NoServerRunning;

                case ControlConnectResult.AccessDenied:
                    // Distinguished from "nothing there" so the caller never has to elevate
                    // speculatively just to find out whether a server exists.
                    WriteLine(LocalizationService.Translate(LocalizationService.Mark(
                        "A server is already running from this folder, but this account is not allowed to control it. Run as an administrator to attach to it.")));
                    return ControlClientOutcome.Refused;

                case ControlConnectResult.Busy:
                    WriteLine(LocalizationService.Translate(LocalizationService.Mark(
                        "Another window on this machine is already attached to this server. Use that window, or close it to free the connection.")));
                    return ControlClientOutcome.Refused;

                case ControlConnectResult.Failed:
                    WriteLine(LocalizationService.Translate(LocalizationService.Mark(
                        "Could not reach the server running from this folder.")));
                    return ControlClientOutcome.Failed;
            }

            if (stream == null)
                return ControlClientOutcome.Failed;

            using (stream)
                return Pump(stream);
        }

        private static ControlClientOutcome Pump(Stream stream)
        {
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            using var reader = new StreamReader(stream, encoding, false, 4096, leaveOpen: true);
            using var writer = new StreamWriter(stream, encoding, 4096, leaveOpen: true) { AutoFlush = true };

            var greeting = reader.ReadLine();
            if (greeting == null || !greeting.StartsWith(ControlListener.Protocol, StringComparison.Ordinal))
            {
                WriteLine(LocalizationService.Translate(LocalizationService.Mark(
                    "The program answering on this folder is not a TopSpeed server.")));
                return ControlClientOutcome.Failed;
            }

            var verdict = reader.ReadLine();
            if (verdict == null)
                return ControlClientOutcome.Failed;

            if (verdict.StartsWith("REFUSED", StringComparison.Ordinal))
            {
                var reason = reader.ReadLine();
                if (!string.IsNullOrWhiteSpace(reason))
                    WriteLine(reason);
                return ControlClientOutcome.Refused;
            }

            // Everything the server prints arrives on its own thread so that typing and output
            // do not have to take turns.
            var pumping = true;
            var reading = new Thread(() =>
            {
                try
                {
                    while (pumping)
                    {
                        var line = reader.ReadLine();
                        if (line == null)
                            break;

                        WriteLine(line);
                    }
                }
                catch (IOException)
                {
                }
                finally
                {
                    pumping = false;
                }
            })
            {
                IsBackground = true,
                Name = "TopSpeed.Server.Attach"
            };
            reading.Start();

            try
            {
                while (pumping)
                {
                    var input = Console.ReadLine();
                    if (input == null)
                        break;

                    if (string.Equals(input.Trim(), "exit", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteLine(LocalizationService.Translate(LocalizationService.Mark(
                            "Disconnected. The server is still running.")));
                        break;
                    }

                    writer.WriteLine(input);
                }
            }
            catch (IOException)
            {
            }
            finally
            {
                pumping = false;
            }

            return ControlClientOutcome.SessionEnded;
        }

        private static void WriteLine(string text)
        {
            try
            {
                Console.WriteLine(text);
            }
            catch (IOException)
            {
            }
        }

        /// <summary>
        /// True when this process created the console window it is using, meaning the window
        /// will vanish the moment it exits. Something launched from Explorer needs to be given
        /// a chance to be read before that happens; something launched from an existing prompt
        /// does not, and pausing there is just an irritation.
        /// </summary>
        public static bool OwnsConsoleWindow()
        {
            if (!OperatingSystem.IsWindows())
                return false;

            try
            {
                var buffer = new uint[4];
                return GetConsoleProcessList(buffer, (uint)buffer.Length) <= 1;
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

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);
    }
}
