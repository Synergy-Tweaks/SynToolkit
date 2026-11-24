using Roomy.Core;

namespace Roomy.Tests;

public sealed class ReminderTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    static Preferences Enabled => new() { RunInBackground = true, LowSpaceAlerts = true, WeeklyChecks = true };

    [Fact]
    public void NewInstallDoesNotRunOrNotify()
    {
        var p = new Preferences();
        Assert.False(p.RunInBackground);
        Assert.False(p.LowSpaceAlerts);
        Assert.False(p.WeeklyChecks);
        Assert.False(ReminderPolicy.CanNotify(p, Now));
        Assert.False(ReminderPolicy.WeeklyCheckDue(p, Now));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(10737418239L, true)]
    [InlineData(10737418240L, false)]
    public void LowSpaceUsesStrictThresholdAndIgnoresInvalidReadings(long free, bool expected) =>
        Assert.Equal(expected, ReminderPolicy.IsLowSpace(Enabled, free));

    [Fact]
    public void DisabledLowSpaceAlertsIgnoreFullDrive() =>
        Assert.False(ReminderPolicy.IsLowSpace(Enabled with { LowSpaceAlerts = false }, 0));

    [Theory]
    [InlineData(23.99, false)]
    [InlineData(24, true)]
    [InlineData(-1, false)]
    public void DailyLimitAlsoHandlesClockMovingBackwards(double hours, bool expected) =>
        Assert.Equal(expected, ReminderPolicy.CanNotify(Enabled with { LastNotification = Now.AddHours(-hours) }, Now));

    [Fact]
    public void SnoozeBlocksBothKindsOfReminder()
    {
        var p = Enabled with { SnoozedUntil = Now.AddDays(1) };
        Assert.False(ReminderPolicy.CanNotify(p, Now));
        Assert.False(ReminderPolicy.WeeklyCheckDue(p, Now));
        Assert.True(ReminderPolicy.CanNotify(p, Now.AddDays(1)));
    }

    [Fact]
    public void WeeklyCheckWaitsSevenDaysAndSharesNotificationCooldown()
    {
        var p = Enabled with { LastWeeklyCheck = Now.AddDays(-6) };
        Assert.False(ReminderPolicy.WeeklyCheckDue(p, Now));
        Assert.True(ReminderPolicy.WeeklyCheckDue(p, Now.AddDays(1)));
        Assert.False(ReminderPolicy.WeeklyCheckDue(p with { LastNotification = Now.AddHours(12) }, Now.AddDays(1)));
    }

    [Fact]
    public void TurningOffBackgroundStopsAllReminders()
    {
        var p = Enabled with { RunInBackground = false };
        Assert.False(ReminderPolicy.CanNotify(p, Now));
        Assert.False(ReminderPolicy.WeeklyCheckDue(p, Now));
    }

    [Theory]
    [InlineData(-100, 1)]
    [InlineData(999, 500)]
    [InlineData(20, 20)]
    public void InvalidThresholdCannotOverflowOrTriggerEveryDrive(int input, int expected) =>
        Assert.Equal(expected, (Enabled with { LowSpaceGB = input }).Normalize().LowSpaceGB);
}
