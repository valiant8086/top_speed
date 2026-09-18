using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using TopSpeed.Server.Commands;
using TopSpeed.Server.Control;
using TopSpeed.Server.Logging;
using Xunit;

namespace TopSpeed.Tests.Behavior.Server.Control
{
    /// <summary>
    /// One instance holds the command session and every other is told so. What these pin down
    /// is the telling: a further copy started while one is attached used to wait in a queue,
    /// unanswered, until the attached one left, which looked exactly like a server that had
    /// hung. Now it is answered at once, and by the server, which knows, rather than guessed by
    /// the client from a wait that timed out.
    ///
    /// These share the process-wide session state, so they live in one class and run in turn.
    /// </summary>
    [Trait("Category", "Behavior")]
    public class ControlListenerBehavior
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(3);

        private static string NewFolder()
        {
            var folder = Path.Combine(Path.GetTempPath(), "ts-ctl-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static Stream Connect(string folder)
        {
            ControlTransport.TryConnect(folder, Patience, out var stream).Should().Be(ControlConnectResult.Connected);
            return stream!;
        }

        /// <summary>Reads up to the given number of lines, stopping early at end of stream.</summary>
        private static string[] ReadLines(Stream stream, int count)
        {
            var lines = new List<string>();
            var reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            var work = Task.Run(() =>
            {
                for (var i = 0; i < count; i++)
                {
                    var line = reader.ReadLine();
                    if (line == null)
                        break;
                    lines.Add(line);
                }
            });

            work.Wait(Patience).Should().BeTrue("the server should answer within {0}", Patience);
            return lines.ToArray();
        }

        private static void WriteLine(Stream stream, string text)
        {
            var bytes = new UTF8Encoding(false).GetBytes(text + "\n");
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        [Fact]
        public void AnInstanceArrivingWhileAnotherIsAttachedIsToldSoAtOnce()
        {
            var folder = NewFolder();
            var logger = new Logger(LogLevel.None, null, writeToConsole: false);
            using var listener = new ControlListener(folder, logger, () => "status");
            listener.TryStart().Should().BeTrue();

            using var first = Connect(folder);
            var welcome = ReadLines(first, 3);
            welcome.Should().HaveCount(3);
            welcome[0].Should().StartWith(ControlListener.Protocol);
            welcome[1].Should().Be("OK");
            welcome[2].Should().Be("status");

            var clock = Stopwatch.StartNew();
            using var second = Connect(folder);
            var answer = ReadLines(second, 3);
            clock.Stop();

            answer.Should().HaveCount(3);
            answer[0].Should().StartWith(ControlListener.Protocol);
            answer[1].Should().Be("REFUSED AlreadyAttached");
            // Answered, not queued behind the first: the old behaviour was silence until the
            // attached instance left, which could be hours.
            clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));

            // And then shown the door, so the refused copy can print the reason and exit rather
            // than sit on an open connection.
            ReadLines(second, 1).Should().BeEmpty();
        }

        [Fact]
        public void TheSessionIsFreeAgainOnceTheAttachedInstanceHasGone()
        {
            var folder = NewFolder();
            var logger = new Logger(LogLevel.None, null, writeToConsole: false);
            using var listener = new ControlListener(folder, logger, () => "status");
            listener.TryStart().Should().BeTrue();

            // Stands in for the command host, which is what reads the attached session and so
            // is what notices, through the read ending, that its client has gone. No handshake
            // is involved: the operating system ends the read when the process does, however
            // the process went.
            string? typed = null;
            var host = new Thread(() => CommandSessions.TryReadLine(out typed)) { IsBackground = true };

            var first = Connect(folder);
            ReadLines(first, 3)[1].Should().Be("OK");
            host.Start();

            // Dropped without a word, as a killed process would be.
            first.Dispose();

            // The listener looks round once a slice; give it a couple.
            Thread.Sleep(750);

            using var next = Connect(folder);
            var welcome = ReadLines(next, 3);
            welcome.Should().HaveCount(3);
            welcome[1].Should().Be("OK", "the session should be free once its holder has gone");

            // The stand-in host now reads this session; a line from it lets the thread finish.
            WriteLine(next, "hello");
            host.Join(Patience).Should().BeTrue();
            typed.Should().Be("hello");
        }
    }
}
