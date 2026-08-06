using System;
using TopSpeed.Server.Updates;
using Xunit;

namespace TopSpeed.Tests;

[Trait("Category", "Behavior")]
public sealed class UpdateRetryScheduleBehaviorTests
{
    [Theory]
    [InlineData(1, 20)]
    [InlineData(2, 40)]
    [InlineData(3, 60)]
    [InlineData(4, 60)]
    [InlineData(24, 60)]
    public void NextDelay_ShouldStepFromTwentyMinutesToHourly(int completedAttempts, int expectedMinutes)
    {
        var delay = UpdateRetrySchedule.NextDelay(completedAttempts);

        delay.Should().Be(TimeSpan.FromMinutes(expectedMinutes));
    }

    [Fact]
    public void NextDelay_ShouldPutTheFirstRetryPastTheUsualPublishDelay()
    {
        // A release normally becomes downloadable about fifteen minutes after the version
        // is bumped, so the first retry has to land after that to be worth making.
        UpdateRetrySchedule.NextDelay(1).Should().BeGreaterThan(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void ElapsedAtAttempt_ShouldReachTheHourOnTheThirdAttempt()
    {
        UpdateRetrySchedule.ElapsedAtAttempt(1).Should().Be(TimeSpan.Zero);
        UpdateRetrySchedule.ElapsedAtAttempt(2).Should().Be(TimeSpan.FromMinutes(20));
        UpdateRetrySchedule.ElapsedAtAttempt(3).Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void ElapsedAtAttempt_ShouldFinishTheCycleAtHourTwentyThree()
    {
        var elapsed = UpdateRetrySchedule.ElapsedAtAttempt(UpdateRetrySchedule.MaxAttempts);

        elapsed.Should().Be(TimeSpan.FromHours(23));
    }

    [Fact]
    public void ACycleShouldAlwaysFinishBeforeTheDailyIntervalThatFollowsIt()
    {
        // The retry schedule and the daily check share one timer, so a cycle that ran past
        // the daily interval would mean two checks were due at once.
        var worstCase = UpdateRetrySchedule.ElapsedAtAttempt(UpdateRetrySchedule.MaxAttempts)
            + TimeSpan.FromMilliseconds(UpdateRetrySchedule.MaxJitter.TotalMilliseconds * UpdateRetrySchedule.MaxAttempts);

        worstCase.Should().BeLessThan(UpdateRetrySchedule.DailyInterval);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(24, false)]
    [InlineData(25, true)]
    [InlineData(26, true)]
    public void IsExhausted_ShouldStopAfterTheLastAttempt(int completedAttempts, bool expected)
    {
        UpdateRetrySchedule.IsExhausted(completedAttempts).Should().Be(expected);
    }

    [Fact]
    public void ApplyJitter_ShouldOnlyEverLengthenTheDelay()
    {
        var random = new Random(1234);
        var baseDelay = UpdateRetrySchedule.NextDelay(1);

        for (var i = 0; i < 500; i++)
        {
            var jittered = UpdateRetrySchedule.ApplyJitter(baseDelay, random);

            jittered.Should().BeGreaterThanOrEqualTo(baseDelay);
            jittered.Should().BeLessThanOrEqualTo(baseDelay + UpdateRetrySchedule.MaxJitter);
        }
    }
}
