#nullable enable

using System;
using System.Globalization;

namespace SynToolkit.Services.SynergyOsUpdate
{
    /// <summary>
    /// Presentation model for the SynergyOS update InfoBar. Contains only what the notice shows.
    /// </summary>
    public sealed record SynergyOsUpdateNoticeModel(
        string Title,
        string BodyLine,
        string? PublishedLine,
        string ViewReleaseLabel,
        string RemindLaterLabel,
        string SkipVersionLabel)
    {
        public bool HasPublishedLine => !string.IsNullOrWhiteSpace(PublishedLine);
    }

    public static class SynergyOsUpdateNotice
    {
        public static SynergyOsUpdateNoticeModel Create(
            string latestVersion,
            string installedVersion,
            DateTimeOffset? publishedAtUtc,
            Func<string, string> getString,
            CultureInfo? culture = null)
        {
            ArgumentNullException.ThrowIfNull(getString);
            culture ??= CultureInfo.CurrentCulture;

            string title = getString("SynergyOsUpdate_Title");
            string body = string.Format(
                culture,
                getString("SynergyOsUpdate_Body"),
                latestVersion,
                installedVersion);

            string? publishedLine = null;
            if (publishedAtUtc is DateTimeOffset published)
            {
                string shortDate = published.ToLocalTime().ToString("MMM d, yyyy", culture);
                publishedLine = string.Format(
                    culture,
                    getString("SynergyOsUpdate_Published"),
                    shortDate);
            }

            return new SynergyOsUpdateNoticeModel(
                title,
                body,
                publishedLine,
                getString("SynergyOsUpdate_ViewRelease"),
                getString("SynergyOsUpdate_RemindLater"),
                getString("SynergyOsUpdate_SkipVersion"));
        }
    }
}
