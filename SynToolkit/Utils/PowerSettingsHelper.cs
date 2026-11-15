#nullable enable

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace SynToolkit.Utils
{
    /// <summary>
    /// Reads and writes the active Windows power scheme through PowrProf.dll.
    /// These APIs are locale-independent and avoid parsing powercfg output.
    /// </summary>
    public static class PowerSettingsHelper
    {
        private const uint ERROR_SUCCESS = 0;
        private const uint ERROR_FILE_NOT_FOUND = 2;
        private const uint ERROR_MORE_DATA = 234;
        private const uint ERROR_INSUFFICIENT_BUFFER = 122;

        public static (uint AcValue, uint DcValue) ReadCurrentValues(Guid subgroup, Guid setting)
        {
            Guid scheme = GetActiveScheme();
            uint acResult = PowerReadACValueIndex(
                IntPtr.Zero,
                ref scheme,
                ref subgroup,
                ref setting,
                out uint acValue);
            ThrowIfFailed(acResult, "read the AC power setting");

            uint dcResult = PowerReadDCValueIndex(
                IntPtr.Zero,
                ref scheme,
                ref subgroup,
                ref setting,
                out uint dcValue);
            ThrowIfFailed(dcResult, "read the DC power setting");

            return (acValue, dcValue);
        }

        public static void WriteCurrentValues(Guid subgroup, Guid setting, uint acValue, uint dcValue)
        {
            Guid scheme = GetActiveScheme();
            (uint originalAcValue, uint originalDcValue) = ReadValues(scheme, subgroup, setting);

            try
            {
                WriteValues(scheme, subgroup, setting, acValue, dcValue);
                ActivateScheme(scheme);

                (uint actualAcValue, uint actualDcValue) = ReadValues(scheme, subgroup, setting);
                if (actualAcValue != acValue || actualDcValue != dcValue)
                {
                    throw new InvalidOperationException("Windows did not retain both requested power-setting values.");
                }
            }
            catch (Exception writeException)
            {
                try
                {
                    WriteValues(scheme, subgroup, setting, originalAcValue, originalDcValue);
                    ActivateScheme(scheme);
                }
                catch (Exception rollbackException)
                {
                    throw new InvalidOperationException(
                        "Unable to update the power setting, and Windows could not restore its previous values.",
                        new AggregateException(writeException, rollbackException));
                }

                throw;
            }
        }

        private static (uint AcValue, uint DcValue) ReadValues(Guid scheme, Guid subgroup, Guid setting)
        {
            uint acResult = PowerReadACValueIndex(
                IntPtr.Zero,
                ref scheme,
                ref subgroup,
                ref setting,
                out uint acValue);
            ThrowIfFailed(acResult, "read the AC power setting");

            uint dcResult = PowerReadDCValueIndex(
                IntPtr.Zero,
                ref scheme,
                ref subgroup,
                ref setting,
                out uint dcValue);
            ThrowIfFailed(dcResult, "read the DC power setting");

            return (acValue, dcValue);
        }

        private static void WriteValues(
            Guid scheme,
            Guid subgroup,
            Guid setting,
            uint acValue,
            uint dcValue)
        {
            uint acResult = PowerWriteACValueIndex(
                IntPtr.Zero,
                ref scheme,
                ref subgroup,
                ref setting,
                acValue);
            ThrowIfFailed(acResult, "write the AC power setting");

            uint dcResult = PowerWriteDCValueIndex(
                IntPtr.Zero,
                ref scheme,
                ref subgroup,
                ref setting,
                dcValue);
            ThrowIfFailed(dcResult, "write the DC power setting");
        }

        private static void ActivateScheme(Guid scheme)
        {
            uint activateResult = PowerSetActiveScheme(IntPtr.Zero, ref scheme);
            ThrowIfFailed(activateResult, "activate the updated power scheme");
        }

        public static Guid GetActiveScheme()
        {
            uint result = PowerGetActiveScheme(IntPtr.Zero, out IntPtr schemePointer);
            ThrowIfFailed(result, "read the active power scheme");

            if (schemePointer == IntPtr.Zero)
            {
                throw new InvalidOperationException("Windows returned an empty active power scheme.");
            }

            try
            {
                return Marshal.PtrToStructure<Guid>(schemePointer);
            }
            finally
            {
                _ = LocalFree(schemePointer);
            }
        }

        /// <summary>
        /// Reads the active scheme GUID plus its resolved friendly name and description.
        /// Resource-string names (e.g. <c>@%SystemRoot%\system32\powrprof.dll,-...</c>) are returned as localized text.
        /// </summary>
        public static (Guid SchemeId, string Name, string Description) GetActivePlanInfo()
        {
            Guid schemeId = GetActiveScheme();
            return (schemeId, ReadSchemeFriendlyName(schemeId), ReadSchemeDescription(schemeId));
        }

        /// <summary>
        /// Writes the friendly name and description for a power scheme.
        /// Does not call <c>PowerSetActiveScheme</c>; name/description changes persist without reactivation.
        /// </summary>
        public static void WritePlanIdentity(Guid schemeId, string name, string description)
        {
            WriteSchemeFriendlyName(schemeId, name);
            WriteSchemeDescription(schemeId, description);
        }

        /// <summary>
        /// Reads the friendly name and description for a power scheme GUID.
        /// </summary>
        public static (string Name, string Description) ReadPlanIdentity(Guid schemeId) =>
            (ReadSchemeFriendlyName(schemeId), ReadSchemeDescription(schemeId));

        private static string ReadSchemeFriendlyName(Guid schemeId) =>
            ReadSchemeText(schemeId, friendlyName: true, "read the power plan name");

        private static string ReadSchemeDescription(Guid schemeId) =>
            ReadSchemeText(schemeId, friendlyName: false, "read the power plan description");

        private static string ReadSchemeText(Guid schemeId, bool friendlyName, string operation)
        {
            Guid scheme = schemeId;
            uint byteCount = 0;
            uint sizeResult = friendlyName
                ? PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, null, ref byteCount)
                : PowerReadDescription(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, null, ref byteCount);

            // Descriptions are often absent on custom schemes; treat as empty rather than failing the read.
            if (sizeResult == ERROR_FILE_NOT_FOUND || byteCount < 2)
            {
                return string.Empty;
            }

            if (sizeResult != ERROR_SUCCESS &&
                sizeResult != ERROR_MORE_DATA &&
                sizeResult != ERROR_INSUFFICIENT_BUFFER)
            {
                ThrowIfFailed(sizeResult, operation);
            }

            if (byteCount > 65536)
            {
                throw new InvalidOperationException($"Unable to {operation}: Windows reported an oversized buffer.");
            }

            byte[] buffer = new byte[byteCount];
            uint readResult = friendlyName
                ? PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref byteCount)
                : PowerReadDescription(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref byteCount);
            ThrowIfFailed(readResult, operation);

            return Encoding.Unicode.GetString(buffer, 0, (int)Math.Min(byteCount, (uint)buffer.Length))
                .TrimEnd('\0')
                .Trim();
        }

        private static void WriteSchemeFriendlyName(Guid schemeId, string name) =>
            WriteSchemeText(schemeId, name, friendlyName: true, "write the power plan name");

        private static void WriteSchemeDescription(Guid schemeId, string description) =>
            WriteSchemeText(schemeId, description, friendlyName: false, "write the power plan description");

        private static void WriteSchemeText(Guid schemeId, string text, bool friendlyName, string operation)
        {
            Guid scheme = schemeId;
            byte[] buffer = Encoding.Unicode.GetBytes(text + '\0');
            uint byteCount = (uint)buffer.Length;
            uint result = friendlyName
                ? PowerWriteFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, byteCount)
                : PowerWriteDescription(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, byteCount);
            ThrowIfFailed(result, operation);
        }

        private static void ThrowIfFailed(uint result, string operation)
        {
            if (result != ERROR_SUCCESS)
            {
                throw new Win32Exception(unchecked((int)result), $"Unable to {operation}.");
            }
        }

        [DllImport("PowrProf.dll", SetLastError = true)]
        private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

        [DllImport("PowrProf.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint PowerReadFriendlyName(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            IntPtr subgroupOfPowerSettingsGuid,
            IntPtr powerSettingGuid,
            byte[]? buffer,
            ref uint bufferSize);

        [DllImport("PowrProf.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint PowerReadDescription(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            IntPtr subgroupOfPowerSettingsGuid,
            IntPtr powerSettingGuid,
            byte[]? buffer,
            ref uint bufferSize);

        [DllImport("PowrProf.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint PowerWriteFriendlyName(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            IntPtr subgroupOfPowerSettingsGuid,
            IntPtr powerSettingGuid,
            byte[] buffer,
            uint bufferSize);

        [DllImport("PowrProf.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint PowerWriteDescription(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            IntPtr subgroupOfPowerSettingsGuid,
            IntPtr powerSettingGuid,
            byte[] buffer,
            uint bufferSize);

        [DllImport("PowrProf.dll", SetLastError = true)]
        private static extern uint PowerReadACValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subgroupGuid,
            ref Guid powerSettingGuid,
            out uint acValueIndex);

        [DllImport("PowrProf.dll", SetLastError = true)]
        private static extern uint PowerReadDCValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subgroupGuid,
            ref Guid powerSettingGuid,
            out uint dcValueIndex);

        [DllImport("PowrProf.dll", SetLastError = true)]
        private static extern uint PowerWriteACValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subgroupGuid,
            ref Guid powerSettingGuid,
            uint acValueIndex);

        [DllImport("PowrProf.dll", SetLastError = true)]
        private static extern uint PowerWriteDCValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subgroupGuid,
            ref Guid powerSettingGuid,
            uint dcValueIndex);

        [DllImport("PowrProf.dll", SetLastError = true)]
        private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);
    }
}
