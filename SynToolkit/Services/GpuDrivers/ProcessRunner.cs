using System.Diagnostics;
using System.Text;

namespace SynToolkit.Services.GpuDrivers;

internal static class ProcessRunner
{
    public static async Task<(bool Success, string Output)> RunAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken = default)
    {
        StringBuilder output = new();

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                lock (output)
                {
                    output.AppendLine(args.Data);
                }
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                lock (output)
                {
                    output.AppendLine(args.Data);
                }
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Unable to start {fileName}.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        process.WaitForExit();

        string combined = output.ToString().Trim();
        return (process.ExitCode == 0, combined);
    }
}
