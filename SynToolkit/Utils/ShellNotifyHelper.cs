using System;
using System.Runtime.InteropServices;

namespace SynToolkit.Utils;

/// <summary>
/// Provides shell notification helpers for notifying Explorer of changes.
/// </summary>
internal static class ShellNotifyHelper
{
    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    /// <summary>
    /// Notifies the shell that file type associations have changed.
    /// Call this after modifying context menu registry entries so Explorer
    /// updates immediately without requiring a restart or sign-out.
    /// </summary>
    public static void NotifyAssocChanged()
    {
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll", CallingConvention = CallingConvention.Winapi)]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
