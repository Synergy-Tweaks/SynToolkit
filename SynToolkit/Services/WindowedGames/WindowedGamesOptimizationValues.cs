#nullable enable

using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SynToolkit.Services.WindowedGames
{
    /// <summary>
    /// Detection/write helpers for DirectXUserGlobalSettings SwapEffectUpgradeEnable
    /// (Optimizations for windowed games). Preserves every other Key=Value pair.
    /// </summary>
    public static class WindowedGamesOptimizationValues
    {
        public const string KeyPath = @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences";
        public const string ValueName = "DirectXUserGlobalSettings";
        public const string SwapEffectPairKey = "SwapEffectUpgradeEnable";

        public enum DetectionKind
        {
            On,
            Off,
            MissingPair,
            MissingString,
            Unsupported,
            Error,
        }

        public sealed record DetectionResult(
            DetectionKind Kind,
            string? RawString,
            bool? SwapEffectEnabled,
            string DisplayLabel,
            string? Warning);

        public static DetectionResult DetectCurrentState(bool readSucceeded, object? registryValue)
        {
            if (!readSucceeded)
            {
                return new DetectionResult(
                    DetectionKind.Error,
                    null,
                    null,
                    "Unknown",
                    "Couldn't read the current value.");
            }

            if (registryValue is null)
            {
                return new DetectionResult(
                    DetectionKind.MissingString,
                    null,
                    null,
                    "Off",
                    null);
            }

            if (registryValue is not string text)
            {
                return new DetectionResult(
                    DetectionKind.Unsupported,
                    Convert.ToString(registryValue, CultureInfo.InvariantCulture),
                    null,
                    "Other",
                    "DirectXUserGlobalSettings is not a REG_SZ this tweak would write.");
            }

            if (!TryParsePairs(text, out Dictionary<string, string> pairs, out bool malformed))
            {
                return new DetectionResult(
                    DetectionKind.Unsupported,
                    text,
                    null,
                    "Other",
                    malformed
                        ? "DirectXUserGlobalSettings is malformed; pick On/Off to rewrite the SwapEffect pair."
                        : "DirectXUserGlobalSettings could not be parsed.");
            }

            if (!pairs.TryGetValue(SwapEffectPairKey, out string? rawPairValue))
            {
                return new DetectionResult(
                    DetectionKind.MissingPair,
                    text,
                    null,
                    "Off",
                    null);
            }

            if (rawPairValue == "1")
            {
                return new DetectionResult(DetectionKind.On, text, true, "On", null);
            }

            if (rawPairValue == "0")
            {
                return new DetectionResult(DetectionKind.Off, text, false, "Off", null);
            }

            return new DetectionResult(
                DetectionKind.Unsupported,
                text,
                null,
                $"Other ({rawPairValue})",
                $"SwapEffectUpgradeEnable={rawPairValue} is not a value this tweak would write.");
        }

        /// <summary>
        /// Sets or updates only SwapEffectUpgradeEnable; preserves every other pair.
        /// </summary>
        public static string BuildUpdatedString(string? existing, bool enable)
        {
            Dictionary<string, string> pairs = new(StringComparer.OrdinalIgnoreCase);
            List<string> order = new();

            if (!string.IsNullOrEmpty(existing)
                && TryParsePairs(existing, out Dictionary<string, string> parsed, out _))
            {
                foreach (KeyValuePair<string, string> pair in parsed)
                {
                    pairs[pair.Key] = pair.Value;
                    order.Add(pair.Key);
                }
            }

            string target = enable ? "1" : "0";
            if (!pairs.ContainsKey(SwapEffectPairKey))
            {
                order.Add(SwapEffectPairKey);
            }

            pairs[SwapEffectPairKey] = target;

            StringBuilder builder = new();
            foreach (string key in order)
            {
                if (!pairs.TryGetValue(key, out string? value))
                {
                    continue;
                }

                builder.Append(key);
                builder.Append('=');
                builder.Append(value);
                builder.Append(';');
            }

            return builder.ToString();
        }

        public static bool TryParsePairs(
            string text,
            out Dictionary<string, string> pairs,
            out bool malformed)
        {
            pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            malformed = false;

            try
            {
                string[] segments = text.Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (string segment in segments)
                {
                    string trimmed = segment.Trim();
                    if (trimmed.Length == 0)
                    {
                        continue;
                    }

                    int eq = trimmed.IndexOf('=');
                    if (eq <= 0)
                    {
                        malformed = true;
                        continue;
                    }

                    string key = trimmed[..eq].Trim();
                    string value = trimmed[(eq + 1)..].Trim();
                    if (key.Length == 0)
                    {
                        malformed = true;
                        continue;
                    }

                    pairs[key] = value;
                }

                return true;
            }
            catch
            {
                malformed = true;
                pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                return false;
            }
        }
    }
}
