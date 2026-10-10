#nullable enable

using System;
using System.Globalization;

namespace SynToolkit.Services.SynergyOsUpdate
{
    /// <summary>
    /// Tolerant semver-style comparison for SynergyOS tags and installed playbook versions.
    /// </summary>
    public static class SynergyOsSemVer
    {
        public sealed record ParsedVersion(
            int[] Segments,
            string? PreRelease,
            string OriginalNormalized);

        /// <summary>
        /// Returns negative when <paramref name="left"/> is older, zero when equal,
        /// positive when <paramref name="left"/> is newer. Returns null when either side is unknown.
        /// </summary>
        public static int? CompareVersions(string? left, string? right)
        {
            if (!TryParse(left, out ParsedVersion? a) || !TryParse(right, out ParsedVersion? b) ||
                a is null || b is null)
            {
                return null;
            }

            int segmentCount = Math.Max(a.Segments.Length, b.Segments.Length);
            for (int index = 0; index < segmentCount; index++)
            {
                int leftSegment = index < a.Segments.Length ? a.Segments[index] : 0;
                int rightSegment = index < b.Segments.Length ? b.Segments[index] : 0;
                int comparison = leftSegment.CompareTo(rightSegment);
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            bool leftHasPre = !string.IsNullOrEmpty(a.PreRelease);
            bool rightHasPre = !string.IsNullOrEmpty(b.PreRelease);
            if (leftHasPre != rightHasPre)
            {
                // A release without a pre-release suffix is newer than one with a suffix.
                return leftHasPre ? -1 : 1;
            }

            if (!leftHasPre)
            {
                return 0;
            }

            return string.Compare(a.PreRelease, b.PreRelease, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsNewer(string? latest, string? installed)
        {
            int? comparison = CompareVersions(latest, installed);
            return comparison is > 0;
        }

        public static bool TryParse(string? raw, out ParsedVersion? parsed)
        {
            parsed = null;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            string normalized = raw.Trim();
            if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[1..].TrimStart();
            }

            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            string core = normalized;
            string? preRelease = null;
            int dashIndex = normalized.IndexOf('-');
            if (dashIndex >= 0)
            {
                core = normalized[..dashIndex];
                preRelease = normalized[(dashIndex + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(preRelease))
                {
                    return false;
                }
            }

            string[] parts = core.Split('.', StringSplitOptions.None);
            if (parts.Length is < 1 or > 4)
            {
                return false;
            }

            int[] segments = new int[parts.Length];
            for (int index = 0; index < parts.Length; index++)
            {
                if (!int.TryParse(
                        parts[index],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int value) ||
                    value < 0)
                {
                    return false;
                }

                segments[index] = value;
            }

            parsed = new ParsedVersion(segments, preRelease, normalized);
            return true;
        }

        public static string? NormalizeDisplay(string? raw)
        {
            return TryParse(raw, out ParsedVersion? parsed) && parsed is not null
                ? parsed.OriginalNormalized
                : null;
        }
    }
}
