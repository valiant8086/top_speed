using FluentAssertions;
using TopSpeed.Server;
using Xunit;

namespace TopSpeed.Tests.Server
{
    [Trait("Category", "Behavior")]
    public class ConsoleTitleBehavior
    {
        private const string Exe = @"C:\games\tsServer\TopSpeed.Server.exe";

        [Fact]
        public void A_window_named_after_this_program_is_ours()
        {
            // Windows names a console it creates after the file it created it for, so this is
            // Windows saying the window exists to run us.
            ConsoleTitle.NameSaysTheWindowIsOurs(Exe, Exe).Should().BeTrue();
        }

        [Theory]
        [InlineData("Windows PowerShell")]
        [InlineData(@"C:\windows\system32\cmd.exe")]
        [InlineData("claude")]
        public void A_window_somebody_else_named_is_theirs(string title)
        {
            // A title outlives the program that set it, so a shell's window must be left with
            // the name its owner gave it.
            ConsoleTitle.NameSaysTheWindowIsOurs(title, Exe).Should().BeFalse();
        }

        [Fact]
        public void Spelling_of_the_path_does_not_decide_it()
        {
            ConsoleTitle.NameSaysTheWindowIsOurs(Exe.ToUpperInvariant(), Exe).Should().BeTrue();
        }

        [Fact]
        public void Nothing_to_compare_means_not_ours()
        {
            // A process with no console has no title to read, and refusing is the safe way to
            // be wrong: an unnamed window costs nothing, somebody else's renamed window lasts.
            ConsoleTitle.NameSaysTheWindowIsOurs(null, Exe).Should().BeFalse();
            ConsoleTitle.NameSaysTheWindowIsOurs(Exe, null).Should().BeFalse();
        }

        [Fact]
        public void A_title_this_program_already_set_no_longer_looks_like_ours()
        {
            // Not a defect to be fixed here, and the reason the caller asks this once and keeps
            // the answer. Naming the window destroys the evidence that it was ever ours, so a
            // window told what it is running would refuse every later name it earns, including
            // the one that says it is now attached to the service.
            ConsoleTitle.NameSaysTheWindowIsOurs("TopSpeed Server, port 28630", Exe)
                .Should().BeFalse();
        }
    }
}
