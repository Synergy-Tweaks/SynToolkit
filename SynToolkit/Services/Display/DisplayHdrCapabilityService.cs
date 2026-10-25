#nullable enable

using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace SynToolkit.Services.Display;

internal static class DisplayHdrCapabilityService
{
    internal const int EdidBlockSize = 128;

    private const string DisplayEnumKeyPath = @"SYSTEM\CurrentControlSet\Enum\DISPLAY";

    private const byte CtaExtensionTag = 0x02;

    private const byte ExtendedDataBlockTag = 0x07;

    private const byte HdrStaticMetadataExtendedTag = 0x06;

    private const byte HdrEotfMask = 0x0C;

    internal static bool IsHdrSupportedByEdid(string? monitorDevicePath)
    {
        var edid = ReadEdid(monitorDevicePath);
        return edid is not null && ContainsHdrStaticMetadata(edid);
    }

    internal static byte[]? ReadEdid(string? monitorDevicePath)
    {
        if (!TryGetDeviceIds(monitorDevicePath, out var hardwareId, out var instanceId))
            return null;

        try
        {
            using var hardwareKey = Registry.LocalMachine.OpenSubKey(DisplayEnumKeyPath + "\\" + hardwareId);
            if (hardwareKey is null)
                return null;

            if (instanceId.Length > 0)
            {
                var exact = ReadEdidBlock(hardwareKey, instanceId);
                if (exact is not null)
                    return exact;
            }

            var group = InstanceGroup(instanceId);
            if (group.Length == 0)
                return null;

            foreach (var instanceName in hardwareKey.GetSubKeyNames())
            {
                if (!instanceName.StartsWith(group, StringComparison.OrdinalIgnoreCase))
                    continue;

                var edid = ReadEdidBlock(hardwareKey, instanceName);
                if (edid is not null)
                    return edid;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"HDR capability read failed: {ex.Message}");
        }

        return null;
    }

    internal static bool TryGetDeviceIds(string? monitorDevicePath, out string hardwareId, out string instanceId)
    {
        hardwareId = string.Empty;
        instanceId = string.Empty;
        if (string.IsNullOrWhiteSpace(monitorDevicePath))
            return false;

        var parts = monitorDevicePath.Split(['#', '\\'], StringSplitOptions.RemoveEmptyEntries);
        var displayIndex = Array.FindIndex(parts, part => string.Equals(part, "DISPLAY", StringComparison.OrdinalIgnoreCase));
        if (displayIndex < 0 || parts.Length < displayIndex + 3)
            return false;

        hardwareId = parts[displayIndex + 1];
        instanceId = parts[displayIndex + 2];
        return hardwareId.Length > 0;
    }

    internal static string InstanceGroup(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return string.Empty;

        var first = instanceId.IndexOf('&');
        if (first < 0)
            return instanceId;

        var second = instanceId.IndexOf('&', first + 1);
        return second < 0 ? instanceId : instanceId[..second];
    }

    internal static bool ContainsHdrStaticMetadata(byte[] edid)
    {
        if (edid.Length < EdidBlockSize)
            return false;

        var extensionCount = Math.Min(edid[126], (edid.Length / EdidBlockSize) - 1);
        for (var block = 1; block <= extensionCount; block++)
        {
            var start = block * EdidBlockSize;
            if (start + EdidBlockSize > edid.Length)
                break;

            if (edid[start] != CtaExtensionTag)
                continue;

            var declaredLength = edid[start + 2];
            var end = declaredLength >= 4 && declaredLength <= EdidBlockSize
                ? start + declaredLength
                : start + EdidBlockSize - 1;

            var offset = start + 4;
            while (offset < end)
            {
                var header = edid[offset++];
                var length = header & 0x1F;
                if (length == 0 || offset + length > end)
                    break;

                if ((header >> 5) == ExtendedDataBlockTag && length >= 2 &&
                    edid[offset] == HdrStaticMetadataExtendedTag && (edid[offset + 1] & HdrEotfMask) != 0)
                    return true;

                offset += length;
            }
        }

        return false;
    }

    private static byte[]? ReadEdidBlock(RegistryKey hardwareKey, string instanceName)
    {
        try
        {
            using var instanceKey = hardwareKey.OpenSubKey(instanceName);
            using var parametersKey = instanceKey?.OpenSubKey("Device Parameters");
            return parametersKey?.GetValue("EDID") as byte[];
        }
        catch (Exception)
        {
            return null;
        }
    }
}
