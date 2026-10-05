#nullable enable

using System;
using System.Collections.Generic;

namespace SynToolkit.Services
{
    internal enum NeedsAttentionIgnoreDuration
    {
        UntilTomorrow,
        OneDay,
        TwoDays,
        ThreeDays,
        FourDays,
        OneWeek,
        TwoWeeks,
        ThreeWeeks,
        OneMonth,
        TwoMonths,
        ThreeMonths,
        SixMonths,
        OneYear,
        TwoYears,
        Forever
    }

    /// <summary>
    /// Stable category IDs for Needs Attention checks. Ignoring is stored per ID so a future
    /// check is never suppressed until the user explicitly ignores that new ID.
    /// </summary>
    internal static class NeedsAttentionCheckIds
    {
        public const string Drivers = "drivers";
        public const string Apps = "apps";
        public const string SynToolkitVersion = "syntoolkit_version";
        public const string RestorePoint = "restore_point";
        public const string WindowsTime = "windows_time";
        public const string LowDisk = "low_disk";

        public static IReadOnlyList<string> All { get; } =
        [
            Drivers,
            Apps,
            SynToolkitVersion,
            RestorePoint,
            WindowsTime,
            LowDisk,
        ];
    }

    /// <summary>
    /// Converts a chosen ignore duration into the UTC instant a warning becomes visible again.
    /// Kept free of Windows/app dependencies so it can be covered by the service test runner.
    /// </summary>
    internal static class NeedsAttentionIgnorePolicy
    {
        public static NeedsAttentionIgnoreDuration Default => NeedsAttentionIgnoreDuration.ThreeMonths;

        public static IReadOnlyList<NeedsAttentionIgnoreDuration> All { get; } =
            (NeedsAttentionIgnoreDuration[])Enum.GetValues(typeof(NeedsAttentionIgnoreDuration));

        public static DateTimeOffset ResolveExpiry(NeedsAttentionIgnoreDuration duration, DateTimeOffset nowUtc) =>
            duration switch
            {
                NeedsAttentionIgnoreDuration.UntilTomorrow => GetNextLocalMidnight(nowUtc),
                NeedsAttentionIgnoreDuration.OneDay => nowUtc.AddDays(1),
                NeedsAttentionIgnoreDuration.TwoDays => nowUtc.AddDays(2),
                NeedsAttentionIgnoreDuration.ThreeDays => nowUtc.AddDays(3),
                NeedsAttentionIgnoreDuration.FourDays => nowUtc.AddDays(4),
                NeedsAttentionIgnoreDuration.OneWeek => nowUtc.AddDays(7),
                NeedsAttentionIgnoreDuration.TwoWeeks => nowUtc.AddDays(14),
                NeedsAttentionIgnoreDuration.ThreeWeeks => nowUtc.AddDays(21),
                NeedsAttentionIgnoreDuration.OneMonth => nowUtc.AddMonths(1),
                NeedsAttentionIgnoreDuration.TwoMonths => nowUtc.AddMonths(2),
                NeedsAttentionIgnoreDuration.ThreeMonths => nowUtc.AddMonths(3),
                NeedsAttentionIgnoreDuration.SixMonths => nowUtc.AddMonths(6),
                NeedsAttentionIgnoreDuration.OneYear => nowUtc.AddYears(1),
                NeedsAttentionIgnoreDuration.TwoYears => nowUtc.AddYears(2),
                NeedsAttentionIgnoreDuration.Forever => DateTimeOffset.MaxValue,
                _ => nowUtc.AddMonths(3)
            };

        public static string GetLocalizationKey(NeedsAttentionIgnoreDuration duration) =>
            "NeedsAttention_IgnoreDuration_" + duration;

        private static DateTimeOffset GetNextLocalMidnight(DateTimeOffset nowUtc)
        {
            try
            {
                DateTimeOffset localNow = TimeZoneInfo.ConvertTime(nowUtc, TimeZoneInfo.Local);
                DateTime nextLocalMidnight = localNow.Date.AddDays(1);
                return new DateTimeOffset(nextLocalMidnight, TimeZoneInfo.Local.GetUtcOffset(nextLocalMidnight));
            }
            catch (ArgumentException)
            {
                // A system with no usable local time zone still gets a sane "tomorrow" window.
                return nowUtc.AddDays(1);
            }
        }
    }
}