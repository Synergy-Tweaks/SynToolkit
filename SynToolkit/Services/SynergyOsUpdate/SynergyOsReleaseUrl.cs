#nullable enable

using System;

namespace SynToolkit.Services.SynergyOsUpdate
{
    public static class SynergyOsReleaseUrl
    {
        public static string ResolveSafeOpenUrl(string? htmlUrl)
        {
            if (TryValidateReleaseUrl(htmlUrl, out string? validated) && validated is not null)
            {
                return validated;
            }

            return SynergyOsUpdateConstants.ReleasesLatestPageUrl;
        }

        public static bool TryValidateReleaseUrl(string? htmlUrl, out string? validated)
        {
            validated = null;
            if (!Uri.TryCreate(htmlUrl, UriKind.Absolute, out Uri? uri))
            {
                return false;
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string path = uri.AbsolutePath;
            if (!path.StartsWith(
                    SynergyOsUpdateConstants.ReleasesPathPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            validated = uri.GetLeftPart(UriPartial.Path);
            if (!string.IsNullOrEmpty(uri.Query))
            {
                // Drop query/fragment — release pages do not need them.
            }

            return true;
        }
    }
}
