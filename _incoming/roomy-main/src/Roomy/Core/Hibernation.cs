using System.ComponentModel;
using System.Diagnostics;
using System.IO.Enumeration;

namespace Roomy.Core;

/// <summary>
/// The hibernation file (hiberfil.sys). Roomy never deletes it directly: Windows' own
/// <c>powercfg /hibernate off</c> turns hibernation off and removes the file, and
/// <c>powercfg /hibernate on</c> brings it back.
/// </summary>
public static class Hibernation
{
    public static string FilePath => Path.Combine(Env.SystemRoot, "hiberfil.sys");

    /// <summary>Size of hiberfil.sys, or 0 when hibernation is off. Reads the directory entry, so no admin is needed.</summary>
    public static long FileSize()
    {
        try
        {
            var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true, RecurseSubdirectories = false };
            var sizes = new FileSystemEnumerable<long>(Env.SystemRoot, (ref FileSystemEntry e) => e.Length, options)
            {
                ShouldIncludePredicate = (ref FileSystemEntry e) =>
                    !e.IsDirectory && e.FileName.Equals("hiberfil.sys", StringComparison.OrdinalIgnoreCase),
            };
            foreach (var size in sizes) return size;
        }
        catch { }
        return 0;
    }

    public static bool IsOn => FileSize() > 0;

    /// <summary>
    /// Turns hibernation on or off with powercfg. Windows asks for admin once.
    /// Returns false if the prompt was declined or powercfg failed.
    /// </summary>
    public static async Task<bool> SetAsync(bool on)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "powercfg.exe"), on ? "/hibernate on" : "/hibernate off")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (p is null) return false;
            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch (Win32Exception) { return false; } // the admin prompt was declined
        catch { return false; }
    }
}
