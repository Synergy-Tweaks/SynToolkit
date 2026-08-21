#nullable enable

using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SynToolkit.Services
{
    internal sealed record CpuZCoreSet(string Name, int Cores, int Threads, decimal? MaximumFrequencyMHz);

    internal sealed record CpuZProcessorDetails(
        string? Manufacturer,
        string? Codename,
        string? Socket,
        string? Technology,
        string? Cpuid,
        string? Stepping,
        string? InstructionSets,
        string? ThermalDesignPower,
        string? TemperatureLimit,
        decimal? MinimumFrequencyMHz,
        decimal? MaximumFrequencyMHz,
        string? L1DataCache,
        string? L1InstructionCache,
        string? L2Cache,
        string? L3Cache,
        CpuZCoreSet? PerformanceCores,
        CpuZCoreSet? EfficientCores,
        bool HasAmd3dVCache);

    internal static class CpuZProcessorDetailsParser
    {
        private static readonly Regex FirstNumberPattern = new(@"\d+(?:[\.,]\d+)?", RegexOptions.Compiled);
        private static readonly Regex CoreSetPattern = new(
            @"^\s*Core Set \d+\s+(?<name>[PE]-Cores),\s*(?<cores>\d+) cores,\s*(?<threads>\d+) threads",
            RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);
        private static readonly Regex CoreRatioPattern = new(
            @"^\s*Ratio \d+\s+(?<name>[PE])-Core(?:s)?\s+(?<ratio>\d+(?:[\.,]\d+)?)x",
            RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);
        private static readonly Regex CacheDetailPattern = new(@"\s*\([^)]*\)", RegexOptions.Compiled);
        private static readonly Regex CacheMultiplierPattern = new(@"\s+x\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RepeatedWhitespacePattern = new(@"\s+", RegexOptions.Compiled);

        internal static CpuZProcessorDetails? TryParse(string report)
        {
            if (string.IsNullOrWhiteSpace(report))
            {
                return null;
            }

            string processorSection = ExtractProcessorSection(report);
            string? manufacturer = ReadValue(processorSection, "Manufacturer");
            string? name = ReadValue(processorSection, "Name");
            string? specification = ReadValue(processorSection, "Specification");
            decimal? baseFrequencyMHz = ReadNumber(processorSection, "Base frequency (cores)");
            decimal? minimumFrequencyMHz = Multiply(baseFrequencyMHz, ReadNumber(processorSection, "Min operating ratio"));
            decimal? maximumFrequencyMHz = Multiply(
                baseFrequencyMHz,
                ReadNumber(processorSection, "Max turbo ratio") ?? ReadNumber(processorSection, "Max non-turbo ratio"));
            CpuZCoreSet? performanceCores = ReadCoreSet(processorSection, "P", baseFrequencyMHz);
            CpuZCoreSet? efficientCores = ReadCoreSet(processorSection, "E", baseFrequencyMHz);
            bool isAmd = (manufacturer?.Contains("AMD", StringComparison.OrdinalIgnoreCase) ?? false)
                || (name?.Contains("AMD", StringComparison.OrdinalIgnoreCase) ?? false)
                || (specification?.Contains("AMD", StringComparison.OrdinalIgnoreCase) ?? false);
            bool hasAmd3dVCache = isAmd && (
                name?.Contains("X3D", StringComparison.OrdinalIgnoreCase) == true
                || specification?.Contains("X3D", StringComparison.OrdinalIgnoreCase) == true
                || processorSection.Contains("3D V-Cache", StringComparison.OrdinalIgnoreCase));

            bool hasDetails = manufacturer is not null
                || name is not null
                || maximumFrequencyMHz.HasValue
                || performanceCores is not null
                || efficientCores is not null
                || ReadValue(processorSection, "L3 cache") is not null;
            if (!hasDetails)
            {
                return null;
            }

            return new CpuZProcessorDetails(
                manufacturer,
                ReadValue(processorSection, "Codename"),
                ReadValue(processorSection, "Package (platform ID)"),
                ReadValue(processorSection, "Technology"),
                ReadValue(processorSection, "CPUID"),
                ReadValue(processorSection, "Core Stepping"),
                ReadValue(processorSection, "Instructions sets"),
                ReadValue(processorSection, "TDP Limit"),
                FormatTemperature(ReadValue(processorSection, "Tjmax")),
                minimumFrequencyMHz,
                maximumFrequencyMHz,
                SimplifyCache(ReadValue(processorSection, "L1 Data cache")),
                SimplifyCache(ReadValue(processorSection, "L1 Instruction cache")),
                SimplifyCache(ReadValue(processorSection, "L2 cache")),
                SimplifyCache(ReadValue(processorSection, "L3 cache")),
                performanceCores,
                efficientCores,
                hasAmd3dVCache);
        }

        private static CpuZCoreSet? ReadCoreSet(string processorSection, string corePrefix, decimal? baseFrequencyMHz)
        {
            Match coreSetMatch = CoreSetPattern.Matches(processorSection)
                .Cast<Match>()
                .FirstOrDefault(match => match.Groups["name"].Value.StartsWith(corePrefix, StringComparison.OrdinalIgnoreCase))
                ?? Match.Empty;
            if (!coreSetMatch.Success
                || !int.TryParse(coreSetMatch.Groups["cores"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int cores)
                || !int.TryParse(coreSetMatch.Groups["threads"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int threads))
            {
                return null;
            }

            decimal? maximumFrequencyMHz = CoreRatioPattern.Matches(processorSection)
                .Cast<Match>()
                .Where(match => match.Groups["name"].Value.Equals(corePrefix, StringComparison.OrdinalIgnoreCase))
                .Select(match => ParseDecimal(match.Groups["ratio"].Value))
                .Where(ratio => ratio.HasValue)
                .Select(ratio => Multiply(baseFrequencyMHz, ratio))
                .Where(frequency => frequency.HasValue)
                .Select(frequency => frequency!.Value)
                .DefaultIfEmpty()
                .Max();

            return new CpuZCoreSet(
                coreSetMatch.Groups["name"].Value.ToUpperInvariant(),
                cores,
                threads,
                maximumFrequencyMHz == 0 ? null : maximumFrequencyMHz);
        }

        private static string ExtractProcessorSection(string report)
        {
            int start = report.IndexOf("Processors Information", StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return report;
            }

            int end = report.IndexOf("Thread dumps", start, StringComparison.OrdinalIgnoreCase);
            return end < 0 ? report[start..] : report[start..end];
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

        private static decimal? ReadNumber(string report, string label) => ParseDecimal(ReadValue(report, label));

        private static decimal? ParseDecimal(string? value)
        {
            Match match = value is null ? Match.Empty : FirstNumberPattern.Match(value);
            return match.Success
                && decimal.TryParse(match.Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal parsed)
                ? parsed
                : null;
        }

        private static decimal? Multiply(decimal? first, decimal? second) => first.HasValue && second.HasValue
            ? first.Value * second.Value
            : null;

        private static string? FormatTemperature(string? value)
        {
            decimal? temperature = ParseDecimal(value);
            return temperature.HasValue
                ? temperature.Value.ToString("0.##", CultureInfo.InvariantCulture) + " \u00B0C"
                : null;
        }
        private static string? SimplifyCache(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return RepeatedWhitespacePattern.Replace(
                CacheMultiplierPattern.Replace(CacheDetailPattern.Replace(value, string.Empty), " × "),
                " ").Trim();
        }
    }
}