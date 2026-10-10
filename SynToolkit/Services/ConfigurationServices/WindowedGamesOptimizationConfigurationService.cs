#nullable enable

using SynToolkit.Services.WindowedGames;
using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// Optimizations for windowed games via DirectXUserGlobalSettings
    /// SwapEffectUpgradeEnable under HKCU\Software\Microsoft\DirectX\UserGpuPreferences.
    /// Only that pair is rewritten; every other Key=Value pair is preserved.
    /// </summary>
    public class WindowedGamesOptimizationConfigurationService : IConfigurationService
    {
        private readonly ConfigurationStore _windowedGamesOptimizationStore;

        public WindowedGamesOptimizationConfigurationService(
            [FromKeyedServices("WindowedGamesOptimization")] ConfigurationStore windowedGamesOptimizationStore)
        {
            _windowedGamesOptimizationStore = windowedGamesOptimizationStore;
        }

        public void Disable() => Apply(enable: false);

        public void Enable() => Apply(enable: true);

        public bool IsEnabled()
        {
            WindowedGamesOptimizationValues.DetectionResult result = DetectCurrentState();
            if (result.Kind is WindowedGamesOptimizationValues.DetectionKind.Unsupported
                or WindowedGamesOptimizationValues.DetectionKind.Error)
            {
                throw new NotSupportedException(result.Warning ?? result.DisplayLabel);
            }

            return result.Kind == WindowedGamesOptimizationValues.DetectionKind.On;
        }

        public WindowedGamesOptimizationValues.DetectionResult DetectCurrentState()
        {
            bool readSucceeded = RegistryHelper.TryReadValueWithKind(
                WindowedGamesOptimizationValues.KeyPath,
                WindowedGamesOptimizationValues.ValueName,
                out object? raw,
                out _);
            return WindowedGamesOptimizationValues.DetectCurrentState(readSucceeded, raw);
        }

        private void Apply(bool enable)
        {
            WindowedGamesOptimizationValues.DetectionResult before = DetectCurrentState();
            if (before.Kind is WindowedGamesOptimizationValues.DetectionKind.Error)
            {
                throw new InvalidOperationException(before.Warning ?? "Couldn't read DirectXUserGlobalSettings.");
            }

            if (before.Kind == WindowedGamesOptimizationValues.DetectionKind.On && enable)
            {
                _windowedGamesOptimizationStore.CurrentSetting = true;
                return;
            }

            if (!enable
                && before.Kind is WindowedGamesOptimizationValues.DetectionKind.Off
                    or WindowedGamesOptimizationValues.DetectionKind.MissingPair
                    or WindowedGamesOptimizationValues.DetectionKind.MissingString)
            {
                _windowedGamesOptimizationStore.CurrentSetting = false;
                return;
            }

            string existing = before.RawString ?? string.Empty;
            string updated = WindowedGamesOptimizationValues.BuildUpdatedString(
                string.IsNullOrEmpty(existing) ? null : existing,
                enable);

            RegistryHelper.SetValue(
                WindowedGamesOptimizationValues.KeyPath,
                WindowedGamesOptimizationValues.ValueName,
                updated,
                RegistryValueKind.String);

            WindowedGamesOptimizationValues.DetectionResult after = DetectCurrentState();
            bool ok = enable
                ? after.Kind == WindowedGamesOptimizationValues.DetectionKind.On
                : after.Kind == WindowedGamesOptimizationValues.DetectionKind.Off;

            _windowedGamesOptimizationStore.CurrentSetting = after.Kind == WindowedGamesOptimizationValues.DetectionKind.On;
            if (!ok)
            {
                throw new InvalidOperationException(
                    "DirectXUserGlobalSettings write could not be verified.");
            }
        }
    }
}
