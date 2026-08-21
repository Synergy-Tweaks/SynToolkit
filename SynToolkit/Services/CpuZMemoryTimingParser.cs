#nullable enable

using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SynToolkit.Services
{
    internal sealed record CpuZMemoryTimings(string? MemoryType, string? TimingText);

    internal static class CpuZMemoryTimingParser
    {
        private static readonly Regex FirstNumberPattern = new(@"\d+(?:[\.,]\d+)?", RegexOptions.Compiled);

        internal static CpuZMemoryTimings? TryParse(string report)
        {
            if (string.IsNullOrWhiteSpace(report))
            {
                return null;
            }

            string? memoryType = ReadValue(report, "Memory Type");
            string? casLatency = ReadClockValue(report, "CAS# latency (CL)");
            string? rasToCasDelay = ReadClockValue(report, "RAS# to CAS# delay (tRCD)");
            string? rasPrecharge = ReadClockValue(report, "RAS# Precharge (tRP)");
            string? cycleTime = ReadClockValue(report, "Cycle Time (tRAS)");
            string? timingText = casLatency is null
                ? null
                : rasToCasDelay is not null && rasPrecharge is not null && cycleTime is not null
                    ? $"CL{casLatency} {casLatency}-{rasToCasDelay}-{rasPrecharge}-{cycleTime}"
                    : $"CL{casLatency}";

            return string.IsNullOrWhiteSpace(memoryType) && timingText is null
                ? null
                : new CpuZMemoryTimings(memoryType, timingText);
        }

        private static string? ReadValue(string report, string label)
        {
            foreach (string line in report.Split(["\r\n", "\n"], StringSplitOptions.None))
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                {
                    string value = trimmed[label.Length..].Trim();
                    return string.IsNullOrWhiteSpace(value) ? null : value;
                }
            }

            return null;
        }

        private static string? ReadClockValue(string report, string label)
        {
            string? rawValue = ReadValue(report, label);
            Match match = rawValue is null ? Match.Empty : FirstNumberPattern.Match(rawValue);
            if (!match.Success ||
                !decimal.TryParse(
                    match.Value.Replace(',', '.'),
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out decimal value))
            {
                return null;
            }

            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}