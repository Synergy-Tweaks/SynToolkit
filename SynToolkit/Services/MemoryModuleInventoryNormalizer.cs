#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using SynToolkit.Models;

namespace SynToolkit.Services
{
    /// <summary>
    /// Normalizes firmware memory inventories for the Specs display. Some laptops expose each
    /// soldered DRAM chip as a separate unidentified Win32_PhysicalMemory entry rather than a
    /// module; those entries retain their individual capacity while receiving a useful onboard-memory label.
    /// </summary>
    internal static class MemoryModuleInventoryNormalizer
    {
        private const int MinimumFragmentCount = 4;
        private const ulong Megabyte = 1024UL * 1024UL;

        internal static IReadOnlyList<MemoryModuleSpec> Normalize(
            IEnumerable<MemoryModuleSpec> memoryModules,
            ulong totalMemoryBytes)
        {
            List<MemoryModuleSpec> modules = memoryModules.ToList();
            if (ShouldLabelAsOnboardMemoryChips(modules, totalMemoryBytes))
            {
                modules = modules
                    .Select(module => module with { Manufacturer = "Onboard Memory Chip" })
                    .ToList();
            }

            int nextSlotNumber = 1;
            return modules
                .Select(module => module.IsMemoryStick
                    ? module with { SlotLabel = $"Slot {nextSlotNumber++}" }
                    : module)
                .ToList();
        }

        private static bool ShouldLabelAsOnboardMemoryChips(
            IReadOnlyList<MemoryModuleSpec> modules,
            ulong totalMemoryBytes)
        {
            if (totalMemoryBytes == 0 || modules.Count < MinimumFragmentCount ||
                modules.Any(module => module.CapacityBytes == 0 || module.SpeedMHz is null ||
                                      !IsUnidentifiedManufacturer(module.Manufacturer)))
            {
                return false;
            }

            if (modules.Select(module => module.CapacityBytes).Distinct().Skip(1).Any() ||
                modules.Select(module => module.SpeedMHz).Distinct().Skip(1).Any())
            {
                return false;
            }

            ulong combinedCapacity = modules.Aggregate(0UL, (total, module) => total + module.CapacityBytes);
            ulong difference = combinedCapacity >= totalMemoryBytes
                ? combinedCapacity - totalMemoryBytes
                : totalMemoryBytes - combinedCapacity;
            ulong tolerance = Math.Max(512UL * Megabyte, totalMemoryBytes / 20);
            return difference <= tolerance;
        }

        private static bool IsUnidentifiedManufacturer(string? manufacturer)
        {
            return string.IsNullOrWhiteSpace(manufacturer) ||
                   manufacturer.Trim().Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
                   manufacturer.Trim().Equals("Not specified", StringComparison.OrdinalIgnoreCase) ||
                   manufacturer.Trim().Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase);
        }
    }
}