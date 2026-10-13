using System.Runtime.InteropServices;

namespace SynToolkit.Utils.ReadWrite;

internal static partial class InpOut
{
    private const string DllName = "inpoutx64.dll";

    [LibraryImport(DllName)]
    public static partial IntPtr MapPhysToLin(IntPtr pbPhysAddr, uint dwPhysSize, out IntPtr pHandle);

    [LibraryImport(DllName)]
    public static partial int UnmapPhysicalMemory(IntPtr pHandle, IntPtr pbLinAddr);
}
