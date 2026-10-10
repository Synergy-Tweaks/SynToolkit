#nullable enable

using System;

namespace SynToolkit.Services.SynergyOsUpdate
{
    /// <summary>
    /// Pure update availability — never considers dismissed, snoozed, skipped, or session state.
    /// </summary>
    public enum SynergyOsUpdateAvailability
    {
        UpdateAvailable,
        UpToDate,
        Unknown,
        CheckFailed
    }

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
        /// <summary>
        /// Compares installed vs latest release only. Does not read dismiss/snooze/skip/session.
        /// </summary>
        public static SynergyOsUpdateAvailability GetUpdateStatus(
            string? installedVersion,
            SynergyOsCachedRelease? latestRelease)
        {
            if (string.IsNullOrWhiteSpace(installedVersion) ||
                !SynergyOsSemVer.TryParse(installedVersion, out _))
            {
                return SynergyOsUpdateAvailability.Unknown;
            }

            if (latestRelease is null ||
                string.IsNullOrWhiteSpace(latestRelease.Version) ||
                !SynergyOsSemVer.TryParse(latestRelease.Version, out _))
            {
                return SynergyOsUpdateAvailability.Unknown;
            }

            return SynergyOsSemVer.IsNewer(latestRelease.Version, installedVersion)
                ? SynergyOsUpdateAvailability.UpdateAvailable
                : SynergyOsUpdateAvailability.UpToDate;
        }

        /// <summary>
        /// Whether the in-app notice should appear. Applies dismiss/snooze/skip/session after
        /// <see cref="GetUpdateStatus"/>.
        /// </summary>
        public static bool ShouldShowNotice(
            string? installedVersion,
            SynergyOsCachedRelease? latest,
            SynergyOsUpdateSettingsDocument settings,
            DateTimeOffset utcNow,
            bool updatesEnabled,
            bool notifiedThisSession)
        {
            if (!updatesEnabled || notifiedThisSession)
            {
                return false;
            }

            if (GetUpdateStatus(installedVersion, latest) != SynergyOsUpdateAvailability.UpdateAvailable)
            {
                return false;
            }

            SynergyOsUpdateNotifyReason reason = ShouldNotify(
                installedVersion,
                latest,
                settings,
                utcNow,
                updatesEnabled);
            return reason == SynergyOsUpdateNotifyReason.Notify;
        }

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

            SynergyOsUpdateAvailability availability = GetUpdateStatus(installedVersion, latest);
            if (availability == SynergyOsUpdateAvailability.Unknown)
            {
                if (string.IsNullOrWhiteSpace(installedVersion) ||
                    !SynergyOsSemVer.TryParse(installedVersion, out _))
                {
                    return SynergyOsUpdateNotifyReason.InstalledUnknown;
                }

                return SynergyOsUpdateNotifyReason.LatestUnknown;
            }

            if (availability == SynergyOsUpdateAvailability.UpToDate)
            {
                return SynergyOsUpdateNotifyReason.NotNewer;
            }

            // availability == UpdateAvailable — apply skip / snooze only here.
            if (!string.IsNullOrWhiteSpace(settings.SkippedVersion) &&
                SynergyOsSemVer.CompareVersions(latest!.Version, settings.SkippedVersion) == 0)
            {
                return SynergyOsUpdateNotifyReason.SkippedVersion;
            }

            if (!string.IsNullOrWhiteSpace(settings.SnoozedVersion) &&
                SynergyOsSemVer.CompareVersions(latest!.Version, settings.SnoozedVersion) == 0 &&
                settings.SnoozeUntilUtc is DateTimeOffset snoozeUntil &&
                utcNow < snoozeUntil)
            {
                return SynergyOsUpdateNotifyReason.Snoozed;
            }

            return SynergyOsUpdateNotifyReason.Notify;
        }

        public static bool IsVersionSkipped(
            SynergyOsCachedRelease? latest,
            SynergyOsUpdateSettingsDocument settings) =>
            latest is not null &&
            !string.IsNullOrWhiteSpace(settings.SkippedVersion) &&
            SynergyOsSemVer.CompareVersions(latest.Version, settings.SkippedVersion) == 0;

        /// <summary>
        /// Formats Settings-row status text. "You're up to date" only when status is UpToDate.
        /// </summary>
        public static string FormatSettingsStatusText(
            SynergyOsUpdateAvailability status,
            string? installedVersion,
            SynergyOsCachedRelease? latest,
            bool isSkipped,
            Func<string, string> getString)
        {
            return status switch
            {
                SynergyOsUpdateAvailability.UpdateAvailable when latest is not null =>
                    FormatAvailable(installedVersion, latest.Version, isSkipped, getString),
                SynergyOsUpdateAvailability.UpToDate =>
                    getString("SynergyOsUpdate_UpToDate"),
                SynergyOsUpdateAvailability.CheckFailed =>
                    getString("SynergyOsUpdate_CouldNotCheck"),
                _ => getString("SynergyOsUpdate_VersionUnknown"),
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
                getString("SynergyOsUpdate_AvailableStatus"),
                latestVersion,
                installed);
            if (isSkipped)
            {
                text += " " + getString("SynergyOsUpdate_SkippedMarker");
            }

            return text;
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
