#nullable enable

using SynToolkit.Services.Mpo;
using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// Multi-Plane Overlay (MPO). Disable writes OverlayTestMode=5; Enable deletes the value
    /// (Windows default = MPO enabled). Requires signing out.
    /// </summary>
    internal class MultiPlaneOverlayConfigurationService : IConfigurationService
    {
        private const string SYNTOOLKIT_STORE_KEY_NAME = @"HKLM\SOFTWARE\SynToolkit\Services\MultiPlaneOverlay";
        private const string STATE_VALUE_NAME = "state";

        private readonly ConfigurationStore _multiPlaneOverlayConfigurationStore;

        public MultiPlaneOverlayConfigurationService(
            [FromKeyedServices("MultiPlaneOverlay")] ConfigurationStore multiPlaneOverlayConfigurationStore)
        {
            _multiPlaneOverlayConfigurationStore = multiPlaneOverlayConfigurationStore;
        }

        public void Disable()
        {
            MultiPlaneOverlayValues.DetectionResult before = DetectCurrentState();
            if (before.Kind == MultiPlaneOverlayValues.DetectionKind.Disabled)
            {
                _multiPlaneOverlayConfigurationStore.CurrentSetting = false;
                return;
            }

            if (before.Kind is MultiPlaneOverlayValues.DetectionKind.Unsupported
                or MultiPlaneOverlayValues.DetectionKind.Error)
            {
                throw new NotSupportedException(before.Warning ?? before.DisplayLabel);
            }

            RegistryHelper.SetValue(
                MultiPlaneOverlayValues.DwmKeyPath,
                MultiPlaneOverlayValues.OverlayTestModeValueName,
                unchecked((int)MultiPlaneOverlayValues.DisableMpoValue),
                RegistryValueKind.DWord);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 0);

            MultiPlaneOverlayValues.DetectionResult after = DetectCurrentState();
            _multiPlaneOverlayConfigurationStore.CurrentSetting = after.Kind == MultiPlaneOverlayValues.DetectionKind.Enabled;
            if (after.Kind != MultiPlaneOverlayValues.DetectionKind.Disabled)
            {
                throw new InvalidOperationException(
                    "OverlayTestMode write could not be verified.");
            }

            App.ContentDialogCaller("logoff");
        }

        public void Enable()
        {
            MultiPlaneOverlayValues.DetectionResult before = DetectCurrentState();
            if (before.Kind == MultiPlaneOverlayValues.DetectionKind.Enabled
                && before.RawValue is null)
            {
                _multiPlaneOverlayConfigurationStore.CurrentSetting = true;
                return;
            }

            if (before.Kind is MultiPlaneOverlayValues.DetectionKind.Unsupported
                or MultiPlaneOverlayValues.DetectionKind.Error)
            {
                throw new NotSupportedException(before.Warning ?? before.DisplayLabel);
            }

            // Restoring default means deleting the value, not writing another number.
            RegistryHelper.DeleteValue(
                MultiPlaneOverlayValues.DwmKeyPath,
                MultiPlaneOverlayValues.OverlayTestModeValueName);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 1);

            MultiPlaneOverlayValues.DetectionResult after = DetectCurrentState();
            _multiPlaneOverlayConfigurationStore.CurrentSetting = after.Kind == MultiPlaneOverlayValues.DetectionKind.Enabled;
            if (after.Kind != MultiPlaneOverlayValues.DetectionKind.Enabled || after.RawValue is not null)
            {
                throw new InvalidOperationException(
                    "OverlayTestMode delete could not be verified.");
            }

            App.ContentDialogCaller("logoff");
        }

        public bool IsEnabled()
        {
            MultiPlaneOverlayValues.DetectionResult result = DetectCurrentState();
            if (result.Kind is MultiPlaneOverlayValues.DetectionKind.Unsupported
                or MultiPlaneOverlayValues.DetectionKind.Error)
            {
                throw new NotSupportedException(result.Warning ?? result.DisplayLabel);
            }

            return result.Kind == MultiPlaneOverlayValues.DetectionKind.Enabled;
        }

        public MultiPlaneOverlayValues.DetectionResult DetectCurrentState()
        {
            bool readSucceeded = RegistryHelper.TryReadValueWithKind(
                MultiPlaneOverlayValues.DwmKeyPath,
                MultiPlaneOverlayValues.OverlayTestModeValueName,
                out object? raw,
                out RegistryValueKind? kind);
            return MultiPlaneOverlayValues.DetectCurrentState(readSucceeded, raw, kind);
        }
    }
}
