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
                        "Another instance is already attached to this service.")));
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

            // Named only now, because a window that was refused a session is attached to nothing
            // and should not say otherwise. The port comes from the settings the server it just
            // reached is running on, which is the one thing this window knows about that server
            // without asking it.
            var port = Service.ServiceIdentity.ReadConfiguredPort(directory);
            ConsoleTitle.Set(port > 0
                ? LocalizationService.Format(
                    LocalizationService.Mark("{0}, port {1}, attached"), ConsoleTitle.Product, port)
                : ConsoleTitle.Product);

            // Everything the server prints arrives on its own thread so that typing and output
            // do not have to take turns.
            var pumping = true;
            var serverWentAway = false;

            // Held from the moment a typed line is in hand until the work it asked for is
            // finished. The window may be ended out from under somebody waiting at an empty
            // prompt, and never partway through a command: removing a service outlives the
            // session it was asked through, and a handover means to come back.
            var gate = new object();
            var running = false;

            void AnnounceServerGone()
            {
                // Not said during a handover: the server stopping is the point of that, and this
                // window has already been told it is going to the service instead.
                if (!serverWentAway || Service.ServiceRuntime.HandingOverToService)
                    return;

                WriteLine(LocalizationService.Translate(LocalizationService.Mark(
                    "The server has stopped, so this window is no longer attached to anything.")));
            }

            // Reached when the server goes while nobody is asking this window for anything.
            //
            // Waiting for a line cannot be interrupted, so a window in that state has no way to
            // learn its server has gone: it sits there looking attached and then closes on the
            // next key pressed, as though that key had done it. Ending it here is the same
            // ending every other route reaches, at the moment it becomes true rather than at the
            // moment somebody happens to touch the keyboard.
            void EndWindowIfWaiting()
            {
                lock (gate)
                {
                    if (running || Service.ServiceRuntime.HandingOverToService)
                        return;

                    AnnounceServerGone();
                    Environment.Exit(0);
                }
            }

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
                    EndWindowIfWaiting();
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

                    lock (gate)
                        running = true;

                    try
                    {
                        // Handled here rather than sent on, for the same reason exit is: this is
                        // a process the owner launched, so it is the one that may ask for the
                        // rights installing or removing a service needs. The server on the other
                        // end is very likely the service itself, which cannot ask for anything.
                        //
                        // Matched on the first word so that "service stop" is kept here too.
                        // Sending it on would land it at the service and be refused, which is the
                        // same dead end by a longer route.
                        if (IsCommand(input, "service", out var serviceArguments))
                        {
                            // The server holding the folder is the one at the other end, so
                            // stopping it means asking it to, which is exactly what typing
                            // shutdown would do.
                            Service.ServiceConsole.Run(
                                serviceArguments,
                                directory,
                                () => writer.WriteLine("shutdown"));

                            // The folder is changing hands, so there is nothing left here to type
                            // at. Waiting for the far end to go is what lets this window carry on
                            // by itself: it is the point at which the server has genuinely
                            // released the folder, and reaching it by returning to the prompt
                            // would mean the handover only happened once somebody pressed a key
                            // at a dead console.
                            if (Service.ServiceRuntime.HandingOverToService)
                            {
                                reading.Join(TimeSpan.FromSeconds(60));
                                break;
                            }

                            continue;
                        }

                        // Nothing is said on the way out. Whatever was printed here would be on
                        // screen for the moment it takes this to return and the window to close,
                        // which is not long enough to read and was one more sentence to translate.
                        if (string.Equals(input.Trim(), "exit", StringComparison.OrdinalIgnoreCase))
                            break;

                        writer.WriteLine(input);
                    }
                    finally
                    {
                        lock (gate)
                            running = false;
                    }
                }
            }
            catch (IOException)
            {
            }
            finally
            {
                pumping = false;
            }

            // Reached when the server went while a command was being carried out, which is the
            // other way round from the one above and ends the same way.
            AnnounceServerGone();
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
