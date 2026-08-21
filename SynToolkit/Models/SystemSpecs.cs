#nullable enable

namespace SynToolkit.Models
{
    public sealed record CpuSpec(string Name, int Cores, int LogicalProcessors, uint MaxClockSpeedMHz);

    public sealed record GpuSpec(string Name, ulong? AdapterRamBytes, string? DriverVersion, string IconPath);

    public sealed record MemoryModuleSpec(
        string? Manufacturer,
        ulong CapacityBytes,
        uint? SpeedMHz,
        string? MemoryType = null,
        string? SlotLabel = null,
        string? TimingText = null,
        bool IsMemoryStick = false);

    public sealed record StorageDriveSpec(string Model, ulong SizeBytes, string? MediaType, string? InterfaceType);

    public sealed record NetworkAdapterSpec(
        string Name,
        string? Manufacturer,
        string? ConnectionName,
        string? MacAddress,
        ulong? SpeedBitsPerSecond,
        string ConnectionStatus,
        bool IsConnected);

    public sealed record MotherboardSpec(string? Manufacturer, string? Product);

    public sealed record MotherboardDetail(string Label, string Value);

    public sealed record SystemSpecsSnapshot(
        CpuSpec? Cpu,
        System.Collections.Generic.IReadOnlyList<GpuSpec> Gpus,
        ulong TotalMemoryBytes,
        System.Collections.Generic.IReadOnlyList<MemoryModuleSpec> MemoryModules,
        System.Collections.Generic.IReadOnlyList<StorageDriveSpec> StorageDrives,
        System.Collections.Generic.IReadOnlyList<NetworkAdapterSpec> NetworkAdapters,
        MotherboardSpec? Motherboard,
        string WindowsProductName,
        string WindowsDisplayVersion,
        string WindowsBuild,
        string Architecture);
}