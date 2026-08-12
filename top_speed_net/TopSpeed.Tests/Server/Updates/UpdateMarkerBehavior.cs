using System;
using System.Diagnostics;
using System.IO;
using FluentAssertions;
using TopSpeed.Server.Updates;
using Xunit;

namespace TopSpeed.Tests.Server.Updates
{
    /// <summary>
    /// The marker decides whether the program refuses to start, so every way of reading it
    /// wrongly ends in a folder nobody can start a server from. These are the ways it must
    /// answer no, which are the ones that keep that from happening; answering yes wrongly only
    /// costs somebody a second attempt.
    /// </summary>
    [Trait("Category", "Behavior")]
    public class UpdateMarkerBehavior : IDisposable
    {
        private readonly string _folder;

        public UpdateMarkerBehavior()
        {
            _folder = Path.Combine(Path.GetTempPath(), "ts-marker-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        private void WriteMarker(string contents)
        {
            File.WriteAllText(UpdateMarker.PathIn(_folder), contents);
        }

        [Fact]
        public void No_marker_means_nothing_is_under_way()
        {
            UpdateMarker.UpdateIsUnderWay(_folder, out _).Should().BeFalse();
        }

        [Fact]
        public void A_marker_naming_a_process_that_has_gone_is_abandoned()
        {
            // The updater died partway. Saying yes here would refuse to start a folder that
            // nothing is ever going to come back and finish.
            WriteMarker(FindAnUnusedProcessId().ToString());

            UpdateMarker.UpdateIsUnderWay(_folder, out _).Should().BeFalse();
        }

        [Fact]
        public void A_marker_naming_a_process_that_is_not_the_updater_is_abandoned()
        {
            // Process ids are handed out again once they are free. Without checking what the
            // number belongs to, a stranger inheriting it would keep the folder shut.
            WriteMarker(Process.GetCurrentProcess().Id.ToString());

            UpdateMarker.UpdateIsUnderWay(_folder, out _).Should().BeFalse();
        }

        [Fact]
        public void An_old_marker_is_abandoned_however_alive_its_process_looks()
        {
            // The backstop for an updater that hung rather than died. No unpack takes this long,
            // so past it the file is worth less than the ability to start the server.
            WriteMarker(Process.GetCurrentProcess().Id.ToString());
            File.SetLastWriteTimeUtc(UpdateMarker.PathIn(_folder), DateTime.UtcNow.AddHours(-1));

            UpdateMarker.UpdateIsUnderWay(_folder, out _).Should().BeFalse();
        }

        [Fact]
        public void A_marker_that_says_nothing_useful_is_abandoned()
        {
            // Truncated by a crash partway through writing it, or written by something older.
            WriteMarker("   ");

            UpdateMarker.UpdateIsUnderWay(_folder, out _).Should().BeFalse();
        }

        [Fact]
        public void Clearing_says_whether_there_was_one()
        {
            // What the server uses to decide whether to report an update that never finished,
            // so it has to tell the two apart rather than always tidying quietly.
            UpdateMarker.Clear(_folder).Should().BeFalse();

            UpdateMarker.Raise(_folder, 1234, windowComesBackByItself: false);
            UpdateMarker.Clear(_folder).Should().BeTrue();
            File.Exists(UpdateMarker.PathIn(_folder)).Should().BeFalse();
        }

        [Fact]
        public void What_is_raised_is_the_id_that_can_be_looked_up_later()
        {
            UpdateMarker.Raise(_folder, 4321, windowComesBackByItself: false);

            File.ReadAllLines(UpdateMarker.PathIn(_folder))[0].Trim().Should().Be("4321");
        }

        [Fact]
        public void A_service_update_is_written_without_the_window_note()
        {
            // Nothing opens a window at the end of a service update, so somebody who wants a
            // console has to ask for one. Saying otherwise would leave them waiting for a window
            // that is never coming.
            UpdateMarker.Raise(_folder, 4321, windowComesBackByItself: false);

            File.ReadAllText(UpdateMarker.PathIn(_folder)).Should().NotContain("window-returns");
        }

        [Fact]
        public void An_interactive_update_is_written_with_the_window_note()
        {
            // The updater opens this one again itself, so the advice has to be the opposite:
            // leave it alone. Starting a second server by hand first is the collision the whole
            // check exists to avoid.
            UpdateMarker.Raise(_folder, 4321, windowComesBackByItself: true);

            var lines = File.ReadAllLines(UpdateMarker.PathIn(_folder));
            lines[0].Trim().Should().Be("4321");
            lines[1].Trim().Should().Be("window-returns");
        }

        /// <summary>
        /// A number no process currently holds. Walked upwards from an implausible one rather
        /// than picked at random, so the test cannot fail on an unlucky draw.
        /// </summary>
        private static int FindAnUnusedProcessId()
        {
            for (var candidate = 999_999; candidate > 100_000; candidate--)
            {
                try
                {
                    using var existing = Process.GetProcessById(candidate);
                }
                catch (ArgumentException)
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException("Every process id tried was in use.");
        }
    }
}
