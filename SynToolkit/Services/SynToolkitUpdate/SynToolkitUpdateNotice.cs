#nullable enable

using System;
using System.Globalization;

namespace SynToolkit.Services.SynToolkitUpdate
{
    public sealed record SynToolkitUpdateNoticeModel(
        string Title,
        string BodyLine,
        string? PublishedLine,
        string ViewReleaseLabel,
        string RemindLaterLabel,
        string SkipVersionLabel)
    {
        public bool HasPublishedLine => !string.IsNullOrWhiteSpace(PublishedLine);
    }

    public static class SynToolkitUpdateNotice
    {
        public static SynToolkitUpdateNoticeModel Create(
            string latestVersion,
            string installedVersion,
            DateTimeOffset? publishedAtUtc,
            Func<string, string> getString,
            CultureInfo? culture = null)
        {
            ArgumentNullException.ThrowIfNull(getString);
            culture ??= CultureInfo.CurrentCulture;

            string title = getString("SynToolkitUpdate_Title");
            string body = string.Format(
                culture,
                getString("SynToolkitUpdate_Body"),
                latestVersion,
                installedVersion);

            string? publishedLine = null;
            if (publishedAtUtc is DateTimeOffset published)
            {
                string shortDate = published.ToLocalTime().ToString("MMM d, yyyy", culture);
                publishedLine = string.Format(
                    culture,
                    getString("SynToolkitUpdate_Published"),
                    shortDate);
            }

            return new SynToolkitUpdateNoticeModel(
                title,
                body,
                publishedLine,
                getString("SynToolkitUpdate_ViewRelease"),
                getString("SynToolkitUpdate_RemindLater"),
                getString("SynToolkitUpdate_SkipVersion"));
        }
    }
}
