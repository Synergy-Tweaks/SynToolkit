using System.Runtime.InteropServices;

namespace SynToolkit.Utils.ReadWrite;

public partial class ReadWriteHelper : IDisposable
{
    [DllImport("kernel32.dll")]
    private static extern bool IsBadReadPtr(IntPtr lp, nuint ucb);

    public ReadWriteHelper()
    {
        // Initialization handled by native DLLs
    }

    // MSR
    public static bool ReadMsr(uint index, out ulong value)
    {
        uint eax = 0, edx = 0;
        if (WinRing.Rdmsr(index, ref eax, ref edx) != 0)
        {
            value = ((ulong)edx << 32) | eax;
            return true;
        }
        value = 0;
        return false;
    }

    public static bool WriteMsr(uint index, ulong value)
    {
        uint eax = (uint)(value & 0xFFFFFFFF);
        uint edx = (uint)(value >> 32);
        return WinRing.Wrmsr(index, eax, edx) != 0;
    }

    // PMC
    public static ulong ReadPmc(uint index)
    {
        uint eax = 0, edx = 0;
        WinRing.Rdpmc(index, ref eax, ref edx);
        return ((ulong)edx << 32) | eax;
    }

    public static ulong ReadPmcTx(uint index, UIntPtr threadAffinityMask)
    {
        uint eax = 0, edx = 0;
        WinRing.RdpmcTx(index, ref eax, ref edx, threadAffinityMask);
        return ((ulong)edx << 32) | eax;
    }

    // PCI
    public static bool ReadPci(uint bus, uint dev, uint func, byte offset, int size, out uint value)
    {
        uint address = WinRing.PciBusDevFunc(bus, dev, func);
        value = size switch
        {
            8 => WinRing.ReadPciConfigByte(address, offset),
            16 => WinRing.ReadPciConfigWord(address, offset),
            32 => WinRing.ReadPciConfigDword(address, offset),
            _ => 0
        };
        return true;
    }

    public static void WritePci(uint bus, uint dev, uint func, byte offset, uint value, int size)
    {
        uint address = WinRing.PciBusDevFunc(bus, dev, func);
        switch (size)
        {
            case 8: WinRing.WritePciConfigByte(address, offset, (byte)value); break;
            case 16: WinRing.WritePciConfigWord(address, offset, (ushort)value); break;
            case 32: WinRing.WritePciConfigDword(address, offset, value); break;
        }
    }

    // IO Port
    public static uint ReadIo(ushort port, int size)
    {
        return size switch
        {
            8 => WinRing.ReadIoPortByte(port),
            16 => WinRing.ReadIoPortWord(port),
            32 => WinRing.ReadIoPortDword(port),
            _ => 0
        };
    }

    public static void WriteIo(ushort port, uint value, int size)
    {
        switch (size)
        {
            case 8: WinRing.WriteIoPortByte(port, (byte)value); break;
            case 16: WinRing.WriteIoPortWord(port, (ushort)value); break;
            case 32: WinRing.WriteIoPortDword(port, value); break;
        }
    }

    // Physical Memory
    public unsafe bool ReadMemory(ulong address, byte[] buffer)
    {
        IntPtr pLinAddr = InpOut.MapPhysToLin((IntPtr)address, (uint)buffer.Length, out nint hMapping);
        if (pLinAddr == IntPtr.Zero) return false;

        if (IsBadReadPtr(pLinAddr, (nuint)buffer.Length))
        {
            _ = InpOut.UnmapPhysicalMemory(hMapping, pLinAddr);
            return false;
        }

        try
        {
            fixed (byte* pBuffer = buffer)
            {
                Buffer.MemoryCopy((void*)pLinAddr, pBuffer, buffer.Length, buffer.Length);
            }
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _ = InpOut.UnmapPhysicalMemory(hMapping, pLinAddr);
        }
    }

    public unsafe bool WriteMemory(ulong address, byte[] buffer)
    {
        IntPtr pLinAddr = InpOut.MapPhysToLin((IntPtr)address, (uint)buffer.Length, out nint hMapping);
        if (pLinAddr == IntPtr.Zero) return false;

        try
        {
            fixed (byte* pBuffer = buffer)
            {
                Buffer.MemoryCopy(pBuffer, (void*)pLinAddr, buffer.Length, buffer.Length);
            }
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _ = InpOut.UnmapPhysicalMemory(hMapping, pLinAddr);
        }
    }

    public bool ReadMemory32(ulong address, out uint value)
    {
        byte[] buffer = new byte[4];
        if (ReadMemory(address, buffer))
        {
            value = BitConverter.ToUInt32(buffer, 0);
            return true;
        }
        value = 0;
        return false;
    }

    public bool WriteMemory32(ulong address, uint value)
    {
        return WriteMemory(address, BitConverter.GetBytes(value));
    }

    public void Dispose()
    {
        WinRing.DeinitializeOls();
        GC.SuppressFinalize(this);
    }
}
