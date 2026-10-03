#nullable enable

using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace SynToolkit.Services
{
    public sealed record PowerPlanSettingInspection(
        Guid SubgroupId,
        Guid SettingId,
        string GroupName,
        string Name,
        string Description,
        uint? AcIndex,
        uint? DcIndex,
        string AcText,
        string DcText,
        uint? CurrentAcIndex,
        uint? CurrentDcIndex,
        string CurrentAcText,
        string CurrentDcText)
    {
        public bool IsDifferent => AcIndex != CurrentAcIndex || DcIndex != CurrentDcIndex;
    }

    public sealed record PowerPlanInspection(
        string Name,
        string Description,
        IReadOnlyList<PowerPlanSettingInspection> Settings);

    /// <summary>
    /// Reads a .pow export as a private registry hive. Opening the viewer does not
    /// import, install, or activate the scheme. Windows power APIs provide the same
    /// localized setting names and value labels used by Power Settings Explorer.
    /// </summary>
    public static class PowerPlanSettingsReader
    {
        private const uint KeyRead = 0x20019;
        private const uint ErrorSuccess = 0;
        private const uint ErrorMoreData = 234;
        private const uint ErrorInsufficientBuffer = 122;
        private const string SosResourceName = "SynToolkit.PowerPlans.SOS.pow";
        private static readonly Guid NoSubgroupId = new("fea3413e-7e05-4911-9a71-700331f1c294");

        public static PowerPlanInspection ReadFile(
            string filePath,
            Guid? currentSchemeId,
            CancellationToken cancellationToken)
        {
            string fullPath = Path.GetFullPath(filePath);
            if (!string.Equals(Path.GetExtension(fullPath), ".pow", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Choose a Windows .pow power-plan file.");
            }

            FileInfo file = new(fullPath);
            if (!file.Exists || file.Length < 4 || file.Length > PowerPlanService.MaximumPlanFileBytes)
            {
                throw new InvalidDataException("The power-plan file is missing, empty, or larger than 64 MB.");
            }

            using (FileStream header = file.OpenRead())
            {
                Span<byte> signature = stackalloc byte[4];
                if (header.Read(signature) != 4 ||
                    signature[0] != (byte)'r' || signature[1] != (byte)'e' ||
                    signature[2] != (byte)'g' || signature[3] != (byte)'f')
                {
                    throw new InvalidDataException("This file is not a Windows power-plan export.");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            int result = RegLoadAppKey(fullPath, out SafeRegistryHandle handle, KeyRead, 0, 0);
            if (result != 0)
            {
                throw new Win32Exception(result, "Windows could not read this power-plan file.");
            }

            using RegistryKey hive = RegistryKey.FromHandle(handle);
            string name = (hive.GetValue("FriendlyName") as string)?.Trim() ?? Path.GetFileNameWithoutExtension(fullPath);
            string description = (hive.GetValue("Description") as string)?.Trim() ?? string.Empty;
            var settings = new List<PowerPlanSettingInspection>();

            foreach (string keyName in hive.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Guid.TryParse(keyName, out Guid subgroupId))
                {
                    continue;
                }

                using RegistryKey? groupKey = hive.OpenSubKey(keyName, writable: false);
                if (groupKey is null)
                {
                    continue;
                }

                // Some exported settings (for example, plan personality) live
                // directly under the hive root rather than in a subgroup.
                if (HasValueIndex(groupKey))
                {
                    settings.Add(ReadSetting(groupKey, NoSubgroupId, subgroupId,
                        "Plan properties", currentSchemeId));
                }

                string groupName = ReadNativeText(NativeTextKind.GroupName, subgroupId, Guid.Empty)
                    ?? subgroupId.ToString("D");
                foreach (string settingKeyName in groupKey.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!Guid.TryParse(settingKeyName, out Guid settingId))
                    {
                        continue;
                    }

                    using RegistryKey? settingKey = groupKey.OpenSubKey(settingKeyName, writable: false);
                    if (settingKey is not null && HasValueIndex(settingKey))
                    {
                        settings.Add(ReadSetting(settingKey, subgroupId, settingId,
                            groupName, currentSchemeId));
                    }
                }
            }

            if (settings.Count == 0)
            {
                throw new InvalidDataException("No power settings were found in this .pow file.");
            }

            return new PowerPlanInspection(name, description, settings
                .OrderBy(setting => GetGroupOrder(setting.GroupName))
                .ThenBy(setting => setting.GroupName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(setting => setting.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray());
        }

        private static int GetGroupOrder(string name)
        {
            if (name.Equals("Plan properties", StringComparison.OrdinalIgnoreCase)) return 0;
            if (name.Contains("Processor", StringComparison.OrdinalIgnoreCase)) return 1;
            if (name.Contains("Display", StringComparison.OrdinalIgnoreCase)) return 2;
            if (name.Contains("Sleep", StringComparison.OrdinalIgnoreCase)) return 3;
            if (name.Contains("Hard disk", StringComparison.OrdinalIgnoreCase)) return 4;
            if (name.Contains("PCI Express", StringComparison.OrdinalIgnoreCase)) return 5;
            if (name.Contains("USB", StringComparison.OrdinalIgnoreCase)) return 6;
            if (name.Contains("Wireless", StringComparison.OrdinalIgnoreCase)) return 7;
            if (name.Contains("Multimedia", StringComparison.OrdinalIgnoreCase)) return 8;
            if (name.Contains("Battery", StringComparison.OrdinalIgnoreCase)) return 9;
            return 10;
        }

        public static PowerPlanInspection ReadBuiltIn(Guid? currentSchemeId, CancellationToken cancellationToken)
        {
            using Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(SosResourceName)
                ?? throw new FileNotFoundException("The bundled SOS.pow resource is missing.");
            string tempPath = Path.Combine(Path.GetTempPath(), $"SynToolkit-Inspect-{Guid.NewGuid():N}.pow");
            try
            {
                using (FileStream destination = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    resource.CopyTo(destination);
                }
                return ReadFile(tempPath, currentSchemeId, cancellationToken);
            }
            finally
            {
                try { File.Delete(tempPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static bool HasValueIndex(RegistryKey key) =>
            key.GetValue("ACSettingIndex") is not null || key.GetValue("DCSettingIndex") is not null;

        private static PowerPlanSettingInspection ReadSetting(
            RegistryKey key,
            Guid subgroupId,
            Guid settingId,
            string groupName,
            Guid? currentSchemeId)
        {
            uint? ac = ReadIndex(key, "ACSettingIndex");
            uint? dc = ReadIndex(key, "DCSettingIndex");
            string name = ReadNativeText(NativeTextKind.SettingName, subgroupId, settingId)
                ?? settingId.ToString("D");
            string description = ReadNativeText(NativeTextKind.Description, subgroupId, settingId)
                ?? string.Empty;
            string units = ReadNativeText(NativeTextKind.Units, subgroupId, settingId) ?? string.Empty;
            bool isRanged = IsRangedSetting(subgroupId, settingId);
            uint? currentAc = currentSchemeId is Guid schemeId
                ? ReadCurrentIndex(schemeId, subgroupId, settingId, onAc: true)
                : null;
            uint? currentDc = currentSchemeId is Guid currentId
                ? ReadCurrentIndex(currentId, subgroupId, settingId, onAc: false)
                : null;

            return new PowerPlanSettingInspection(
                subgroupId, settingId, groupName, name, description,
                ac, dc, FormatValue(subgroupId, settingId, ac, units, isRanged, name),
                FormatValue(subgroupId, settingId, dc, units, isRanged, name),
                currentAc, currentDc,
                FormatValue(subgroupId, settingId, currentAc, units, isRanged, name),
                FormatValue(subgroupId, settingId, currentDc, units, isRanged, name));
        }

        private static uint? ReadIndex(RegistryKey key, string name) => key.GetValue(name) switch
        {
            int value => unchecked((uint)value),
            long value when value >= 0 && value <= uint.MaxValue => (uint)value,
            byte[] bytes when bytes.Length >= sizeof(uint) => BitConverter.ToUInt32(bytes, 0),
            _ => null
        };

        private static uint? ReadCurrentIndex(Guid schemeId, Guid subgroupId, Guid settingId, bool onAc)
        {
            uint value = 0;
            uint result = onAc
                ? PowerReadACValueIndex(IntPtr.Zero, ref schemeId, ref subgroupId, ref settingId, ref value)
                : PowerReadDCValueIndex(IntPtr.Zero, ref schemeId, ref subgroupId, ref settingId, ref value);
            return result == ErrorSuccess ? value : null;
        }

        private static bool IsRangedSetting(Guid subgroupId, Guid settingId)
        {
            uint min = 0, max = 0, increment = 0;
            return PowerReadValueMin(IntPtr.Zero, ref subgroupId, ref settingId, ref min) == ErrorSuccess &&
                PowerReadValueMax(IntPtr.Zero, ref subgroupId, ref settingId, ref max) == ErrorSuccess &&
                PowerReadValueIncrement(IntPtr.Zero, ref subgroupId, ref settingId, ref increment) == ErrorSuccess;
        }

        private static string FormatValue(
            Guid subgroupId, Guid settingId, uint? value, string units, bool isRanged, string settingName)
        {
            if (value is not uint index)
            {
                return "—";
            }

            string? label = !isRanged && index <= int.MaxValue
                ? ReadNativeText(NativeTextKind.PossibleValue, subgroupId, settingId, (int)index)
                : null;
            if (!string.IsNullOrWhiteSpace(label) &&
                !string.Equals(label, settingName, StringComparison.OrdinalIgnoreCase))
            {
                return $"{label} ({index})";
            }

            return string.IsNullOrWhiteSpace(units) ? index.ToString() : $"{index} {units}";
        }

        private enum NativeTextKind { GroupName, SettingName, Description, Units, PossibleValue }

        private static string? ReadNativeText(
            NativeTextKind kind, Guid subgroupId, Guid settingId, int possibleIndex = 0)
        {
            byte[] buffer = new byte[512];
            uint byteCount = (uint)buffer.Length;
            uint result = CallNativeText(kind, ref subgroupId, ref settingId,
                possibleIndex, buffer, ref byteCount);
            if ((result == ErrorMoreData || result == ErrorInsufficientBuffer) &&
                byteCount > buffer.Length && byteCount <= 65536)
            {
                buffer = new byte[byteCount];
                result = CallNativeText(kind, ref subgroupId, ref settingId,
                    possibleIndex, buffer, ref byteCount);
            }

            if (result != ErrorSuccess || byteCount < 2)
            {
                return null;
            }

            string value = Encoding.Unicode.GetString(buffer, 0,
                (int)Math.Min(byteCount, (uint)buffer.Length)).TrimEnd('\0').Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static uint CallNativeText(
            NativeTextKind kind,
            ref Guid subgroupId,
            ref Guid settingId,
            int possibleIndex,
            byte[] buffer,
            ref uint byteCount) => kind switch
        {
            NativeTextKind.GroupName => PowerReadGroupName(IntPtr.Zero, IntPtr.Zero,
                ref subgroupId, IntPtr.Zero, buffer, ref byteCount),
            NativeTextKind.SettingName => PowerReadSettingName(IntPtr.Zero, IntPtr.Zero,
                ref subgroupId, ref settingId, buffer, ref byteCount),
            NativeTextKind.Description => PowerReadDescription(IntPtr.Zero, IntPtr.Zero,
                ref subgroupId, ref settingId, buffer, ref byteCount),
            NativeTextKind.Units => PowerReadValueUnitsSpecifier(IntPtr.Zero,
                ref subgroupId, ref settingId, buffer, ref byteCount),
            NativeTextKind.PossibleValue => PowerReadPossibleFriendlyName(IntPtr.Zero,
                ref subgroupId, ref settingId, possibleIndex, buffer, ref byteCount),
            _ => 1
        };

        [DllImport("advapi32.dll", EntryPoint = "RegLoadAppKeyW", CharSet = CharSet.Unicode)]
        private static extern int RegLoadAppKey(
            string file, out SafeRegistryHandle key, uint access, uint options, uint reserved);

        [DllImport("powrprof.dll", EntryPoint = "PowerReadFriendlyName", CharSet = CharSet.Unicode)]
        private static extern uint PowerReadGroupName(
            IntPtr root, IntPtr scheme, ref Guid subgroup, IntPtr setting,
            byte[] buffer, ref uint byteCount);

        [DllImport("powrprof.dll", EntryPoint = "PowerReadFriendlyName", CharSet = CharSet.Unicode)]
        private static extern uint PowerReadSettingName(
            IntPtr root, IntPtr scheme, ref Guid subgroup, ref Guid setting,
            byte[] buffer, ref uint byteCount);

        [DllImport("powrprof.dll", EntryPoint = "PowerReadDescription", CharSet = CharSet.Unicode)]
        private static extern uint PowerReadDescription(
            IntPtr root, IntPtr scheme, ref Guid subgroup, ref Guid setting,
            byte[] buffer, ref uint byteCount);

        [DllImport("powrprof.dll", EntryPoint = "PowerReadValueUnitsSpecifier", CharSet = CharSet.Unicode)]
        private static extern uint PowerReadValueUnitsSpecifier(
            IntPtr root, ref Guid subgroup, ref Guid setting,
            byte[] buffer, ref uint byteCount);

        [DllImport("powrprof.dll", EntryPoint = "PowerReadPossibleFriendlyName", CharSet = CharSet.Unicode)]
        private static extern uint PowerReadPossibleFriendlyName(
            IntPtr root, ref Guid subgroup, ref Guid setting, int index,
            byte[] buffer, ref uint byteCount);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadValueMin(
            IntPtr root, ref Guid subgroup, ref Guid setting, ref uint value);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadValueMax(
            IntPtr root, ref Guid subgroup, ref Guid setting, ref uint value);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadValueIncrement(
            IntPtr root, ref Guid subgroup, ref Guid setting, ref uint value);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadACValueIndex(
            IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, ref uint value);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadDCValueIndex(
            IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, ref uint value);
    }
}
