using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using SynToolkit.Utils.ReadWrite;

namespace SynToolkit.Utils;

/// <summary>
/// Information about a XHCI USB controller device
/// </summary>
public class XhciDeviceInfo
{
    public string FriendlyName { get; set; } = string.Empty;
    public string PnpDeviceId { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public ulong BaseAddress { get; set; }
    public bool IsImodEnabled { get; set; }
}

/// <summary>
/// Helper class for managing XHCI USB controller devices and their Interrupt Moderation (IMOD) settings
/// </summary>
public static class XhciDeviceHelper
{
    private const string IMOD_REGISTRY_PATH = @"SOFTWARE\SynToolkit\XhciImod";

    // Windows xHCI driver service names: Intel/inbox use USBXHCI, AMD uses AMDXHCI.
    private static readonly string[] XhciServiceNames = ["USBXHCI", "AMDXHCI"];

    /// <summary>
    /// Stable per-device id for registry names. string.GetHashCode is random
    /// per process and must never persist across runs.
    /// </summary>
    internal static string StableDeviceId(string pnpDeviceId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(pnpDeviceId.ToUpperInvariant()));
        return Convert.ToHexString(hash)[..16];
    }

    // Win32 API declarations for device enumeration
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint property, out uint propertyRegDataType, byte[] propertyBuffer, uint propertyBufferSize, out uint requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInstanceId(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, char[] deviceInstanceId, uint deviceInstanceIdSize, out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNode(out uint pdnDevInst, string pDeviceID, uint ulFlags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_First_Log_Conf(out IntPtr plcLogConf, uint dnDevInst, uint ulFlags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Next_Res_Des(out IntPtr prdResDes, IntPtr rdResDes, uint forResource, out uint pResourceID, uint ulFlags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Res_Des_Data_Size(out uint pulSize, IntPtr rdResDes, uint ulFlags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Res_Des_Data(IntPtr rdResDes, byte[] buffer, uint bufferLen, uint ulFlags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Free_Log_Conf_Handle(IntPtr lcLogConf);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Free_Res_Des_Handle(IntPtr rdResDes);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    private static readonly Guid GUID_DEVCLASS_USB = new("36fc9e60-c465-11cf-8056-444553540000");
    private const uint DIGCF_PRESENT = 0x02;
    private const uint SPDRP_FRIENDLYNAME = 0x0000000C;
    private const uint SPDRP_SERVICE = 0x00000004;
    private const uint SPDRP_LOCATION_INFORMATION = 0x0000000D;
    private const uint CR_SUCCESS = 0;
    private const uint ALLOC_LOG_CONF = 0x00000002;
    private const uint BOOT_LOG_CONF = 0x00000003;

    /// <summary>
    /// Gets all XHCI USB controllers on the system
    /// </summary>
    public static List<XhciDeviceInfo> GetXhciControllers()
    {
        var devices = new List<XhciDeviceInfo>();

        try
        {
            Guid classGuid = GUID_DEVCLASS_USB;
            IntPtr deviceInfoSet = SetupDiGetClassDevs(ref classGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT);

            if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
                return devices;

            try
            {
                uint index = 0;
                SP_DEVINFO_DATA deviceInfoData = new() { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };

                while (SetupDiEnumDeviceInfo(deviceInfoSet, index++, ref deviceInfoData))
                {
                    string service = GetDeviceProperty(deviceInfoSet, ref deviceInfoData, SPDRP_SERVICE);

                    // Intel/inbox (USBXHCI) and AMD (AMDXHCI) controllers.
                    if (!XhciServiceNames.Any(s => string.Equals(service, s, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    string pnpDeviceId = GetDeviceInstanceId(deviceInfoSet, ref deviceInfoData);
                    if (string.IsNullOrEmpty(pnpDeviceId))
                        continue;

                    ulong baseAddress = GetDeviceBaseAddress(pnpDeviceId);
                    if (baseAddress == 0)
                        continue;

                    var device = new XhciDeviceInfo
                    {
                        FriendlyName = GetDeviceProperty(deviceInfoSet, ref deviceInfoData, SPDRP_FRIENDLYNAME),
                        PnpDeviceId = pnpDeviceId,
                        Location = GetDeviceProperty(deviceInfoSet, ref deviceInfoData, SPDRP_LOCATION_INFORMATION),
                        BaseAddress = baseAddress
                    };

                    // Check current IMOD state
                    try
                    {
                        device.IsImodEnabled = GetImodState(device);
                    }
                    catch
                    {
                        device.IsImodEnabled = true; // Assume enabled if we can't read
                    }

                    devices.Add(device);
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }
        }
        catch (Exception ex)
        {
            App.logger.Error($"[XHCI] Error enumerating XHCI controllers: {ex.Message}");
        }

        return devices;
    }

    private static string GetDeviceProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint property)
    {
        SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref deviceInfoData, property, out _, null, 0, out uint requiredSize);
        if (requiredSize == 0) return string.Empty;

        byte[] buffer = new byte[requiredSize];
        if (SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref deviceInfoData, property, out _, buffer, requiredSize, out _))
        {
            return System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        }
        return string.Empty;
    }

    private static string GetDeviceInstanceId(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData)
    {
        SetupDiGetDeviceInstanceId(deviceInfoSet, ref deviceInfoData, null, 0, out uint requiredSize);
        if (requiredSize == 0) return string.Empty;

        char[] buffer = new char[requiredSize];
        if (SetupDiGetDeviceInstanceId(deviceInfoSet, ref deviceInfoData, buffer, requiredSize, out _))
        {
            return new string(buffer).TrimEnd('\0');
        }
        return string.Empty;
    }

    private static ulong GetDeviceBaseAddress(string pnpDeviceId)
    {
        if (CM_Locate_DevNode(out uint devInst, pnpDeviceId, 0) != CR_SUCCESS)
            return 0;

        if (CM_Get_First_Log_Conf(out IntPtr logConf, devInst, ALLOC_LOG_CONF) != CR_SUCCESS)
        {
            if (CM_Get_First_Log_Conf(out logConf, devInst, BOOT_LOG_CONF) != CR_SUCCESS)
                return 0;
        }

        try
        {
            IntPtr currentHandle = logConf;
            bool isFirst = true;
            IntPtr resDes = IntPtr.Zero;

            while (true)
            {
                int result = CM_Get_Next_Res_Des(out IntPtr nextResDes, currentHandle, 0, out uint resType, 0);

                if (!isFirst && resDes != IntPtr.Zero)
                    CM_Free_Res_Des_Handle(resDes);

                if (result != CR_SUCCESS)
                    break;

                resDes = nextResDes;
                isFirst = false;
                currentHandle = resDes;

                // Memory resource types (MEM_RESOURCE = 1, MEM_LARGE_RESOURCE = 7)
                if (resType == 1 || resType == 7)
                {
                    if (CM_Get_Res_Des_Data_Size(out uint dataSize, resDes, 0) == CR_SUCCESS && dataSize >= 24)
                    {
                        byte[] data = new byte[dataSize];
                        if (CM_Get_Res_Des_Data(resDes, data, dataSize, 0) == CR_SUCCESS)
                        {
                            ulong addr = BitConverter.ToUInt64(data, 8);
                            CM_Free_Res_Des_Handle(resDes);
                            return addr;
                        }
                    }
                }
            }

            if (resDes != IntPtr.Zero)
                CM_Free_Res_Des_Handle(resDes);
        }
        finally
        {
            CM_Free_Log_Conf_Handle(logConf);
        }

        return 0;
    }

    /// <summary>
    /// Gets the current IMOD state for a device (true = enabled, false = disabled)
    /// </summary>
    public static bool GetImodState(XhciDeviceInfo device, ReadWriteHelper sharedHw = null)
    {
        using var localHw = sharedHw == null ? new ReadWriteHelper() : null;
        var hw = sharedHw ?? localHw;

        if (device.BaseAddress == 0) return false;

        if (!hw.ReadMemory32(device.BaseAddress + 0x18, out uint rtsoff))
            throw new Exception($"Failed to read RTSOFF register at base address 0x{device.BaseAddress:X}");

        if (rtsoff == 0xFFFFFFFF || rtsoff == 0)
            throw new Exception($"Invalid RTSOFF value (0x{rtsoff:X8}) read from controller base address 0x{device.BaseAddress:X}.");

        ulong runtime = device.BaseAddress + (rtsoff & ~0x1Fu);

        if (!hw.ReadMemory32(device.BaseAddress + 0x04, out uint hcs1))
            throw new Exception($"Failed to read HCS1 register at base address 0x{device.BaseAddress:X}");

        if (hcs1 == 0xFFFFFFFF)
            throw new Exception($"Invalid HCS1 value (0x{hcs1:X8}) read from controller base address 0x{device.BaseAddress:X}.");

        int max = (int)((hcs1 >> 8) & 0x7FF);
        if (max > 128)
            throw new Exception($"Sanity check failed: unusually large number of interrupters ({max}) read from HCS1 (0x{hcs1:X8}).");

        for (int i = 0; i < max; i++)
        {
            if (!hw.ReadMemory32(runtime + 0x24 + (0x20 * (ulong)i), out uint imod))
                throw new Exception($"Failed to read IMOD register at index {i}");
            if (imod != 0) return true;
        }

        return false;
    }

    /// <summary>
    /// Toggles IMOD for a device (enable = true to enable IMOD, false to disable)
    /// </summary>
    public static void ToggleImod(XhciDeviceInfo device, bool enable)
    {
        using var hw = new ReadWriteHelper();
        if (device.BaseAddress == 0) return;

        if (!hw.ReadMemory32(device.BaseAddress + 0x18, out uint rtsoff))
            throw new Exception($"Failed to read RTSOFF register at base address 0x{device.BaseAddress:X}");

        if (rtsoff == 0xFFFFFFFF || rtsoff == 0)
            throw new Exception($"Invalid RTSOFF value (0x{rtsoff:X8}) read from controller base address 0x{device.BaseAddress:X}.");

        ulong runtime = device.BaseAddress + (rtsoff & ~0x1Fu);

        if (!hw.ReadMemory32(device.BaseAddress + 0x04, out uint hcs1))
            throw new Exception($"Failed to read HCS1 register at base address 0x{device.BaseAddress:X}");

        if (hcs1 == 0xFFFFFFFF)
            throw new Exception($"Invalid HCS1 value (0x{hcs1:X8}) read from controller base address 0x{device.BaseAddress:X}.");

        int max = (int)((hcs1 >> 8) & 0x7FF);
        if (max > 128)
            throw new Exception($"Sanity check failed: unusually large number of interrupters ({max}) read from HCS1 (0x{hcs1:X8}).");

        if (enable)
        {
            // Restore saved intervals
            var savedIntervals = LoadSavedIntervals(device.PnpDeviceId);
            if (savedIntervals is null)
            {
                // Nothing recorded: if the controller is already moderated there
                // is nothing to do, otherwise restoring is impossible.
                if (GetImodState(device, hw))
                {
                    SaveImodState(device.PnpDeviceId, true);
                    return;
                }

                throw new InvalidOperationException(
                    $"No saved IMOD values exist for '{device.FriendlyName}'. Disable IMOD first so the originals are recorded, then re-enable.");
            }

            foreach (var kvp in savedIntervals)
            {
                if (ulong.TryParse(kvp.Key, out ulong addr) && uint.TryParse(kvp.Value?.ToString(), out uint val))
                {
                    if (!hw.WriteMemory32(addr, val))
                        throw new Exception($"Failed to write memory address 0x{addr:X}");
                }
            }
        }
        else
        {
            // Save current intervals before disabling
            SaveImodIntervals(device, hw, runtime, max);

            // Set all IMOD registers to 0 (disable)
            for (int i = 0; i < max; i++)
            {
                if (!hw.WriteMemory32(runtime + 0x24 + (0x20 * (ulong)i), 0))
                    throw new Exception($"Failed to write memory address 0x{runtime + 0x24 + (0x20 * (ulong)i):X}");
            }
        }

        // Update stored state
        SaveImodState(device.PnpDeviceId, enable);
    }

    private static void SaveImodIntervals(XhciDeviceInfo device, ReadWriteHelper hw, ulong runtime, int max)
    {
        var intervals = new JsonObject();
        for (int i = 0; i < max; i++)
        {
            ulong addr = runtime + 0x24 + (0x20 * (ulong)i);
            if (!hw.ReadMemory32(addr, out uint val))
                throw new Exception($"Failed to read memory address 0x{addr:X}");
            intervals[addr.ToString()] = val;
        }

        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(IMOD_REGISTRY_PATH);
            key?.SetValue($"Intervals_{StableDeviceId(device.PnpDeviceId)}", intervals.ToJsonString(), RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            App.logger.Error($"[XHCI] Failed to save IMOD intervals: {ex.Message}");
        }
    }

    private static JsonObject LoadSavedIntervals(string pnpDeviceId)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(IMOD_REGISTRY_PATH);
            var json = key?.GetValue($"Intervals_{StableDeviceId(pnpDeviceId)}")?.ToString();
            if (!string.IsNullOrEmpty(json))
            {
                return JsonNode.Parse(json)?.AsObject();
            }
        }
        catch (Exception ex)
        {
            App.logger.Error($"[XHCI] Failed to load saved IMOD intervals: {ex.Message}");
        }
        return null;
    }

    private static void SaveImodState(string pnpDeviceId, bool enabled)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(IMOD_REGISTRY_PATH);
            key?.SetValue($"State_{StableDeviceId(pnpDeviceId)}", enabled ? 1 : 0, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            App.logger.Error($"[XHCI] Failed to save IMOD state: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if IMOD is currently disabled for any XHCI controller.
    /// Hardware is truth: a stale persisted record (e.g. registers reset by a
    /// reboot) must never report the tweak as active.
    /// </summary>
    public static bool IsAnyImodDisabled()
    {
        try
        {
            var controllers = GetXhciControllers();
            foreach (var controller in controllers)
            {
                // Check actual hardware state
                if (!controller.IsImodEnabled)
                    return true;
            }
        }
        catch
        {
            // Ignore errors
        }
        return false;
    }

    /// <summary>
    /// Enumerates controllers and proves their registers are readable. Throws
    /// with an actionable message when the access driver is blocked (Secure
    /// Boot / Memory Integrity) instead of letting callers "succeed" silently.
    /// </summary>
    private static List<XhciDeviceInfo> RequireAccessibleControllers()
    {
        var controllers = GetXhciControllers();
        if (controllers.Count == 0)
        {
            throw new NotSupportedException(
                "No XHCI USB controllers were found. This tweak needs an Intel (USBXHCI) or AMD (AMDXHCI) USB controller.");
        }

        using var hw = new ReadWriteHelper();
        foreach (var controller in controllers)
        {
            try
            {
                GetImodState(controller, hw);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Cannot access '{controller.FriendlyName}' registers. The inpoutx64 access driver is likely blocked " +
                    "(Secure Boot or Memory Integrity/VBS must be off for this tweak). " +
                    $"Details: {exception.Message}");
            }
        }

        return controllers;
    }

    /// <summary>
    /// Disables IMOD for all XHCI controllers
    /// </summary>
    public static void DisableAllImod()
    {
        var controllers = RequireAccessibleControllers();
        var failures = new List<string>();
        foreach (var controller in controllers)
        {
            try
            {
                ToggleImod(controller, false);
            }
            catch (Exception ex)
            {
                App.logger.Error($"[XHCI] Failed to disable IMOD for {controller.FriendlyName}: {ex.Message}");
                failures.Add(controller.FriendlyName);
            }
        }

        VerifyAllImod(GetXhciControllers(), expectEnabled: false, failures);
    }

    /// <summary>
    /// Enables IMOD for all XHCI controllers (restores default)
    /// </summary>
    public static void EnableAllImod()
    {
        var controllers = RequireAccessibleControllers();
        var failures = new List<string>();
        foreach (var controller in controllers)
        {
            try
            {
                ToggleImod(controller, true);
            }
            catch (Exception ex)
            {
                App.logger.Error($"[XHCI] Failed to enable IMOD for {controller.FriendlyName}: {ex.Message}");
                failures.Add(controller.FriendlyName);
            }
        }

        VerifyAllImod(GetXhciControllers(), expectEnabled: true, failures);
    }

    /// <summary>
    /// Re-reads hardware after an apply: success is reported only when every
    /// controller actually holds the requested state.
    /// </summary>
    private static void VerifyAllImod(
        List<XhciDeviceInfo> controllers,
        bool expectEnabled,
        List<string> failures)
    {
        foreach (var controller in controllers)
        {
            if (controller.IsImodEnabled != expectEnabled)
            {
                failures.Add($"{controller.FriendlyName} (verification)");
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                $"IMOD change did not take effect on: {string.Join(", ", failures.Distinct())}.");
        }
    }
}
