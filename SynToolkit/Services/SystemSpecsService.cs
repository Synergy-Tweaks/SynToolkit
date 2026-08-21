#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using Microsoft.Win32;
using SynToolkit.Models;

namespace SynToolkit.Services
{
    /// <summary>
    /// Reads read-only hardware/OS identity via WMI for the Specs tab. Never modifies the
    /// system. GPU VRAM prefers the registry's HardwareInformation.qwMemorySize (a 64-bit
    /// value) over WMI's Win32_VideoController.AdapterRAM, which is a 32-bit field that wraps
    /// around for adapters with more than ~4 GB of VRAM — a well-documented WMI limitation, not
    /// a hypothetical edge case.
    /// </summary>
    public static class SystemSpecsService
    {
        private const string VideoClassKeyPath =
            @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

        public static SystemSpecsSnapshot GetSnapshot(ISystemInformationService systemInformationService)
        {
            SystemInformationSnapshot windowsInfo = systemInformationService.Detect();

            ulong totalMemoryBytes = GetTotalMemoryBytes();

            return new SystemSpecsSnapshot(
                GetCpu(),
                GetGpus(),
                totalMemoryBytes,
                GetMemoryModules(totalMemoryBytes),
                GetStorageDrives(),
                GetNetworkAdapters(),
                GetMotherboard(),
                windowsInfo.WindowsProductName,
                windowsInfo.WindowsDisplayVersion,
                windowsInfo.WindowsBuild,
                System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString());
        }

