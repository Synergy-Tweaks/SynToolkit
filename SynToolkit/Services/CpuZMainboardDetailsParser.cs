#nullable enable

using System;

namespace SynToolkit.Services
{
    internal sealed record CpuZMainboardDetails(
        string? Model,
        string? Northbridge,
        string? Southbridge,
        string? BusSpecification,
        string? GraphicsInterface,
        string? LpcioVendor,
        string? LpcioModel);

    internal static class CpuZMainboardDetailsParser
    {
        internal static CpuZMainboardDetails? TryParse(string report)
        {
            if (string.IsNullOrWhiteSpace(report))
            {
                return null;
            }

            CpuZMainboardDetails details = new(
                CleanModel(ReadValue(report, "Mainboard Model")),
                ReadValue(report, "Northbridge"),
                ReadValue(report, "Southbridge"),
                ReadValue(report, "Bus Specification"),
                ReadValue(report, "Graphic Interface"),
                ReadValue(report, "LPCIO Vendor"),
                ReadValue(report, "LPCIO Model"));

            return details.Model is null
                && details.Northbridge is null
                && details.Southbridge is null
                && details.LpcioVendor is null
                ? null
                : details;
        }

        private static string? ReadValue(string report, string label)
        {
            foreach (string line in report.Split(["\r\n", "\n"], StringSplitOptions.None))
            {
                string trimmed = line.TrimStart();
                if (!trimmed.StartsWith(label, StringComparison.OrdinalIgnoreCase)
                    || (trimmed.Length > label.Length && !char.IsWhiteSpace(trimmed[label.Length])))
                {
                    continue;
                }

                string value = trimmed[label.Length..].Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }

            return null;
        }

        private static string? CleanModel(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            int hardwareIdStart = value.IndexOf(" (0x", StringComparison.OrdinalIgnoreCase);
            return (hardwareIdStart >= 0 ? value[..hardwareIdStart] : value).Trim();
        }
    }
}