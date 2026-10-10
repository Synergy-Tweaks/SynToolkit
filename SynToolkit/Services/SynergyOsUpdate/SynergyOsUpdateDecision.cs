#nullable enable

using System;

namespace SynToolkit.Services.SynergyOsUpdate
{
    public enum SynergyOsUpdateNotifyReason
    {
        Notify,
        InstalledUnknown,
        LatestUnknown,
        NotNewer,
        DraftOrPrerelease,
        SkippedVersion,
        Snoozed,
        Disabled
    }

    public static class SynergyOsUpdateDecision
    {
        public static SynergyOsUpdateNotifyReason ShouldNotify(
            string? installedVersion,
            SynergyOsCachedRelease? latest,
            SynergyOsUpdateSettingsDocument settings,
            DateTimeOffset utcNow,
            bool updatesEnabled)
        {
            if (!updatesEnabled)
            {
                return SynergyOsUpdateNotifyReason.Disabled;
            }

            if (string.IsNullOrWhiteSpace(installedVersion) ||
                !SynergyOsSemVer.TryParse(installedVersion, out _))
            {
                return SynergyOsUpdateNotifyReason.InstalledUnknown;
            }

            if (latest is null ||
                string.IsNullOrWhiteSpace(latest.Version) ||
                !SynergyOsSemVer.TryParse(latest.Version, out _))
            {
                return SynergyOsUpdateNotifyReason.LatestUnknown;
            }

            if (!SynergyOsSemVer.IsNewer(latest.Version, installedVersion))
            {
                return SynergyOsUpdateNotifyReason.NotNewer;
            }

            if (!string.IsNullOrWhiteSpace(settings.SkippedVersion) &&
                SynergyOsSemVer.CompareVersions(latest.Version, settings.SkippedVersion) == 0)
            {
                return SynergyOsUpdateNotifyReason.SkippedVersion;
            }

            if (!string.IsNullOrWhiteSpace(settings.SnoozedVersion) &&
                SynergyOsSemVer.CompareVersions(latest.Version, settings.SnoozedVersion) == 0 &&
                settings.SnoozeUntilUtc is DateTimeOffset snoozeUntil &&
                utcNow < snoozeUntil)
            {
                return SynergyOsUpdateNotifyReason.Snoozed;
            }

            return SynergyOsUpdateNotifyReason.Notify;
        }

        public static bool IsWithinCheckInterval(
            DateTimeOffset? lastSuccessfulCheckUtc,
            DateTimeOffset utcNow,
            int intervalHours = SynergyOsUpdateConstants.CheckIntervalHours)
        {
            if (lastSuccessfulCheckUtc is null)
            {
                return false;
            }

            return utcNow - lastSuccessfulCheckUtc.Value < TimeSpan.FromHours(intervalHours);
        }

        public static bool IsRateLimited(
            DateTimeOffset? rateLimitRetryAfterUtc,
            DateTimeOffset utcNow) =>
            rateLimitRetryAfterUtc is DateTimeOffset retryAfter && utcNow < retryAfter;
    }
}
