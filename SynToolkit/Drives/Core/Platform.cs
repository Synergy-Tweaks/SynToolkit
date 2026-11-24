// Portions adapted from Roomy (MIT) © 2026 krezio — https://github.com/krezio/roomy
using System.Diagnostics;
using System.IO;
using System.Security.Principal;

#nullable enable

namespace SynToolkit.Drives.Core;

public static class Admin
{
    public static bool IsElevated { get; } = Check();

    private static bool Check()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}

public static class Shell
{
    public static void Open(string target)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    public static void Reveal(string path)
    {
        try
        {
            if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                using Process? process = Process.Start("explorer.exe", path);
            }
            else if (File.Exists(path))
            {
                using Process? process = Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            else if (Directory.Exists(path))
            {
                using Process? process = Process.Start("explorer.exe", $"\"{path}\"");
            }
        }
        catch
        {
        }
    }
}
