#nullable enable

namespace SynToolkit.Services.SynToolkitUpdate
{
    /// <summary>
    /// Single source of truth for SynToolkit GitHub release checks.
    /// </summary>
    public static class SynToolkitUpdateConstants
    {
        public const string GithubOwner = "Synergy-Tweaks";
        public const string GithubRepo = "SynToolkit";

        public const string ReleasesApiUrl =
            "https://api.github.com/repos/" + GithubOwner + "/" + GithubRepo + "/releases/latest";

        /// <summary>
        /// Fixed releases list page. Never a /tag/ or /latest redirect URL.
        /// </summary>
        public const string ReleasesPageUrl =
            "https://github.com/synergy-tweaks/syntoolkit/releases";

        public const string ReleasesPathPrefix =
            "/synergy-tweaks/syntoolkit/releases";

        public const int CheckIntervalHours = 24;
        public const int RequestTimeoutSeconds = 10;
        public const int SnoozeDays = 3;

        public const string UserAgent = "Syntoolkit";
        public const string AcceptHeader = "application/vnd.github+json";
        public const string ApiVersionHeader = "2022-11-28";
    }
}
