using System;
using System.IO;
using FluentAssertions;
using TopSpeed.Server.Control;
using TopSpeed.Server.Service;
using Xunit;

namespace TopSpeed.Tests.Server.Service
{
    [Trait("Category", "Behavior")]
    public class ServiceIdentityBehavior
    {
        [Fact]
        public void Two_folders_get_two_service_names()
        {
            // The whole point of deriving the name: somebody running two servers must be able
            // to install both without naming either.
            var first = ServiceIdentity.NameFor(@"C:\games\serverA");
            var second = ServiceIdentity.NameFor(@"C:\games\serverB");

            first.Should().NotBe(second);
        }

        [Fact]
        public void The_same_folder_always_gets_the_same_name()
        {
            // A running service works out what it was registered as by looking at where it is
            // running from, so this has to survive restarts and spelling.
            var plain = ServiceIdentity.NameFor(@"C:\games\tsServer");
            var trailing = ServiceIdentity.NameFor(@"C:\games\tsServer\");

            trailing.Should().Be(plain);
        }

        [Fact]
        public void The_service_name_matches_the_control_endpoint_key()
        {
            // One key for both, so there is never a second thing to keep in step.
            ServiceIdentity.NameFor(@"C:\games\tsServer")
                .Should().Be(ControlEndpoint.PipeNameFor(@"C:\games\tsServer"));
        }

        [Fact]
        public void The_display_name_says_which_server_this_is()
        {
            // What tells two installations apart in a list of services.
            ServiceIdentity.DisplayNameFor(@"C:\games\tsServer").Should().Contain("tsServer");
        }

        [Fact]
        public void The_display_name_leaves_out_anything_that_can_change_later()
        {
            // A registration is written once. The port is a setting somebody can change from
            // the options menu or by editing the file, and nothing revisits the label when they
            // do, so a service list carrying it would eventually be confidently wrong.
            ServiceIdentity.DisplayNameFor(@"C:\games\tsServer").Should().NotContain("port");
        }

        [Fact]
        public void The_display_name_keeps_the_folder_spelled_the_way_the_owner_spelled_it()
        {
            // The name used for identity folds case so one folder maps to one service. Reusing
            // that for the label would show somebody a lowercased version of their own folder.
            ServiceIdentity.DisplayNameFor(@"C:\games\tsServer").Should().Contain("tsServer");
        }

        [Fact]
        public void The_registered_path_keeps_the_case_of_the_real_folder()
        {
            // Windows would tolerate a lowercased path, but it reads as wrong in the service
            // list, and on a system where names are case sensitive it would not find the file.
            ServiceIdentity.ExecutablePathFor(@"C:\games\tsServer").Should().Contain("tsServer");
        }

        [Fact]
        public void The_registered_command_quotes_the_path_and_asks_for_service_mode()
        {
            // An unquoted path with a space in it is the classic way to register a service
            // that installs cleanly and then refuses to start.
            var command = ServiceIdentity.CommandLineFor(@"C:\Program Data\ts server");

            command.Should().StartWith("\"");
            command.Should().EndWith("--service");
            command.Should().Contain("\" --service");
        }

        [Fact]
        public void Installing_from_inside_program_files_is_refused()
        {

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            // The server rewrites its own folder when it updates. Granting a service write
            // access inside a trusted location would turn that trust into a way up.
            ServiceIdentity.IsProtectedLocation(Path.Combine(programFiles, "TopSpeed"), out var location)
                .Should().BeTrue();
            location.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void An_ordinary_folder_is_allowed()
        {

            ServiceIdentity.IsProtectedLocation(@"C:\games\tsServer", out _).Should().BeFalse();
        }

        [Fact]
        public void A_folder_merely_named_like_a_protected_one_is_allowed()
        {

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            // Comparing paths as plain text without minding the separator would wrongly refuse
            // this one, and refusing somebody's ordinary folder is a bug they cannot work around.
            ServiceIdentity.IsProtectedLocation(programFiles + " extra", out _).Should().BeFalse();
        }

        [Fact]
        public void A_folder_with_no_settings_reports_no_port_rather_than_failing()
        {
            // Installing before the server has ever run is a perfectly ordinary order to do
            // things in, and the port is only ever used to label the service.
            var empty = Path.Combine(Path.GetTempPath(), "topspeed-tests-" + Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(empty);
            try
            {
                ServiceIdentity.ReadConfiguredPort(empty).Should().Be(0);
            }
            finally
            {
                Directory.Delete(empty, true);
            }
        }

        [Fact]
        public void The_configured_port_is_read_without_writing_a_settings_file()
        {
            var folder = Path.Combine(Path.GetTempPath(), "topspeed-tests-" + Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(folder);
            try
            {
                File.WriteAllText(Path.Combine(folder, "settings.json"), "{\"port\": 40100}");

                ServiceIdentity.ReadConfiguredPort(folder).Should().Be(40100);

                // Reading must not have created or rewritten anything: doing so while elevated
                // would leave the server a file its own account may not be able to write later.
                Directory.GetFiles(folder).Should().ContainSingle();
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
