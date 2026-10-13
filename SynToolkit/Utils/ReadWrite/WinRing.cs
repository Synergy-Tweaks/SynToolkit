using System.Runtime.InteropServices;

namespace SynToolkit.Utils.ReadWrite;

internal static partial class WinRing
{
    private const string DllName = "WinRing0x64.dll";

    [LibraryImport(DllName)]
    public static partial int InitializeOls();

    [LibraryImport(DllName)]
    public static partial void DeinitializeOls();

    [LibraryImport(DllName)]
    public static partial uint GetDllStatus();

    // MSR
    [LibraryImport(DllName)]
    public static partial int Rdmsr(uint index, ref uint eax, ref uint edx);

    [LibraryImport(DllName)]
    public static partial int Wrmsr(uint index, uint eax, uint edx);

    [LibraryImport(DllName)]
    public static partial int RdmsrTx(uint index, ref uint eax, ref uint edx, UIntPtr threadAffinityMask);

    [LibraryImport(DllName)]
    public static partial int WrmsrTx(uint index, uint eax, uint edx, UIntPtr threadAffinityMask);

    // PMC
    [LibraryImport(DllName)]
    public static partial int Rdpmc(uint index, ref uint eax, ref uint edx);

    [LibraryImport(DllName)]
    public static partial int RdpmcTx(uint index, ref uint eax, ref uint edx, UIntPtr threadAffinityMask);

    // PCI
    public static uint PciBusDevFunc(uint bus, uint dev, uint func)
    {
        return ((bus & 0xFF) << 8) | ((dev & 0x1F) << 3) | (func & 7);
    }

    [LibraryImport(DllName)]
    public static partial byte ReadPciConfigByte(uint pciAddress, byte regAddress);

    [LibraryImport(DllName)]
    public static partial ushort ReadPciConfigWord(uint pciAddress, byte regAddress);

    [LibraryImport(DllName)]
    public static partial uint ReadPciConfigDword(uint pciAddress, byte regAddress);

    [LibraryImport(DllName)]
    public static partial void WritePciConfigByte(uint pciAddress, byte regAddress, byte value);

    [LibraryImport(DllName)]
    public static partial void WritePciConfigWord(uint pciAddress, byte regAddress, ushort value);

    [LibraryImport(DllName)]
    public static partial void WritePciConfigDword(uint pciAddress, byte regAddress, uint value);

    // IO
    [LibraryImport(DllName)]
    public static partial byte ReadIoPortByte(ushort port);

    [LibraryImport(DllName)]
    public static partial ushort ReadIoPortWord(ushort port);

    [LibraryImport(DllName)]
    public static partial uint ReadIoPortDword(ushort port);

    [LibraryImport(DllName)]
    public static partial void WriteIoPortByte(ushort port, byte value);

    [LibraryImport(DllName)]
    public static partial void WriteIoPortWord(ushort port, ushort value);

    [LibraryImport(DllName)]
    public static partial void WriteIoPortDword(ushort port, uint value);
}
