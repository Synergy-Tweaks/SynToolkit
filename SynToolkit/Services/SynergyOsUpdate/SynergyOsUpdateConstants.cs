#nullable enable

namespace SynToolkit.Services.SynergyOsUpdate
{
    /// <summary>
    /// Single source of truth for SynergyOS GitHub release checks.
    /// </summary>
    public static class SynergyOsUpdateConstants
    {
        // TODO: confirm owner/repo if the public releases home ever moves.
        public const string GithubOwner = "Synergy-Tweaks";
        public const string GithubRepo = "SynergyOS";

        public const string ReleasesApiUrl =
            "https://api.github.com/repos/" + GithubOwner + "/" + GithubRepo + "/releases/latest";

        public const string ReleasesLatestPageUrl =
            "https://github.com/" + GithubOwner + "/" + GithubRepo + "/releases/latest";

        public const string ReleasesPathPrefix =
            "/" + GithubOwner + "/" + GithubRepo + "/releases/";

        public const int CheckIntervalHours = 24;
        public const int RequestTimeoutSeconds = 10;
        public const int SnoozeDays = 3;

        public const string UserAgent = "Syntoolkit";
        public const string AcceptHeader = "application/vnd.github+json";
        public const string ApiVersionHeader = "2022-11-28";
    }
}
