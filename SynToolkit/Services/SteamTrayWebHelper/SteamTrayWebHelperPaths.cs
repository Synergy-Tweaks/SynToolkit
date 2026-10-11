#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SynToolkit.Services.SteamTrayWebHelper
{
    /// <summary>
    /// Pure helpers for Steam install discovery, DLL presence, and SHA-256 text parsing.
    /// Inject registry/file existence via callbacks so tests stay free of live registry I/O.
    /// </summary>
    public static class SteamTrayWebHelperPaths
    {
        private static readonly Regex HexHashRegex = new(
            @"\b([0-9a-fA-F]{64})\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public readonly record struct SteamPathResult(string Directory, bool SteamExeExists);

        /// <summary>
        /// Resolves Steam's install folder in order:
        /// HKCU SteamPath → HKLM WOW6432Node InstallPath → Program Files (x86)\Steam.
        /// </summary>
        public static SteamPathResult ResolveSteamDirectory(
            Func<string?> readHkcuSteamPath,
            Func<string?> readHklmInstallPath,
            Func<string, bool> directoryExists,
            Func<string, bool> fileExists)
        {
            ArgumentNullException.ThrowIfNull(readHkcuSteamPath);
            ArgumentNullException.ThrowIfNull(readHklmInstallPath);
            ArgumentNullException.ThrowIfNull(directoryExists);
            ArgumentNullException.ThrowIfNull(fileExists);

            string?[] candidates =
            {
                NormalizeSteamPath(readHkcuSteamPath()),
                NormalizeSteamPath(readHklmInstallPath()),
                SteamTrayWebHelperConstants.FallbackSteamDirectory,
            };

            foreach (string? candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate) || !directoryExists(candidate))
                {
                    continue;
                }

                string exePath = Path.Combine(candidate, SteamTrayWebHelperConstants.SteamExecutableName);
                if (fileExists(exePath))
                {
                    return new SteamPathResult(Path.GetFullPath(candidate), SteamExeExists: true);
                }
            }

            string? firstExisting = candidates.FirstOrDefault(path =>
                !string.IsNullOrWhiteSpace(path) && directoryExists(path!));
            if (!string.IsNullOrWhiteSpace(firstExisting))
            {
                return new SteamPathResult(Path.GetFullPath(firstExisting), SteamExeExists: false);
            }

            return new SteamPathResult(SteamTrayWebHelperConstants.FallbackSteamDirectory, SteamExeExists: false);
        }

        public static string GetDllPath(string steamDirectory) =>
            Path.Combine(steamDirectory, SteamTrayWebHelperConstants.DllFileName);

        public static bool IsDllInstalled(string steamDirectory, Func<string, bool> fileExists)
        {
            ArgumentNullException.ThrowIfNull(fileExists);
            if (string.IsNullOrWhiteSpace(steamDirectory))
            {
                return false;
            }

            return fileExists(GetDllPath(steamDirectory));
        }

        /// <summary>
        /// Extracts a 64-char hex digest from a published .sha256 file body
        /// (supports "hash", "hash  filename", and "SHA256 (file) = hash").
        /// </summary>
        public static string? TryParseSha256FileContents(string? contents)
        {
            if (string.IsNullOrWhiteSpace(contents))
            {
                return null;
            }

            Match match = HexHashRegex.Match(contents);
            return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
        }

        public static bool HashesMatch(string expectedHex, string actualHex) =>
            !string.IsNullOrWhiteSpace(expectedHex)
            && !string.IsNullOrWhiteSpace(actualHex)
            && string.Equals(expectedHex.Trim(), actualHex.Trim(), StringComparison.OrdinalIgnoreCase);

        public static string NormalizeSteamPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string normalized = path.Trim().Trim('"').Replace('/', '\\');
            while (normalized.EndsWith("\\", StringComparison.Ordinal)
                   && normalized.Length > 3)
            {
                normalized = normalized[..^1];
            }

            return normalized;
        }
    }
}
