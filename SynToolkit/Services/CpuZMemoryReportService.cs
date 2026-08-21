#nullable enable

using System;
using System.Diagnostics;
using System.IO;

namespace SynToolkit.Services
{
    internal sealed record CpuZHardwareReport(CpuZMemoryTimings? MemoryTimings, CpuZProcessorDetails? ProcessorDetails, CpuZMainboardDetails? MainboardDetails);

    /// <summary>
    /// Reads one hidden CPU-Z report per app session. Memory, processor, and motherboard details share that report,
    /// which prevents a second CPU-Z process and keeps the initial Specs snapshot responsive.
    /// </summary>
    internal static class CpuZMemoryReportService
    {
        private const int ReportTimeoutMilliseconds = 25_000;
        private static readonly Lazy<CpuZHardwareReport?> CurrentReport = new(ReadCurrentReport);

        internal static CpuZMemoryTimings? GetCurrentTimings() => CurrentReport.Value?.MemoryTimings;

        internal static CpuZProcessorDetails? GetCurrentProcessorDetails() => CurrentReport.Value?.ProcessorDetails;

        internal static CpuZMainboardDetails? GetCurrentMainboardDetails() => CurrentReport.Value?.MainboardDetails;

        private static CpuZHardwareReport? ReadCurrentReport()
        {
            string executablePath = Path.Combine(AppContext.BaseDirectory, "assets", "Tools", "cpuz_x64.exe");
            if (!File.Exists(executablePath))
            {
                return null;
            }

            string reportBasePath = Path.Combine(
                Path.GetTempPath(),
                "SynToolkit-CpuZ-" + Guid.NewGuid().ToString("N"));
            string reportPath = reportBasePath + ".txt";
            try
            {
                using Process process = new();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    WorkingDirectory = Path.GetDirectoryName(executablePath),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                process.StartInfo.ArgumentList.Add("-txt=" + reportBasePath);

                if (!process.Start() || !process.WaitForExit(ReportTimeoutMilliseconds) || !File.Exists(reportPath))
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }

                    return null;
                }

                string report = File.ReadAllText(reportPath);
                return new CpuZHardwareReport(
                    CpuZMemoryTimingParser.TryParse(report),
                    CpuZProcessorDetailsParser.TryParse(report),
                    CpuZMainboardDetailsParser.TryParse(report));
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[Specs] CPU-Z report was unavailable.");
                return null;
            }
            finally
            {
                try
                {
                    if (File.Exists(reportPath))
                    {
                        File.Delete(reportPath);
                    }
                }
                catch (IOException)
                {
                    // The temporary report is harmless if a third-party scanner still holds it.
                }
            }
        }
    }
}