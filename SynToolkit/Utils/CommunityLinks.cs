#nullable enable

using System;
using System.Threading.Tasks;
using Windows.System;

namespace SynToolkit.Utils
{
    /// <summary>
    /// Preferred repository for generic GitHub actions (clone / Settings bug report).
    /// Home repo tiles always open their own fixed URLs and ignore this preference.
    /// </summary>
    public enum PreferredGitHubRepository
    {
        SynToolkit = 0,
        SynergyOS = 1
    }

    /// <summary>
    /// Canonical community / repository URLs used across SynToolkit.
    /// </summary>
    public static class CommunityLinks
    {
        public const string SynergyOsRepoUrl = "https://github.com/Synergy-Tweaks/SynergyOS";
        public const string SynToolkitRepoUrl = "https://github.com/Synergy-Tweaks/SynToolkit";

        public const string SynergyOsBugTemplateUrl =
            "https://github.com/Synergy-Tweaks/SynergyOS/issues/new?template=bug_report.md";

        public const string SynToolkitBugTemplateUrl =
            "https://github.com/Synergy-Tweaks/SynToolkit/issues/new?template=syntoolkit-issues.md";

        public const string SynergyOsReleasesUrl = "https://github.com/synergy-tweaks/synergyos/releases";
        public const string SynToolkitReleasesUrl = "https://github.com/synergy-tweaks/syntoolkit/releases";

        /// <summary>
        /// Obsolete name kept so older call sites compile; always the releases list page.
        /// </summary>
        public const string SynergyOsReleasesLatestUrl = SynergyOsReleasesUrl;

        public const string DiscordInviteUrl = "https://dsc.gg/kwanteks";
        public const string KwanteksYouTubeUrl = "https://www.youtube.com/@Kwanteks";

        public static string GetRepositoryUrl(PreferredGitHubRepository repository) =>
            repository == PreferredGitHubRepository.SynergyOS ? SynergyOsRepoUrl : SynToolkitRepoUrl;

        public static string GetCloneCommand(PreferredGitHubRepository repository) =>
            "git clone " + GetRepositoryUrl(repository);

        public static string GetNewIssueUrl(PreferredGitHubRepository repository) =>
            repository == PreferredGitHubRepository.SynergyOS
                ? SynergyOsBugTemplateUrl
                : SynToolkitBugTemplateUrl;

        public static async Task LaunchUriAsync(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            {
                App.logger.Warn("Refusing to launch an invalid community link: {Url}", url);
                return;
            }

            try
            {
                await Launcher.LaunchUriAsync(uri);
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "Community link could not be opened: {Url}", url);
            }
        }

        public static Task LaunchPreferredRepositoryAsync() =>
            LaunchUriAsync(GetRepositoryUrl(PreferredGitHubRepositorySettings.Get()));

        public static Task LaunchPreferredNewIssueAsync() =>
            LaunchUriAsync(GetNewIssueUrl(PreferredGitHubRepositorySettings.Get()));
    }

    /// <summary>
    /// Persists the Settings "default GitHub repository" preference.
    /// Default is SynToolkit even though older builds linked SynergyOS.
    /// </summary>
    public static class PreferredGitHubRepositorySettings
    {
        private const string RegistryPath = @"HKLM\SOFTWARE\SynToolkit";
        private const string ValueName = "PreferredGitHubRepository";
        private const string SynergyOsKey = "SynergyOS";
        private const string SynToolkitKey = "SynToolkit";

        public static PreferredGitHubRepository Get()
        {
            try
            {
                object? value = RegistryHelper.GetValue(RegistryPath, ValueName);
                if (value is string text &&
                    text.Trim().Equals(SynergyOsKey, StringComparison.OrdinalIgnoreCase))
                {
                    return PreferredGitHubRepository.SynergyOS;
                }
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "Unable to read PreferredGitHubRepository; using SynToolkit default.");
            }

            return PreferredGitHubRepository.SynToolkit;
        }

        public static void Set(PreferredGitHubRepository repository)
        {
            string value = repository == PreferredGitHubRepository.SynergyOS ? SynergyOsKey : SynToolkitKey;
            RegistryHelper.SetValue(RegistryPath, ValueName, value, Microsoft.Win32.RegistryValueKind.String);
        }

        public static PreferredGitHubRepository Parse(string? key)
        {
            if (!string.IsNullOrWhiteSpace(key) &&
                key.Trim().Equals(SynergyOsKey, StringComparison.OrdinalIgnoreCase))
            {
                return PreferredGitHubRepository.SynergyOS;
            }

            return PreferredGitHubRepository.SynToolkit;
        }

        public static string ToStorageKey(PreferredGitHubRepository repository) =>
            repository == PreferredGitHubRepository.SynergyOS ? SynergyOsKey : SynToolkitKey;
    }
}
