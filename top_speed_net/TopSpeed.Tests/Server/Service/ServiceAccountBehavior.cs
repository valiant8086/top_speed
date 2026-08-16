using FluentAssertions;
using TopSpeed.Server.Service;
using Xunit;

namespace TopSpeed.Tests.Server.Service
{
    /// <summary>
    /// Choosing the account a service is registered to run as.
    ///
    /// What this decides is who has to be able to replace the server's files when it updates
    /// itself, so getting it wrong is not noticed at install time. It surfaces weeks later as an
    /// update that will not apply, by which point nobody connects the two.
    ///
    /// The three inputs are awkward to arrange on a real machine — one of them needs an account
    /// that owns a folder and another needs sudo — and trivial to write down, which is why the
    /// decision is a function rather than something buried in the install.
    /// </summary>
    [Trait("Category", "Behavior")]
    public class ServiceAccountBehavior
    {
        [Fact]
        public void TheFolderOwnerWinsBecauseItIsTheOnlyFactHere()
        {
            // Sudo says who asked. The folder says who will be stuck if this is wrong.
            ServiceIdentity.ChooseServiceAccount("valiant8086", "someoneelse", "root")
                .Should().Be("valiant8086");
        }

        [Fact]
        public void AFolderOwnerIsFoundEvenWhenNothingRecordedWhoAsked()
        {
            // The su case. Debian offers a root password at installation and leaves that account
            // out of sudo, so su is the ordinary way to be root there, and it records nothing.
            // Without this the install is refused on a machine where every instruction it could
            // give involves a sudo that was never set up.
            ServiceIdentity.ChooseServiceAccount("valiant8086", null, "root")
                .Should().Be("valiant8086");
        }

        [Fact]
        public void AFolderOwnedByRootFallsBackToWhoeverAsked()
        {
            // A folder unpacked with sudo belongs to root while the person plainly does not.
            // Running as them is both safer than root and what was meant.
            ServiceIdentity.ChooseServiceAccount("root", "valiant8086", "root")
                .Should().Be("valiant8086");
        }

        [Fact]
        public void RootOwningTheFolderWithNobodyElseAroundIsARealAnswer()
        {
            // A rented server handed over with root as its only login, or a container. There is
            // no second account to strand, so root is correct rather than a failure.
            ServiceIdentity.ChooseServiceAccount("root", null, "root")
                .Should().Be("root");
        }

        [Fact]
        public void WithNoOwnerToBeFoundItIsWhoeverSudoRecorded()
        {
            // stat missing, or a filesystem that will not say. The older, poorer answer.
            ServiceIdentity.ChooseServiceAccount(null, "valiant8086", "root")
                .Should().Be("valiant8086");
        }

        [Fact]
        public void WithNothingToGoOnItIsTheAccountThisIsRunningAs()
        {
            ServiceIdentity.ChooseServiceAccount(null, null, "valiant8086")
                .Should().Be("valiant8086");
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void BlankAnswersCountAsNoAnswer(string blank)
        {
            ServiceIdentity.ChooseServiceAccount(blank, blank, "valiant8086")
                .Should().Be("valiant8086");
        }

        [Fact]
        public void SurroundingSpaceIsNotPartOfAnAccountName()
        {
            // stat prints a trailing newline, and a name with one in it would be written into a
            // unit file that then cannot start.
            ServiceIdentity.ChooseServiceAccount("  valiant8086\n", null, "root")
                .Should().Be("valiant8086");
        }
    }
}
