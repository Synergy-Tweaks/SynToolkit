#nullable enable

using Microsoft.Win32;
using System;
using System.Globalization;

namespace SynToolkit.Services
{
    /// <summary>
    /// Registry-based Windows version detection. Prefer this over ProductName
    /// (which can still say "Windows 10" on some Windows 11 builds).
    /// </summary>
    public static class WindowsVersionService
    {
        public const int MinimumSupportedBuild = 19041;
        public const int Windows10MinimumBuild = 10240;
        public const int Windows11MinimumBuild = 22000;

        private const string CurrentVersionPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

        private static readonly object Gate = new();
        private static WindowsVersionInfo? _cached;

        public static WindowsVersionInfo Current
        {
            get
            {
                lock (Gate)
                {
                    return _cached ??= Read();
                }
            }
        }

        public static bool IsWindows10 => Current.IsWindows10;
        public static bool IsWindows11 => Current.IsWindows11;
        public static int Build => Current.Build;
        public static int Ubr => Current.Ubr;
        public static string DisplayVersion => Current.DisplayVersion;
        public static bool IsSupported => Current.IsSupported;

        /// <summary>Clears the cache so the next read hits the registry again.</summary>
        public static void ResetCache()
        {
            lock (Gate)
            {
                _cached = null;
            }
        }

        public static bool ClassifyIsWindows11(int build) => build >= Windows11MinimumBuild;

        public static bool ClassifyIsWindows10(int build) =>
            build >= Windows10MinimumBuild && build < Windows11MinimumBuild;

        public static bool ClassifyIsSupported(int build, bool readSucceeded) =>
            readSucceeded && build >= MinimumSupportedBuild;

        public static string FormatDetected(WindowsVersionInfo info)
        {
            string family = info.IsWindows11
                ? "Windows 11"
                : info.IsWindows10
                    ? "Windows 10"
                    : "Windows";

            string display = string.IsNullOrWhiteSpace(info.DisplayVersion)
                ? string.Empty
                : $" {info.DisplayVersion}";

            string buildText = info.Ubr > 0
                ? $"{info.Build.ToString(CultureInfo.InvariantCulture)}.{info.Ubr.ToString(CultureInfo.InvariantCulture)}"
                : info.Build.ToString(CultureInfo.InvariantCulture);

            return $"Detected: {family}{display} (build {buildText})";
        }

        private static WindowsVersionInfo Read()
        {
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(
                    RegistryHive.LocalMachine,
                    RegistryView.Registry64);
                using RegistryKey? key = baseKey.OpenSubKey(CurrentVersionPath, writable: false);
                if (key is null)
                {
                    LogWarn("HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion is missing.");
                    return WindowsVersionInfo.Failed;
                }

                string? buildText = key.GetValue("CurrentBuildNumber") as string
                    ?? key.GetValue("CurrentBuild") as string;
                if (!int.TryParse(buildText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int build)
                    || build <= 0)
                {
                    LogWarn($"CurrentBuildNumber could not be parsed: '{buildText}'.");
                    return WindowsVersionInfo.Failed;
                }

                int ubr = 0;
                object? ubrValue = key.GetValue("UBR");
                if (ubrValue is int ubrInt && ubrInt >= 0)
                {
                    ubr = ubrInt;
                }
                else if (ubrValue is uint ubrUint)
                {
                    ubr = unchecked((int)ubrUint);
                }

                string displayVersion = (key.GetValue("DisplayVersion") as string)?.Trim()
                    ?? (key.GetValue("ReleaseId") as string)?.Trim()
                    ?? string.Empty;

                // ProductName is read for diagnostics only; never used for Win10/11 classification.
                _ = key.GetValue("ProductName") as string;

                return new WindowsVersionInfo(build, ubr, displayVersion, true);
            }
            catch (Exception exception)
            {
                LogWarn(exception, "Failed to read Windows version from the registry.");
                return WindowsVersionInfo.Failed;
            }
        }

        private static void LogWarn(string message)
        {
            try
            {
                App.logger.Warn("[WindowsVersion] {0}", message);
            }
            catch
            {
                // Logging must never crash version detection.
            }
        }

        private static void LogWarn(Exception exception, string message)
        {
            try
            {
                App.logger.Warn(exception, "[WindowsVersion] {0}", message);
            }
            catch
            {
                // Logging must never crash version detection.
            }
        }
    }

    public sealed record WindowsVersionInfo(
        int Build,
        int Ubr,
        string DisplayVersion,
        bool ReadSucceeded)
    {
        public static WindowsVersionInfo Failed { get; } =
            new(0, 0, string.Empty, false);

        public bool IsWindows11 => WindowsVersionService.ClassifyIsWindows11(Build);

        public bool IsWindows10 => WindowsVersionService.ClassifyIsWindows10(Build);

        public bool IsSupported =>
            WindowsVersionService.ClassifyIsSupported(Build, ReadSucceeded);
    }
}
