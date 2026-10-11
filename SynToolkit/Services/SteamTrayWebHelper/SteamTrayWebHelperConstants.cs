#nullable enable

namespace SynToolkit.Services.SteamTrayWebHelper
{
    /// <summary>
    /// Pinned SteamTrayWebHelper release asset locations and integrity hash.
    /// Download-at-enable (not bundled) to avoid GPL-3.0 redistribution of the binary.
    /// </summary>
    public static class SteamTrayWebHelperConstants
    {
        public const string ProjectUrl = "https://github.com/NobbyBollocks/SteamTrayWebHelper";
        public const string UpstreamProjectUrl = "https://github.com/NobbyBo11ocks/SteamTrayWebHelper";
        public const string OriginalAuthorCredit = "Aetopia / NoSteamWebHelper";

        public const string GithubOwner = "NobbyBollocks";
        public const string GithubRepo = "SteamTrayWebHelper";
        public const string ReleaseTag = "1.0";
        public const string DllFileName = "umpdc.dll";

        public const string DllDownloadUrl =
            "https://github.com/" + GithubOwner + "/" + GithubRepo
            + "/releases/download/" + ReleaseTag + "/" + DllFileName;

        public const string Sha256DownloadUrl = DllDownloadUrl + ".sha256";

        /// <summary>
        /// SHA-256 of the pinned umpdc.dll release asset (uppercase hex, no separators).
        /// </summary>
        public const string ExpectedSha256 =
            "2F36E50075BB21FE58B0BAF728E610CE59C6CD57914C0B365E2E1DFAE888B8C2";

        public const string FallbackSteamDirectory = @"C:\Program Files (x86)\Steam";
        public const string SteamExecutableName = "steam.exe";

        public static readonly string[] SteamProcessNames =
        {
            "steam",
            "steamwebhelper",
            "steamservice",
        };

        public const string UserAgent = "SynToolkit/1.0";
        public const int DownloadTimeoutSeconds = 60;
        public const int SteamShutdownWaitSeconds = 12;
    }
}
