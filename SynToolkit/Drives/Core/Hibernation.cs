// Portions adapted from Roomy (MIT) © 2026 krezio — https://github.com/krezio/roomy
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;

#nullable enable

namespace SynToolkit.Drives.Core;

public static class Hibernation
{
    public static string FilePath => Path.Combine(Env.SystemRoot, "hiberfil.sys");

    public static long FileSize()
    {
        try
        {
            EnumerationOptions options = new()
            {
                AttributesToSkip = 0,
                IgnoreInaccessible = true,
                RecurseSubdirectories = false
            };

            FileSystemEnumerable<long> sizes = new(
                Env.SystemRoot,
                static (ref FileSystemEntry entry) => entry.Length,
                options)
            {
                ShouldIncludePredicate = static (ref FileSystemEntry entry) =>
                    !entry.IsDirectory && entry.FileName.Equals("hiberfil.sys", StringComparison.OrdinalIgnoreCase)
            };

            foreach (long size in sizes)
            {
                return size;
            }
        }
        catch
        {
        }

        return 0;
    }

    public static bool IsOn => FileSize() > 0;

    public static async Task<bool> SetAsync(bool on)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "powercfg.exe"), on ? "/hibernate on" : "/hibernate off")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (process is null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }
}
