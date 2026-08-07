using System;
using System.IO;
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
                return Pump(stream, directory);
        }

        private static ControlClientOutcome Pump(Stream stream, string directory)
        {
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            var reader = new StreamReader(stream, encoding, false, 4096, leaveOpen: true);
            var writer = new StreamWriter(stream, encoding, 4096, leaveOpen: true) { AutoFlush = true };

            // Torn down by hand rather than with using, because tidying up a writer flushes it,
            // and flushing to a server that has gone throws where nothing is catching. A window
            // whose server stopped would end on an unhandled error instead of saying what
            // happened, which is the worst possible reading of an ordinary event.
            try
            {
                return PumpLines(reader, writer, directory);
            }
            finally
            {
                DisposeQuietly(writer);
                DisposeQuietly(reader);
            }
        }

        private static ControlClientOutcome PumpLines(StreamReader reader, StreamWriter writer, string directory)
        {
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
            var serverWentAway = false;
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
                    // The connection ended from the far side rather than because this window
                    // asked to leave. Worth telling apart: stopping or removing a service ends
                    // the very session it was asked through, and a window that simply fell
                    // silent at that point reads like a crash.
                    serverWentAway = pumping;
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

                    // Handled here rather than sent on, for the same reason exit is: this is a
                    // process the owner launched, so it is the one that may ask for the rights
                    // installing or removing a service needs. The server on the other end is
                    // very likely the service itself, which cannot ask for anything.
                    //
                    // Matched on the first word so that "service stop" is kept here too. Sending
                    // it on would land it at the service and be refused, which is the same dead
                    // end by a longer route.
                    if (IsCommand(input, "service", out var serviceArguments))
                    {
                        // The server holding the folder is the one at the other end, so stopping
                        // it means asking it to, which is exactly what typing shutdown would do.
                        Service.ServiceConsole.Run(
                            serviceArguments,
                            directory,
                            () => writer.WriteLine("shutdown"));
                        continue;
                    }

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

            // Not said during a handover: the server stopping is the point of that, and this
            // window has already been told it will be connecting to the service instead.
            if (serverWentAway && !Service.ServiceRuntime.HandingOverToService)
            {
                WriteLine(LocalizationService.Translate(LocalizationService.Mark(
                    "The server has stopped, so this window is no longer attached to anything.")));
            }

            return ControlClientOutcome.SessionEnded;
        }

        /// <summary>
        /// Closing something whose far end has already gone is not news worth reporting, and
        /// certainly not worth ending on.
        /// </summary>
        private static void DisposeQuietly(IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        /// <summary>
        /// Whether a typed line is this command, and whatever followed it.
        ///
        /// Matching the whole line would let "service stop" past, and anything that gets past
        /// here is sent to the server, which for these commands is the one process that cannot
        /// carry them out.
        /// </summary>
        private static bool IsCommand(string input, string name, out string arguments)
        {
            arguments = string.Empty;
            var text = input.Trim();

            if (string.Equals(text, name, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!text.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase))
                return false;

            arguments = text.Substring(name.Length).Trim();
            return true;
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
    }
}
