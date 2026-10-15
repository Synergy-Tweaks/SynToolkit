#nullable enable

namespace SynToolkit.Services.SynergyOsUpdate
{
    /// <summary>
    /// Single source of truth for SynergyOS GitHub release checks.
    /// </summary>
    public static class SynergyOsUpdateConstants
    {
        // API paths keep the historical Synergy-Tweaks casing; page opens use the
        // fixed releases list URL (GitHub paths are case-insensitive).
        public const string GithubOwner = "Synergy-Tweaks";
        public const string GithubRepo = "SynergyOS";

        public const string ReleasesApiUrl =
            "https://api.github.com/repos/" + GithubOwner + "/" + GithubRepo + "/releases/latest";

        /// <summary>
        /// Fixed releases list page. Never a /tag/ or /latest redirect URL.
        /// </summary>
        public const string ReleasesPageUrl =
            "https://github.com/synergy-tweaks/synergyos/releases";

        /// <summary>
        /// Kept for callers that still reference the old name; same as <see cref="ReleasesPageUrl"/>.
        /// </summary>
        public const string ReleasesLatestPageUrl = ReleasesPageUrl;

        public const string ReleasesPathPrefix =
            "/synergy-tweaks/synergyos/releases";

        public const int CheckIntervalHours = 24;
        public const int RequestTimeoutSeconds = 10;
        public const int SnoozeDays = 3;

        public const string UserAgent = "Syntoolkit";
        public const string AcceptHeader = "application/vnd.github+json";
        public const string ApiVersionHeader = "2022-11-28";
    }
}