        private static CpuSpec? GetCpu()
        {
            try
            {
                using ManagementObjectSearcher searcher = new("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    using (item)
                    {
                        string name = (item["Name"] as string ?? "Unknown CPU").Trim();
                        int cores = Convert.ToInt32(item["NumberOfCores"] ?? 0);
                        int logical = Convert.ToInt32(item["NumberOfLogicalProcessors"] ?? 0);
                        uint clock = Convert.ToUInt32(item["MaxClockSpeed"] ?? 0u);
                        return new CpuSpec(name, cores, logical, clock);
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[Specs] Unable to read CPU information via WMI.");
            }

            return null;
        }

        private static IReadOnlyList<GpuSpec> GetGpus()
        {
            List<GpuSpec> gpus = new();
            try
            {
                using ManagementObjectSearcher searcher = new("SELECT Name, AdapterRAM, DriverVersion, PNPDeviceID FROM Win32_VideoController");
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    using (item)
                    {
                        string name = item["Name"] as string ?? "Unknown GPU";
                        string pnpDeviceId = item["PNPDeviceID"] as string ?? string.Empty;
                        string? driverVersion = item["DriverVersion"] as string;
                        ulong? adapterRam = item["AdapterRAM"] is object rawAdapterRam
                            ? Convert.ToUInt64(rawAdapterRam)
                            : null;

                        ulong? accurateVram = TryGetAccurateVramBytes(pnpDeviceId);
                        string iconPath = GpuDetectionService.GetIconPath(name, pnpDeviceId);
                        gpus.Add(new GpuSpec(name, accurateVram ?? adapterRam, driverVersion, iconPath));
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[Specs] Unable to read GPU information via WMI.");
            }

            return gpus;
        }

        private static ulong? TryGetAccurateVramBytes(string pnpDeviceId)
        {
            if (string.IsNullOrWhiteSpace(pnpDeviceId))
            {
                return null;
            }

            RegistryKey? classKey;
            try
            {
                classKey = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[Specs] Unable to open the video adapter class registry key.");
                return null;
            }

            if (classKey is null)
            {
                return null;
            }

            using (classKey)
            {
                string[] subKeyNames;
                try
                {
                    subKeyNames = classKey.GetSubKeyNames();
                }
                catch (Exception exception)
                {
                    App.logger.Debug(exception, "[Specs] Unable to enumerate video adapter registry subkeys.");
                    return null;
                }

                foreach (string subKeyName in subKeyNames)
                {
                    // Some subkeys under this class GUID (e.g. a reserved "Properties" key,
                    // confirmed present on real hardware, not hypothetical) can throw a
                    // permission exception on OpenSubKey. That must not abort the whole scan —
                    // otherwise a permission failure on one subkey silently discards an
                    // already-found correct match on another, and every GPU falls back to
                    // WMI's AdapterRAM, which is known to be wrong for cards with more than ~4 GB.
                    try
                    {
                        using RegistryKey? subKey = classKey.OpenSubKey(subKeyName);
                        object? matchingDeviceId = subKey?.GetValue("MatchingDeviceId");
                        if (matchingDeviceId is not string matchText || matchText.Length == 0)
                        {
                            continue;
                        }

                        if (!pnpDeviceId.Contains(matchText, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        object? memorySize = subKey?.GetValue("HardwareInformation.qwMemorySize");
                        if (memorySize is not null)
                        {
                            return Convert.ToUInt64(memorySize);
                        }
                    }
                    catch (Exception exception)
                    {
                        App.logger.Debug(exception, "[Specs] Unable to read video adapter registry subkey '{0}'.", subKeyName);
                    }
                }
            }

            return null;
        }

        private static ulong GetTotalMemoryBytes()
        {
            try
            {
                using ManagementObjectSearcher searcher = new("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    using (item)
                    {
                        return Convert.ToUInt64(item["TotalPhysicalMemory"] ?? 0UL);
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[Specs] Unable to read total memory via WMI.");
            }

            return 0;
        }

        private static IReadOnlyList<MemoryModuleSpec> GetMemoryModules(ulong totalMemoryBytes)
        {
            List<MemoryModuleSpec> modules = new();
            try
            {
                using ManagementObjectSearcher searcher = new("SELECT Manufacturer, Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType, FormFactor FROM Win32_PhysicalMemory");
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    using (item)
                    {
                        string? manufacturer = (item["Manufacturer"] as string)?.Trim();
                        ulong capacity = Convert.ToUInt64(item["Capacity"] ?? 0UL);
                        uint? configuredClockSpeed = ReadPositiveUInt32(item["ConfiguredClockSpeed"]);
                        uint? reportedSpeed = ReadPositiveUInt32(item["Speed"]);
                        uint? smbiosMemoryType = ReadUInt32(item["SMBIOSMemoryType"]);
                        ushort? formFactor = ReadUInt16(item["FormFactor"]);
                        modules.Add(new MemoryModuleSpec(
                            manufacturer,
                            capacity,
                            configuredClockSpeed ?? reportedSpeed,
                            GetMemoryTechnology(smbiosMemoryType),
                            null,
                            null,
                            IsMemoryStick(formFactor)));
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[Specs] Unable to read memory module information via WMI.");
            }

            return MemoryModuleInventoryNormalizer.Normalize(modules, totalMemoryBytes);
        }

        /// <summary>
        /// Adds optional live timing data after the WMI snapshot has already been shown.
        /// CPU-Z can take several seconds to generate its report, so it must not delay the
        /// rest of the Specs tab.
        /// </summary>
        public static IReadOnlyList<MemoryModuleSpec> AddCurrentMemoryTimingDetails(
            IReadOnlyList<MemoryModuleSpec> modules)
        {
            CpuZMemoryTimings? cpuZTimings = CpuZMemoryReportService.GetCurrentTimings();
            return modules
                .Select(module => module with
                {
                    MemoryType = module.MemoryType ?? cpuZTimings?.MemoryType,
                    TimingText = cpuZTimings?.TimingText
                })
                .ToList();
        }

        private static IReadOnlyList<StorageDriveSpec> GetStorageDrives()
        {
            List<StorageDriveSpec> drives = new();
            try
            {
                using ManagementObjectSearcher searcher = new("SELECT Model, Size, MediaType, InterfaceType FROM Win32_DiskDrive");
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    using (item)
                    {
                        string model = (item["Model"] as string ?? "Unknown drive").Trim();
                        ulong size = item["Size"] is object rawSize ? Convert.ToUInt64(rawSize) : 0UL;
                        string? mediaType = item["MediaType"] as string;
                        string? interfaceType = item["InterfaceType"] as string;
                        drives.Add(new StorageDriveSpec(model, size, mediaType, interfaceType));
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[Specs] Unable to read storage drive information via WMI.");
            }

            return drives.OrderByDescending(drive => drive.SizeBytes).ToList();
        }

        private static IReadOnlyList<NetworkAdapterSpec> GetNetworkAdapters()
        {
            List<NetworkAdapterSpec> adapters = new();
            try
            {
                using ManagementObjectSearcher searcher = new(
                    "SELECT Name, Manufacturer, MACAddress, NetConnectionID, NetConnectionStatus, PhysicalAdapter, PNPDeviceID, ConfigManagerErrorCode, Speed FROM Win32_NetworkAdapter");
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    using (item)
                    {
                        if (!IsUserFacingPhysicalNetworkAdapter(item))
                        {
                            continue;
                        }

                        ushort? connectionStatus = ReadUInt16(item["NetConnectionStatus"]);
                        adapters.Add(new NetworkAdapterSpec(
                            (item["Name"] as string ?? "Unknown adapter").Trim(),
                            (item["Manufacturer"] as string)?.Trim(),
                            (item["NetConnectionID"] as string)?.Trim(),
                            (item["MACAddress"] as string)?.Trim(),
                            ReadNetworkAdapterSpeed(item["Speed"]),
                            GetNetworkConnectionStatus(connectionStatus),
                            connectionStatus == 2));
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[Specs] Unable to read network adapter information via WMI.");
            }

            return adapters
                .OrderByDescending(adapter => adapter.IsConnected)
                .ThenBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsUserFacingPhysicalNetworkAdapter(ManagementBaseObject adapter)
        {
            if (adapter["PhysicalAdapter"] is not bool isPhysical || !isPhysical ||
                ReadUInt32(adapter["ConfigManagerErrorCode"]) == 22)
            {
                return false;
            }

            string name = (adapter["Name"] as string ?? string.Empty).Trim();
            string manufacturer = (adapter["Manufacturer"] as string ?? string.Empty).Trim();
            string pnpDeviceId = (adapter["PNPDeviceID"] as string ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            string identifyingText = string.Join(" ", name, manufacturer, pnpDeviceId);
            string[] excludedTerms =
            {
                "Bluetooth",
                "Microsoft",
                "WAN Miniport",
                "Kernel Debug",
                "Virtual",
                "Hyper-V",
                "Teredo",
                "6to4",
                "ISATAP",
                "Loopback",
                "NdisWan",
                "TAP-Windows",
                "WireGuard",
                "Wintun"
            };

            return !excludedTerms.Any(term =>
                identifyingText.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetNetworkConnectionStatus(ushort? status) => status switch
        {
            0 => "Disconnected",
            1 => "Connecting",
            2 => "Connected",
            3 => "Disconnecting",
            4 => "Hardware not present",
            5 => "Hardware disabled",
            6 => "Hardware malfunction",
            7 => "Media disconnected",
            8 => "Authenticating",
            9 => "Authentication succeeded",
            10 => "Authentication failed",
            11 => "Invalid address",
            12 => "Credentials required",
            _ => "Inactive"
        };

        private static string? GetMemoryTechnology(uint? smbiosMemoryType) => smbiosMemoryType switch
        {
            18 => "DDR1",
            19 => "DDR2",
            20 => "DDR2 FB-DIMM",
            24 => "DDR3",
            26 => "DDR4",
            34 => "DDR5",
            35 => "LPDDR5",
            36 => "LPDDR5X",
            _ => null // CPU-Z provides a fallback for future values, including DDR6.
        };

        private static bool IsMemoryStick(ushort? formFactor) => formFactor is 7 or 8 or 11 or 12 or 13 or 15;
        private static uint? ReadPositiveUInt32(object? value)
        {
            if (value is null)
            {
                return null;
            }

            uint parsed = Convert.ToUInt32(value);
            return parsed == 0 ? null : parsed;
        }

        private static ushort? ReadUInt16(object? value) =>
            value is null ? null : Convert.ToUInt16(value);

        private static uint? ReadUInt32(object? value) =>
            value is null ? null : Convert.ToUInt32(value);

        private static ulong? ReadPositiveUInt64(object? value)
        {
            if (value is null)
            {
                return null;
            }

            ulong parsed = Convert.ToUInt64(value);
            return parsed == 0 ? null : parsed;
        }
        private static ulong? ReadNetworkAdapterSpeed(object? value)
        {
            ulong? speed = ReadPositiveUInt64(value);
            return speed is null or >= (ulong)long.MaxValue ? null : speed;
        }
        /// <summary>
        /// Reads the fuller firmware-provided motherboard inventory only after the user opens
        /// the Motherboard expander. Empty physical sockets are shown only when firmware reports
        /// them; Windows does not expose a dependable inventory for every unpopulated NVMe socket.
        /// </summary>
        public static IReadOnlyList<MotherboardDetail> GetMotherboardDetails()
        {
            List<MotherboardDetail> details = new();
            CpuZMainboardDetails? cpuZMainboard = CpuZMemoryReportService.GetCurrentMainboardDetails();
            AddBaseboardDetails(details, cpuZMainboard);
            AddCpuZMainboardDetails(details, cpuZMainboard);
            AddFirmwareDetails(details);
            AddProcessorSocketDetails(details, CpuZMemoryReportService.GetCurrentProcessorDetails()?.Socket);
            AddMemorySlotDetails(details);
            AddExpansionSlotDetails(details);
            AddNvmeDeviceDetails(details);
            AddChassisDetails(details);
            return details;
        }

        private static void AddBaseboardDetails(ICollection<MotherboardDetail> details, CpuZMainboardDetails? cpuZMainboard)
        {
            try
            {
                using ManagementObjectSearcher searcher = new(
                    "SELECT Manufacturer, Product, Version, SerialNumber, Tag FROM Win32_BaseBoard");
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    using (item)
                    {
                        AddDetail(details, "Board manufacturer", ReadText(item, "Manufacturer"));
                        AddDetail(details, "Board model", cpuZMainboard?.Model ?? ReadText(item, "Product"));
                        AddDetail(details, "Board revision", ReadText(item, "Version"));
                        AddDetail(details, "Board serial number", ReadText(item, "SerialNumber"));
                        AddDetail(details, "Board asset tag", ReadText(item, "Tag"));
                        return;
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[Specs] Unable to read detailed motherboard information via WMI.");
            }
        }

        private static void AddCpuZMainboardDetails(ICollection<MotherboardDetail> details, CpuZMainboardDetails? cpuZMainboard)
        {
            if (cpuZMainboard is null)
            {
                return;
            }

            AddDetail(details, "Northbridge", cpuZMainboard.Northbridge);
            AddDetail(details, "Southbridge", cpuZMainboard.Southbridge);
            AddDetail(details, "Mainboard bus", cpuZMainboard.BusSpecification);
            AddDetail(details, "Graphics interface", cpuZMainboard.GraphicsInterface);
            AddDetail(
                details,
                "LPCIO",
                string.Join(" \u00B7 ", new[] { cpuZMainboard.LpcioVendor, cpuZMainboard.LpcioModel }
                    .Where(value => !string.IsNullOrWhiteSpace(value))));
        }
        private static void AddFirmwareDetails(ICollection<MotherboardDetail> details)
        {
            try
            {
                using ManagementObjectSearcher searcher = new(
                    "SELECT Manufacturer, SMBIOSBIOSVersion, Version, ReleaseDate, SMBIOSMajorVersion, SMBIOSMinorVersion FROM Win32_BIOS");
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    using (item)
                    {
                        AddDetail(details, "BIOS manufacturer", ReadText(item, "Manufacturer"));
                        AddDetail(
                            details,
                            "BIOS version",
                            ReadText(item, "SMBIOSBIOSVersion") ?? ReadText(item, "Version"));
                        AddDetail(details, "BIOS release date", FormatWmiDate(ReadText(item, "ReleaseDate")));

                        uint? major = ReadPositiveUInt32(item["SMBIOSMajorVersion"]);
                        uint? minor = ReadPositiveUInt32(item["SMBIOSMinorVersion"]);
                        if (major.HasValue)
                        {
                            AddDetail(
                                details,
                                "SMBIOS version",
                                minor.HasValue
                                    ? $"{major.Value}.{minor.Value}"
                                    : major.Value.ToString(CultureInfo.InvariantCulture));
                        }

                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[Specs] Unable to read BIOS information via WMI.");
            }

            AddDetail(details, "Firmware mode", GetFirmwareMode());
        }

        private static void AddProcessorSocketDetails(ICollection<MotherboardDetail> details, string? preferredSocket)
        {
            if (!string.IsNullOrWhiteSpace(preferredSocket))
            {
                AddDetail(details, "CPU socket", preferredSocket);
                return;
            }

            try
            {
                using ManagementObjectSearcher searcher = new("SELECT SocketDesignation FROM Win32_Processor");
                string sockets = string.Join(
                    ", ",
                    searcher.Get()
                        .Cast<ManagementBaseObject>()
                        .Select(item =>
                        {
                            using (item)
                            {
                                return ReadText(item, "SocketDesignation");
                            }
                        })
                        .Where(socket => !string.IsNullOrWhiteSpace(socket))
                        .Distinct(StringComparer.OrdinalIgnoreCase));
                AddDetail(details, "CPU socket", sockets);
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[Specs] Unable to read CPU socket information via WMI.");
            }
        }

        private static void AddMemorySlotDetails(ICollection<MotherboardDetail> details)
        {
            int? slotCount = null;
            try
            {
                using ManagementObjectSearcher arraySearcher = new("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray");
                int reportedSlotCount = 0;
                foreach (ManagementBaseObject item in arraySearcher.Get())
                {
                    using (item)
                    {
                        uint? memoryDevices = ReadPositiveUInt32(item["MemoryDevices"]);
                        if (memoryDevices.HasValue)
                        {
                            reportedSlotCount += checked((int)memoryDevices.Value);
                        }
                    }
                }

                if (reportedSlotCount > 0)
                {
                    slotCount = reportedSlotCount;
                }
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[Specs] Unable to read memory slot count via WMI.");
            }

            List<string> populatedSlots = new();
            try
            {
                using ManagementObjectSearcher memorySearcher = new(
                    "SELECT DeviceLocator, BankLabel, Capacity, SMBIOSMemoryType, ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory");
                int unnamedModuleNumber = 0;
                foreach (ManagementBaseObject item in memorySearcher.Get())
                {
                    using (item)
                    {
                        string locator = ReadText(item, "DeviceLocator") ?? $"Memory module {++unnamedModuleNumber}";
                        string? bank = ReadText(item, "BankLabel");
                        ulong capacity = item["Capacity"] is object rawCapacity ? Convert.ToUInt64(rawCapacity) : 0UL;
                        uint? speed = ReadPositiveUInt32(item["ConfiguredClockSpeed"]) ?? ReadPositiveUInt32(item["Speed"]);
                        string? memoryType = GetMemoryTechnology(ReadUInt32(item["SMBIOSMemoryType"]));

                        List<string> parts = new() { locator };
                        if (!string.IsNullOrWhiteSpace(bank) && !string.Equals(bank, locator, StringComparison.OrdinalIgnoreCase))
                        {
                            parts.Add(bank!);
                        }

                        if (capacity > 0)
                        {
                            parts.Add(FormatGigabytes(capacity));
                        }

                        if (!string.IsNullOrWhiteSpace(memoryType))
                        {
                            parts.Add(memoryType!);
                        }

                        if (speed.HasValue)
                        {
                            parts.Add($"{speed.Value:N0} MT/s");
                        }

                        populatedSlots.Add(string.Join(" \u00B7 ", parts));
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[Specs] Unable to read populated memory slots via WMI.");
            }

            if (slotCount.HasValue || populatedSlots.Count > 0)
            {
                string summary = slotCount.HasValue
                    ? $"{populatedSlots.Count} / {slotCount.Value} populated"
                    : $"{populatedSlots.Count} populated";
                AddDetail(details, "Memory slots", summary);
            }

            foreach (string populatedSlot in populatedSlots.OrderBy(slot => slot, StringComparer.OrdinalIgnoreCase))
            {
                AddDetail(details, "Memory slot", populatedSlot);
            }
        }

        private static void AddExpansionSlotDetails(ICollection<MotherboardDetail> details)
        {
            List<(string Label, string Value, bool InUse)> pcieSlots = new();
            List<(string Label, string Value, bool InUse)> m2Slots = new();
            try
            {
                using ManagementObjectSearcher searcher = new(
                    "SELECT SlotDesignation, SlotType, CurrentUsage, MaxDataWidth FROM Win32_SystemSlot");
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    using (item)
                    {
                        string designation = ReadText(item, "SlotDesignation") ?? "Unnamed slot";
                        ushort? slotType = ReadUInt16(item["SlotType"]);
                        ushort? maxDataWidth = ReadUInt16(item["MaxDataWidth"]);
                        ushort? currentUsage = ReadUInt16(item["CurrentUsage"]);
                        string? pcieType = GetPcieSlotType(slotType);
                        bool isM2 = designation.Contains("M.2", StringComparison.OrdinalIgnoreCase)
                            || designation.Contains("M2", StringComparison.OrdinalIgnoreCase)
                            || designation.Contains("NVME", StringComparison.OrdinalIgnoreCase);
                        bool isPcie = pcieType is not null
                            || designation.Contains("PCI", StringComparison.OrdinalIgnoreCase);
                        if (!isM2 && !isPcie)
                        {
                            continue;
                        }

                        string interfaceText = pcieType ?? "PCI Express";
                        if (!interfaceText.Contains(" x", StringComparison.OrdinalIgnoreCase)
                            && maxDataWidth is > 0)
                        {
                            interfaceText += $" x{maxDataWidth.Value}";
                        }

                        string value = string.Join(" \u00B7 ", new[]
                        {
                            designation,
                            interfaceText,
                            GetSlotUsage(currentUsage)
                        });
                        (isM2 ? m2Slots : pcieSlots).Add((isM2 ? "M.2 slot" : "PCIe slot", value, currentUsage == 4));
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[Specs] Unable to read expansion slots via WMI.");
            }

            foreach ((string label, string value, _) in pcieSlots
                .OrderByDescending(slot => slot.InUse)
                .ThenBy(slot => slot.Value, StringComparer.OrdinalIgnoreCase))
            {
                AddDetail(details, label, value);
            }

            foreach ((string label, string value, _) in m2Slots
                .OrderByDescending(slot => slot.InUse)
                .ThenBy(slot => slot.Value, StringComparer.OrdinalIgnoreCase))
            {
                AddDetail(details, label, value);
            }
        }

        private static void AddNvmeDeviceDetails(ICollection<MotherboardDetail> details)
        {
            try
            {
                using ManagementObjectSearcher searcher = new(
                    "SELECT Model, FirmwareRevision, PNPDeviceID, InterfaceType FROM Win32_DiskDrive");
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    using (item)
                    {
                        string? model = ReadText(item, "Model");
                        string? pnpDeviceId = ReadText(item, "PNPDeviceID");
                        string? interfaceType = ReadText(item, "InterfaceType");
                        bool isNvme = (pnpDeviceId?.Contains("NVME", StringComparison.OrdinalIgnoreCase) ?? false)
                            || (model?.Contains("NVME", StringComparison.OrdinalIgnoreCase) ?? false)
                            || string.Equals(interfaceType, "NVMe", StringComparison.OrdinalIgnoreCase);
                        if (!isNvme)
                        {
                            continue;
                        }

                        List<string> parts = new() { model ?? "Unknown NVMe drive" };
                        string? firmware = ReadText(item, "FirmwareRevision");
                        if (!string.IsNullOrWhiteSpace(firmware))
                        {
                            parts.Add($"Firmware {firmware}");
                        }

                        AddDetail(details, "NVMe drive", string.Join(" \u00B7 ", parts));
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[Specs] Unable to read NVMe storage information via WMI.");
            }
        }

        private static void AddChassisDetails(ICollection<MotherboardDetail> details)
        {
            try
            {
                using ManagementObjectSearcher searcher = new("SELECT ChassisTypes FROM Win32_SystemEnclosure");
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    using (item)
                    {
                        if (item["ChassisTypes"] is not Array chassisTypes || chassisTypes.Length == 0)
                        {
                            continue;
                        }

                        ushort chassisType = Convert.ToUInt16(chassisTypes.GetValue(0), CultureInfo.InvariantCulture);
                        AddDetail(details, "System form factor", GetChassisType(chassisType));
                        return;
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[Specs] Unable to read chassis information via WMI.");
            }
        }

        private static void AddDetail(ICollection<MotherboardDetail> details, string label, string? value)
        {
            string? cleanValue = CleanText(value);
            if (!string.IsNullOrWhiteSpace(cleanValue))
            {
                details.Add(new MotherboardDetail(label, cleanValue));
            }
        }

        private static string? ReadText(ManagementBaseObject item, string propertyName) => CleanText(item[propertyName]);

        private static string? CleanText(object? rawValue)
        {
            string value = Convert.ToString(rawValue, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value)
                || string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "None", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "N/A", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Not Specified", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Default string", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "System Serial Number", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("To Be Filled By O.E.M.", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return value;
        }

        private static string? FormatWmiDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length < 8)
            {
                return null;
            }

            return DateTime.TryParseExact(
                value[..8],
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime releaseDate)
                ? releaseDate.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)
                : value;
        }

        private static string? GetFirmwareMode()
        {
            try
            {
                using RegistryKey? controlKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control");
                return ReadUInt32(controlKey?.GetValue("PEFirmwareType")) switch
                {
                    1 => "Legacy BIOS",
                    2 => "UEFI",
                    _ => null
                };
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[Specs] Unable to read firmware mode from the registry.");
                return null;
            }
        }

        private static string GetSlotUsage(ushort? currentUsage) => currentUsage switch
        {
            3 => "Available",
            4 => "In use",
            5 => "Unavailable",
            _ => "Usage not reported"
        };

        private static string? GetPcieSlotType(ushort? slotType)
        {
            if (slotType is >= 165 and <= 170)
            {
                return FormatPcieSlotType(null, slotType.Value - 165);
            }

            if (slotType is >= 171 and <= 200)
            {
                int offset = slotType.Value - 171;
                return FormatPcieSlotType(2 + offset / 6, offset % 6);
            }

            return null;
        }

        private static string FormatPcieSlotType(int? generation, int widthIndex)
        {
            int[] widths = { 0, 1, 2, 4, 8, 16 };
            string value = generation.HasValue ? $"PCIe Gen {generation.Value}" : "PCI Express";
            return widths[widthIndex] == 0 ? value : $"{value} x{widths[widthIndex]}";
        }

        private static string? GetChassisType(ushort chassisType) => chassisType switch
        {
            3 => "Desktop",
            4 => "Low-profile desktop",
            5 => "Pizza-box desktop",
            6 => "Mini tower",
            7 => "Tower",
            8 => "Portable",
            9 => "Laptop",
            10 => "Notebook",
            14 => "Sub-notebook",
            30 => "Tablet",
            31 => "Convertible",
            32 => "Detachable",
            _ => null
        };

        private static string FormatGigabytes(ulong bytes)
        {
            const double gigabyte = 1024d * 1024 * 1024;
            return (bytes / gigabyte).ToString("0.##", CultureInfo.InvariantCulture) + " GB";
        }
        private static MotherboardSpec? GetMotherboard()
        {
            try
            {
                using ManagementObjectSearcher searcher = new("SELECT Manufacturer, Product FROM Win32_BaseBoard");
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    using (item)
                    {
                        string? manufacturer = (item["Manufacturer"] as string)?.Trim();
                        string? product = (item["Product"] as string)?.Trim();
                        return new MotherboardSpec(manufacturer, product);
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[Specs] Unable to read motherboard information via WMI.");
            }

            return null;
        }
    }
}
