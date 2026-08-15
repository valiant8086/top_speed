using System;
using FluentAssertions;
using TopSpeed.Server.Service;
using Xunit;

namespace TopSpeed.Tests.Server.Service
{
    /// <summary>
    /// Telling apart root reached from somebody's account and root as the only account there is.
    ///
    /// Only the first does any harm: a folder owned by a person, holding files written by root,
    /// leaves its owner unable to replace them. Where root is the only login there is no second
    /// owner to lock out, and a rented server or a container is very often exactly that. Getting
    /// this wrong in the cautious direction is not safe, it just refuses to run at all on a
    /// machine where nothing was ever going to go wrong.
    /// </summary>
    [Trait("Category", "Behavior")]
    public class RootAccountBehavior
    {
        private static void WithSudoUser(string? value, Action check)
        {
            var previous = Environment.GetEnvironmentVariable("SUDO_USER");
            try
            {
                Environment.SetEnvironmentVariable("SUDO_USER", value);
                check();
            }
            finally
            {
                Environment.SetEnvironmentVariable("SUDO_USER", previous);
            }
        }

        [Fact]
        public void SudoFromAnOrdinaryAccountIsRootReachedFromSomewhereElse()
        {
            WithSudoUser("valiant8086", () =>
                ServiceIdentity.RootReachedFromAnotherAccount().Should().BeTrue());
        }

        [Fact]
        public void ALoginThatIsSimplyRootIsNot()
        {
            // A rented server whose only account is root, or a container. Nothing records how it
            // got there because it did not get there from anywhere.
            WithSudoUser(null, () =>
                ServiceIdentity.RootReachedFromAnotherAccount().Should().BeFalse());
        }

        [Fact]
        public void SudoUsedByRootIsStillJustRoot()
        {
            // Sudo run while already root records root as the asker. There is still one account,
            // so there is still nobody to lock out of the folder.
            WithSudoUser("root", () =>
                ServiceIdentity.RootReachedFromAnotherAccount().Should().BeFalse());
        }

        [Fact]
        public void AnEmptyValueIsTreatedAsAbsent()
        {
            WithSudoUser("   ", () =>
                ServiceIdentity.RootReachedFromAnotherAccount().Should().BeFalse());
        }
    }
}
