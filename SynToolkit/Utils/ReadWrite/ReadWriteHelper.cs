using System.Runtime.InteropServices;

namespace SynToolkit.Utils.ReadWrite;

/// <summary>
/// Physical-memory read/write via inpoutx64. Only the memory surface is
/// exposed: the MSR/PCI/IO surface was removed because its driver was never
/// initialized and never called.
/// </summary>
public partial class ReadWriteHelper : IDisposable
{
    [DllImport("kernel32.dll")]
    private static extern bool IsBadReadPtr(IntPtr lp, nuint ucb);

    public ReadWriteHelper()
    {
        // Initialization handled by the native DLL.
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
        GC.SuppressFinalize(this);
    }
}
