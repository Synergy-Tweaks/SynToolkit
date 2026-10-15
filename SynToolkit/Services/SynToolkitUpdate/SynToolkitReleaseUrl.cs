#nullable enable

using System;

namespace SynToolkit.Services.SynToolkitUpdate
{
    public static class SynToolkitReleaseUrl
    {
        /// <summary>
        /// Always returns the fixed SynToolkit releases list URL.
        /// API/cached <c>html_url</c> values (including tag-pinned pages) are ignored.
        /// </summary>
        public static string ResolveSafeOpenUrl(string? htmlUrl = null)
        {
            _ = htmlUrl;
            string constant = SynToolkitUpdateConstants.ReleasesPageUrl;
            if (TryValidateReleaseUrl(constant, out string? validated) && validated is not null)
            {
                return validated;
            }

            return constant;
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

            string path = uri.AbsolutePath.TrimEnd('/');
            if (!path.Equals(
                    SynToolkitUpdateConstants.ReleasesPathPrefix,
                    StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWith(
                    SynToolkitUpdateConstants.ReleasesPathPrefix + "/",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            validated = SynToolkitUpdateConstants.ReleasesPageUrl;
            return true;
        }
    }
}
