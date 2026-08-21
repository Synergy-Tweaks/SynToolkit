#nullable enable

using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using SynToolkit.Models;
using SynToolkit.Services;

namespace SynToolkit.ViewModels
{
    public sealed class GpuSpecDisplay
    {
        public GpuSpecDisplay(string name, string vramText, string driverVersionText, string iconPath)
        {
            Name = name;
            VramText = vramText;
            DriverVersionText = driverVersionText;
            IconPath = iconPath;
        }

        public string Name { get; }
        public string VramText { get; }
        public string DriverVersionText { get; }
        public string IconPath { get; }
    }

    public sealed record MemoryModuleDisplay(string ManufacturerText, string CapacityText);

    public sealed record StorageDriveDisplay(string Model, string SizeText, string TypeText);

    public sealed record NetworkAdapterDisplay(string Name, string ManufacturerText, string StatusText, string DetailsText);

    public sealed record CpuDetailDisplay(string Label, string Value);
    public sealed record MotherboardDetailDisplay(string Label, string Value);

    /// <summary>
    /// Drives the Specs tab: a read-only snapshot of CPU, GPU, memory, storage, motherboard,
    /// and Windows identity via SystemSpecsService. Purely informational — makes no changes.
    /// </summary>
    public partial class SpecsPageViewModel : ObservableObject
    {
        private readonly ISystemInformationService _systemInformationService;
        private readonly CpuUsageSampler _cpuUsageSampler = new();
        private decimal? _minimumObservedCpuFrequencyMHz;
        private decimal? _maximumObservedCpuFrequencyMHz;
        private bool _areMotherboardDetailsLoaded;
        private bool _areMotherboardDetailsLoading;

        [ObservableProperty]
        public partial bool IsLoading { get; set; } = true;

        [ObservableProperty]
        public partial bool HasError { get; set; }

