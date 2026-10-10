using Microsoft.Win32;
using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SynToolkit.Utils
{
    public class RegistryHelper
    {
        public static object GetValue(string keyPath, string valueName)
        {
            TryReadValue(keyPath, valueName, out object value);
            return value;
        }

        public static bool TryReadValue(string keyPath, string valueName, out object value)
        {
            return TryReadValueWithKind(keyPath, valueName, out value, out _);
        }

        /// <summary>
        /// Reads a registry value and its kind using the same 64-bit view as writes.
        /// Returns false only for unexpected failures (e.g. access denied), not for missing keys/values.
        /// </summary>
        public static bool TryReadValueWithKind(
            string keyPath,
            string valueName,
            out object value,
            out RegistryValueKind? valueKind)
        {
            value = null;
            valueKind = null;
            try
            {
                using RegistryKey key = OpenKey(keyPath);
                if (key is null)
                {
                    return true;
                }

                object raw = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (raw is null)
                {
                    // Distinguish missing value from empty default: GetValueKind throws if absent.
                    try
                    {
                        _ = key.GetValueKind(valueName);
                    }
                    catch (IOException)
                    {
                        return true;
                    }

                    value = null;
                    return true;
                }

                value = raw;
                try
                {
                    valueKind = key.GetValueKind(valueName);
                }
                catch (IOException)
                {
                    valueKind = null;
                }

                return true;
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, $"[REGHELPER] Unable to read registry value: {keyPath}\\{valueName}");
                value = null;
                valueKind = null;
                return false;
            }
        }

        public static void SetValue(string keyPath, string valueName, object value)
        {
            using RegistryKey key = OpenKey(keyPath, true, true);
            if (key is null)
            {
                throw new InvalidOperationException($"Unable to open or create writable registry key: {keyPath}");
            }

            key.SetValue(valueName, value);
            App.logger.Info($"[REGHELPER] Set registry value: {keyPath}\\{valueName} to {value}");
        }

        public static void SetValue(string keyPath, string valueName, object value, RegistryValueKind valueKind)
        {
            using RegistryKey key = OpenKey(keyPath, true, true);
            if (key is null)
            {
                throw new InvalidOperationException($"Unable to open or create writable registry key: {keyPath}");
            }

            value = value switch
            {
                uint => BitConverter.ToInt32(BitConverter.GetBytes((uint)value)),
                ulong => BitConverter.ToInt64(BitConverter.GetBytes((ulong)value)),
                _ => value
            };

            key.SetValue(valueName, value, valueKind);
            App.logger.Info($"[REGHELPER] Set registry value: {keyPath}\\{valueName} to {value} ({valueKind})");
        }

        public static void DeleteValue(string keyPath, string valueName)
        {
            using RegistryKey key = OpenKey(keyPath, true);
            key?.DeleteValue(valueName, false);
            App.logger.Info($"[REGHELPER] Deleted registry value: {keyPath}\\{valueName}");
        }

        public static bool IsMatch(string keyPath, string valueName, object data)
        {
            if (keyPath is null)
            {
                return false;
            }

            data = data switch
            {
                uint => BitConverter.ToInt32(BitConverter.GetBytes((uint)data)),
                ulong => BitConverter.ToInt64(BitConverter.GetBytes((ulong)data)),
                _ => data
            };

            if (!TryReadValue(keyPath, valueName, out object registryData))
            {
                // Access denied/corrupt registry state is unknown, never the
                // same as a deliberately absent value.
                return false;
            }

            if (registryData is byte[] byteArrayData && data is byte[] byteArray)
            {
                return byteArrayData.SequenceEqual(byteArray);
            }

            if (TryConvertInteger(registryData, out decimal registryNumber)
                && TryConvertInteger(data, out decimal expectedNumber))
            {
                return registryNumber == expectedNumber;
            }

            return registryData?.Equals(data) ?? data is null;
        }

        private static RegistryKey OpenKey(string keyPath, bool writable = false, bool createIfMissing = false)
        {
            string[] split = keyPath.Split('\\');

            RegistryHive hive = split[0] switch
            {
                "HKCR" or "HKEY_CLASSES_ROOT" => RegistryHive.ClassesRoot,
                "HKCU" or "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
                "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
                "HKU" or "HKEY_USERS" => RegistryHive.Users,
                _ => throw new Exception("Hive was not found")
            };

            string keyName = string.Join('\\', split[1..]);

            RegistryView view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);

            return writable && createIfMissing
                ? baseKey.CreateSubKey(keyName, writable: true)
                : baseKey.OpenSubKey(keyName, writable);
        }

        public static void DeleteKey(string keyPath)
        {
            string[] split = keyPath.Split('\\');

            string parentKeyPath = string.Join('\\', split[..^1]);
            string targetKeyName = split[^1];

            using RegistryKey key = OpenKey(parentKeyPath, true);
            key?.DeleteSubKeyTree(targetKeyName, false);
            App.logger.Info($"[REGHELPER] Deleted registry key: {keyPath}");
        }
        public static bool KeyExists(string keyPath)
        {
            try
            {
                using RegistryKey key = OpenKey(keyPath);
                return key is not null;
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, $"[REGHELPER] Unable to inspect registry key: {keyPath}");
                return false;
            }
        }

        private static bool TryConvertInteger(object value, out decimal number)
        {
            switch (value)
            {
                case byte byteValue:
                    number = byteValue;
                    return true;
                case sbyte signedByteValue:
                    number = signedByteValue;
                    return true;
                case short shortValue:
                    number = shortValue;
                    return true;
                case ushort unsignedShortValue:
                    number = unsignedShortValue;
                    return true;
                case int intValue:
                    number = intValue;
                    return true;
                case uint unsignedIntValue:
                    number = unsignedIntValue;
                    return true;
                case long longValue:
                    number = longValue;
                    return true;
                case ulong unsignedLongValue:
                    number = unsignedLongValue;
                    return true;
                case string text when decimal.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out decimal parsed):
                    number = parsed;
                    return true;
                default:
                    number = 0;
                    return false;
            }
        }

        public static void MergeRegFile(string regFilePath)
        {
            if (string.IsNullOrWhiteSpace(regFilePath) || !System.IO.File.Exists(regFilePath))
            {
                throw new System.IO.FileNotFoundException("The registry payload is missing.", regFilePath);
            }

            CommandResult result = CommandPromptHelper.RunProcessResult(
                "reg.exe",
                ["import", regFilePath],
                timeoutMilliseconds: 30_000);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Unable to import registry payload: {result.CombinedOutput}");
            }

            App.logger.Info($"[REGHELPER] Merged registry file: \"{regFilePath}\"");
        }

}
}
