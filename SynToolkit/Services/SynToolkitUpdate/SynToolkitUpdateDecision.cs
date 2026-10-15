#nullable enable

using System;
using SynToolkit.Services.SynergyOsUpdate;

namespace SynToolkit.Services.SynToolkitUpdate
{
    public enum SynToolkitUpdateAvailability
    {
        UpdateAvailable,
        UpToDate,
        Unknown,
        CheckFailed
    }

    public enum SynToolkitUpdateNotifyReason
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

    public static class SynToolkitUpdateDecision
    {
        public static SynToolkitUpdateAvailability GetUpdateStatus(
            string? installedVersion,
            SynToolkitCachedRelease? latestRelease)
        {
            if (string.IsNullOrWhiteSpace(installedVersion) ||
                !SynergyOsSemVer.TryParse(installedVersion, out _))
            {
                return SynToolkitUpdateAvailability.Unknown;
            }

            if (latestRelease is null ||
                string.IsNullOrWhiteSpace(latestRelease.Version) ||
                !SynergyOsSemVer.TryParse(latestRelease.Version, out _))
            {
                return SynToolkitUpdateAvailability.Unknown;
            }

            return SynergyOsSemVer.IsNewer(latestRelease.Version, installedVersion)
                ? SynToolkitUpdateAvailability.UpdateAvailable
                : SynToolkitUpdateAvailability.UpToDate;
        }

        public static bool ShouldShowNotice(
            string? installedVersion,
            SynToolkitCachedRelease? latest,
            SynToolkitUpdateSettingsDocument settings,
            DateTimeOffset utcNow,
            bool updatesEnabled,
            bool notifiedThisSession)
        {
            if (!updatesEnabled || notifiedThisSession)
            {
                return false;
            }

            if (GetUpdateStatus(installedVersion, latest) != SynToolkitUpdateAvailability.UpdateAvailable)
            {
                return false;
            }

            SynToolkitUpdateNotifyReason reason = ShouldNotify(
                installedVersion,
                latest,
                settings,
                utcNow,
                updatesEnabled);
            return reason == SynToolkitUpdateNotifyReason.Notify;
        }

        public static SynToolkitUpdateNotifyReason ShouldNotify(
            string? installedVersion,
            SynToolkitCachedRelease? latest,
            SynToolkitUpdateSettingsDocument settings,
            DateTimeOffset utcNow,
            bool updatesEnabled)
        {
            if (!updatesEnabled)
            {
                return SynToolkitUpdateNotifyReason.Disabled;
            }

            SynToolkitUpdateAvailability availability = GetUpdateStatus(installedVersion, latest);
            if (availability == SynToolkitUpdateAvailability.Unknown)
            {
                if (string.IsNullOrWhiteSpace(installedVersion) ||
                    !SynergyOsSemVer.TryParse(installedVersion, out _))
                {
                    return SynToolkitUpdateNotifyReason.InstalledUnknown;
                }

                return SynToolkitUpdateNotifyReason.LatestUnknown;
            }

            if (availability == SynToolkitUpdateAvailability.UpToDate)
            {
                return SynToolkitUpdateNotifyReason.NotNewer;
            }

            if (!string.IsNullOrWhiteSpace(settings.SkippedVersion) &&
                SynergyOsSemVer.CompareVersions(latest!.Version, settings.SkippedVersion) == 0)
            {
                return SynToolkitUpdateNotifyReason.SkippedVersion;
            }

            if (!string.IsNullOrWhiteSpace(settings.SnoozedVersion) &&
                SynergyOsSemVer.CompareVersions(latest!.Version, settings.SnoozedVersion) == 0 &&
                settings.SnoozeUntilUtc is DateTimeOffset snoozeUntil &&
                utcNow < snoozeUntil)
            {
                return SynToolkitUpdateNotifyReason.Snoozed;
            }

            return SynToolkitUpdateNotifyReason.Notify;
        }

        public static bool IsVersionSkipped(
            SynToolkitCachedRelease? latest,
            SynToolkitUpdateSettingsDocument settings) =>
            latest is not null &&
            !string.IsNullOrWhiteSpace(settings.SkippedVersion) &&
            SynergyOsSemVer.CompareVersions(latest.Version, settings.SkippedVersion) == 0;

        public static string FormatSettingsStatusText(
            SynToolkitUpdateAvailability status,
            string? installedVersion,
            SynToolkitCachedRelease? latest,
            bool isSkipped,
            Func<string, string> getString)
        {
            return status switch
            {
                SynToolkitUpdateAvailability.UpdateAvailable when latest is not null =>
                    FormatAvailable(installedVersion, latest.Version, isSkipped, getString),
                SynToolkitUpdateAvailability.UpToDate =>
                    getString("SynToolkitUpdate_UpToDate"),
                SynToolkitUpdateAvailability.CheckFailed =>
                    getString("SynToolkitUpdate_CouldNotCheck"),
                _ => getString("SynToolkitUpdate_CouldNotCheck"),
            };
        }

        private static string FormatAvailable(
            string? installedVersion,
            string latestVersion,
            bool isSkipped,
            Func<string, string> getString)
        {
            string installed = string.IsNullOrWhiteSpace(installedVersion) ? "?" : installedVersion.Trim();
            string text = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                getString("SynToolkitUpdate_AvailableStatus"),
                latestVersion,
                installed);
            if (isSkipped)
            {
                text += " " + getString("SynToolkitUpdate_SkippedMarker");
            }

            return text;
        }

        public static bool IsWithinCheckInterval(
            DateTimeOffset? lastSuccessfulCheckUtc,
            DateTimeOffset utcNow,
            int intervalHours = SynToolkitUpdateConstants.CheckIntervalHours)
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