        [ObservableProperty]
        public partial string ErrorMessage { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string CpuName { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string CpuDetailsText { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string CpuUtilizationText { get; set; } = "Calculating...";

        [ObservableProperty]
        public partial string CpuCurrentFrequencyText { get; set; } = "Detecting...";

        [ObservableProperty]
        public partial string CpuObservedFrequencyText { get; set; } = "Collecting...";

        [ObservableProperty]
        public partial string MotherboardText { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string TotalMemoryText { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string MemoryDescriptionText { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string WindowsText { get; set; } = string.Empty;

        private string _networkSummaryText = string.Empty;

        public string NetworkSummaryText
        {
            get => _networkSummaryText;
            private set => SetProperty(ref _networkSummaryText, value);
        }

        [ObservableProperty]
        public partial string GraphicsHeaderIcon { get; set; } = GpuDetectionService.DefaultGpuIconPath;

        public ObservableCollection<CpuDetailDisplay> CpuDetails { get; } = new();
        public ObservableCollection<MotherboardDetailDisplay> MotherboardDetails { get; } = new();
        public ObservableCollection<GpuSpecDisplay> Gpus { get; } = new();
        public ObservableCollection<MemoryModuleDisplay> MemoryModules { get; } = new();
        public ObservableCollection<StorageDriveDisplay> StorageDrives { get; } = new();
        public ObservableCollection<NetworkAdapterDisplay> NetworkAdapters { get; } = new();

        public SpecsPageViewModel(ISystemInformationService systemInformationService)
        {
            _systemInformationService = systemInformationService;
        }

        public async Task LoadAsync()
        {
            IsLoading = true;
            HasError = false;
            try
            {
                SystemSpecsSnapshot snapshot = await Task.Run(() => SystemSpecsService.GetSnapshot(_systemInformationService));

                CpuName = snapshot.Cpu?.Name ?? "Unknown CPU";
                CpuDetailsText = CreateCpuSummary(snapshot.Cpu) + " · Loading detailed CPU information...";
                DisplayCpuDetails(snapshot.Cpu, null);

                MotherboardText = snapshot.Motherboard is null
                    ? "Unknown"
                    : string.Join(" \u00B7 ", new[] { snapshot.Motherboard.Manufacturer, snapshot.Motherboard.Product }.Where(part => !string.IsNullOrWhiteSpace(part)));

                TotalMemoryText = FormatBytes(snapshot.TotalMemoryBytes);
                MemoryDescriptionText = TotalMemoryText;
                WindowsText = $"{snapshot.WindowsProductName} ({snapshot.WindowsDisplayVersion}, Build {snapshot.WindowsBuild}, {snapshot.Architecture})";

                Gpus.Clear();
                foreach (GpuSpec gpu in snapshot.Gpus)
                {
                    Gpus.Add(new GpuSpecDisplay(
                        gpu.Name,
                        gpu.AdapterRamBytes.HasValue ? FormatBytes(gpu.AdapterRamBytes.Value) : "Unknown",
                        string.IsNullOrWhiteSpace(gpu.DriverVersion) ? "Unknown driver version" : $"Driver {gpu.DriverVersion}",
                        gpu.IconPath));
                }

                GraphicsHeaderIcon = GpuDetectionService.GetPrimaryIconPath(snapshot.Gpus);

                DisplayMemoryModules(snapshot.MemoryModules);

                NetworkAdapters.Clear();
                foreach (NetworkAdapterSpec adapter in snapshot.NetworkAdapters)
                {
                    List<string> details = new();
                    if (!string.IsNullOrWhiteSpace(adapter.ConnectionName))
                    {
                        details.Add(adapter.ConnectionName!);
                    }

                    if (adapter.IsConnected && adapter.SpeedBitsPerSecond.HasValue)
                    {
                        details.Add(FormatNetworkSpeed(adapter.SpeedBitsPerSecond.Value));
                    }

                    if (!string.IsNullOrWhiteSpace(adapter.MacAddress))
                    {
                        details.Add($"MAC {adapter.MacAddress}");
                    }

                    NetworkAdapters.Add(new NetworkAdapterDisplay(
                        adapter.Name,
                        string.IsNullOrWhiteSpace(adapter.Manufacturer) ? "Unknown manufacturer" : adapter.Manufacturer!,
                        adapter.ConnectionStatus,
                        details.Count == 0 ? "Physical network adapter" : string.Join(" · ", details)));
                }

                int activeAdapterCount = snapshot.NetworkAdapters.Count(adapter => adapter.IsConnected);
                NetworkSummaryText = snapshot.NetworkAdapters.Count == 0
                    ? "No physical network adapters detected"
                    : $"{snapshot.NetworkAdapters.Count} adapter(s), {activeAdapterCount} active";
                StorageDrives.Clear();
                foreach (StorageDriveSpec drive in snapshot.StorageDrives)
                {
                    string typeText = string.Join(" / ", new[] { drive.MediaType, drive.InterfaceType }.Where(part => !string.IsNullOrWhiteSpace(part)));
                    StorageDrives.Add(new StorageDriveDisplay(drive.Model, FormatBytes(drive.SizeBytes), typeText));
                }

                MemoryDescriptionText = $"{TotalMemoryText} · Loading CAS Latency & timings...";
                _ = LoadMemoryTimingDetailsAsync(snapshot.MemoryModules);
                _ = LoadCpuDetailsAsync(snapshot.Cpu);
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Specs] Unable to load system specs.");
                ErrorMessage = exception.Message;
                HasError = true;
            }
            finally
            {
                IsLoading = false;
            }
        }


        public async Task LoadMotherboardDetailsAsync()
        {
            if (_areMotherboardDetailsLoaded || _areMotherboardDetailsLoading)
            {
                return;
            }

            _areMotherboardDetailsLoading = true;
            MotherboardDetails.Clear();
            MotherboardDetails.Add(new MotherboardDetailDisplay("Motherboard details", "Loading..."));
            try
            {
                IReadOnlyList<MotherboardDetail> details = await Task.Run(SystemSpecsService.GetMotherboardDetails);
                MotherboardDetails.Clear();
                if (details.Count == 0)
                {
                    MotherboardDetails.Add(new MotherboardDetailDisplay(
                        "Motherboard details",
                        "Detailed firmware information is unavailable on this system."));
                    return;
                }

                foreach (MotherboardDetail detail in details)
                {
                    MotherboardDetails.Add(new MotherboardDetailDisplay(detail.Label, detail.Value));
                }
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[Specs] Motherboard details were unavailable.");
                MotherboardDetails.Clear();
                MotherboardDetails.Add(new MotherboardDetailDisplay(
                    "Motherboard details",
                    "Detailed firmware information is unavailable on this system."));
            }
            finally
            {
                _areMotherboardDetailsLoading = false;
                _areMotherboardDetailsLoaded = true;
            }
        }
        public void RefreshCpuLiveMetrics()
        {
            CpuLiveMetrics metrics = _cpuUsageSampler.Sample();
            if (metrics.UtilizationPercent.HasValue)
            {
                CpuUtilizationText = $"{metrics.UtilizationPercent.Value}%";
            }

            if (!metrics.AverageFrequencyMHz.HasValue)
            {
                return;
            }

            decimal frequencyMHz = metrics.AverageFrequencyMHz.Value;
            CpuCurrentFrequencyText = FormatLiveCpuFrequency(frequencyMHz);
            _minimumObservedCpuFrequencyMHz = !_minimumObservedCpuFrequencyMHz.HasValue
                ? frequencyMHz
                : Math.Min(_minimumObservedCpuFrequencyMHz.Value, frequencyMHz);
            _maximumObservedCpuFrequencyMHz = !_maximumObservedCpuFrequencyMHz.HasValue
                ? frequencyMHz
                : Math.Max(_maximumObservedCpuFrequencyMHz.Value, frequencyMHz);
            CpuObservedFrequencyText = _minimumObservedCpuFrequencyMHz == _maximumObservedCpuFrequencyMHz
                ? FormatLiveCpuFrequency(_minimumObservedCpuFrequencyMHz.Value)
                : $"{FormatLiveCpuFrequency(_minimumObservedCpuFrequencyMHz.Value)} - {FormatLiveCpuFrequency(_maximumObservedCpuFrequencyMHz.Value)}";
        }

        private async Task LoadCpuDetailsAsync(CpuSpec? cpu)
        {
            try
            {
                CpuZProcessorDetails? details = await Task.Run(CpuZMemoryReportService.GetCurrentProcessorDetails);
                if (details is not null)
                {
                    DisplayCpuDetails(cpu, details);
                    CpuDetailsText = CreateCpuSummary(cpu, details);
                    return;
                }
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[Specs] CPU-Z processor report was unavailable.");
            }

            CpuDetailsText = CreateCpuSummary(cpu);
        }

        private void DisplayCpuDetails(CpuSpec? cpu, CpuZProcessorDetails? details)
        {
            CpuDetails.Clear();
            if (cpu is null)
            {
                return;
            }

            AddCpuDetail("Cores", cpu.Cores.ToString(CultureInfo.InvariantCulture));
            AddCpuDetail("Threads", cpu.LogicalProcessors.ToString(CultureInfo.InvariantCulture));
            if (details is null)
            {
                if (cpu.MaxClockSpeedMHz > 0)
                {
                    AddCpuDetail("Reported maximum frequency", FormatCpuFrequency(cpu.MaxClockSpeedMHz));
                }

                AddCpuDetail("CPU details", "Loading from CPU-Z...");
                return;
            }

            if (details.MinimumFrequencyMHz.HasValue && details.MaximumFrequencyMHz.HasValue)
            {
                AddCpuDetail(
                    "Minimum - maximum frequency",
                    $"{FormatCpuFrequency(details.MinimumFrequencyMHz.Value)} - {FormatCpuFrequency(details.MaximumFrequencyMHz.Value)}");
            }
            else if (details.MaximumFrequencyMHz.HasValue)
            {
                AddCpuDetail("Maximum frequency", FormatCpuFrequency(details.MaximumFrequencyMHz.Value));
            }

            AddCoreSet(details.PerformanceCores);
            AddCoreSet(details.EfficientCores);
            AddCpuDetail("L1 data cache", details.L1DataCache);
            AddCpuDetail("L1 instruction cache", details.L1InstructionCache);
            AddCpuDetail("L2 cache", details.L2Cache);
            AddCpuDetail("L3 cache", details.L3Cache);
            if (details.HasAmd3dVCache)
            {
                AddCpuDetail("AMD 3D V-Cache", "Detected");
            }

            AddCpuDetail("Manufacturer", details.Manufacturer);
            AddCpuDetail("Codename", details.Codename);
            AddCpuDetail("Socket", details.Socket);
            AddCpuDetail("Process", details.Technology);
            AddCpuDetail("Thermal design power", details.ThermalDesignPower);
            AddCpuDetail("Temperature limit", details.TemperatureLimit);
            AddCpuDetail("CPUID", details.Cpuid);
            AddCpuDetail("Stepping", details.Stepping);
            AddCpuDetail("Instruction sets", details.InstructionSets);
        }

        private void AddCoreSet(CpuZCoreSet? coreSet)
        {
            if (coreSet is null)
            {
                return;
            }

            string value = $"{coreSet.Cores} cores, {coreSet.Threads} threads";
            if (coreSet.MaximumFrequencyMHz.HasValue)
            {
                value += $" · up to {FormatCpuFrequency(coreSet.MaximumFrequencyMHz.Value)}";
            }

            AddCpuDetail(coreSet.Name, value);
        }

        private void AddCpuDetail(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                CpuDetails.Add(new CpuDetailDisplay(label, value));
            }
        }

        private static string CreateCpuSummary(CpuSpec? cpu, CpuZProcessorDetails? details = null)
        {
            if (cpu is null)
            {
                return string.Empty;
            }

            List<string> summary = new()
            {
                $"{cpu.Cores} cores",
                $"{cpu.LogicalProcessors} threads"
            };
            if (details?.MinimumFrequencyMHz is decimal minimumFrequencyMHz
                && details.MaximumFrequencyMHz is decimal maximumFrequencyMHz)
            {
                summary.Add($"{FormatCpuFrequency(minimumFrequencyMHz)} - {FormatCpuFrequency(maximumFrequencyMHz)}");
            }
            else if (cpu.MaxClockSpeedMHz > 0)
            {
                summary.Add($"up to {FormatCpuFrequency(cpu.MaxClockSpeedMHz)}");
            }

            return string.Join(" · ", summary);
        }

        private static string FormatCpuFrequency(decimal megahertz) => megahertz >= 1000m
            ? (megahertz / 1000m).ToString("0.##", CultureInfo.InvariantCulture) + " GHz"
            : megahertz.ToString("0", CultureInfo.InvariantCulture) + " MHz";

        private static string FormatLiveCpuFrequency(decimal megahertz) =>
            megahertz.ToString("0", CultureInfo.InvariantCulture) + " MHz";

        private static string FormatCpuFrequency(uint megahertz) => FormatCpuFrequency((decimal)megahertz);
        private async Task LoadMemoryTimingDetailsAsync(IReadOnlyList<MemoryModuleSpec> modules)
        {
            try
            {
                IReadOnlyList<MemoryModuleSpec> modulesWithTimings = await Task.Run(
                    () => SystemSpecsService.AddCurrentMemoryTimingDetails(modules));
                DisplayMemoryModules(modulesWithTimings);
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[Specs] CPU-Z memory timing report was unavailable.");
            }
            finally
            {
                MemoryDescriptionText = TotalMemoryText;
            }
        }

        private void DisplayMemoryModules(IReadOnlyList<MemoryModuleSpec> modules)
        {
            MemoryModules.Clear();
            foreach (MemoryModuleSpec module in modules)
            {
                string manufacturer = string.IsNullOrWhiteSpace(module.Manufacturer) ? "Unknown manufacturer" : module.Manufacturer!;
                string header = string.IsNullOrWhiteSpace(module.SlotLabel)
                    ? manufacturer
                    : $"{module.SlotLabel} · {manufacturer}";
                List<string> details = new() { FormatBytes(module.CapacityBytes) };
                if (!string.IsNullOrWhiteSpace(module.MemoryType))
                {
                    details.Add(module.MemoryType!);
                }
                if (module.SpeedMHz.HasValue)
                {
                    details.Add($"{module.SpeedMHz.Value:N0} MT/s");
                }
                if (!string.IsNullOrWhiteSpace(module.TimingText))
                {
                    details.Add(module.TimingText!);
                }
                MemoryModules.Add(new MemoryModuleDisplay(header, string.Join(" · ", details)));
            }
        }
        private static string FormatNetworkSpeed(ulong bitsPerSecond)
        {
            const double gigabit = 1_000_000_000d;
            const double megabit = 1_000_000d;
            return bitsPerSecond >= (ulong)gigabit
                ? (bitsPerSecond / gigabit).ToString("0.##", CultureInfo.InvariantCulture) + " Gbps"
                : (bitsPerSecond / megabit).ToString("0.##", CultureInfo.InvariantCulture) + " Mbps";
        }
        private static string FormatBytes(ulong bytes)
        {
            if (bytes == 0)
            {
                return "Unknown";
            }

            const double gigabyte = 1024d * 1024 * 1024;
            return (bytes / gigabyte).ToString("0.##", CultureInfo.InvariantCulture) + " GB";
        }
    }
}
