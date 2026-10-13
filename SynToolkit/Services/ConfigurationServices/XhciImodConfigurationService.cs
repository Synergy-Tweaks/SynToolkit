using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SynToolkit.Stores;
using SynToolkit.Utils;

namespace SynToolkit.Services.ConfigurationServices;

/// <summary>
/// Configuration service for XHCI Interrupt Moderation (IMOD).
/// 
/// Toggle ON = IMOD is ENABLED (Windows default)
/// Toggle OFF = IMOD is DISABLED (recommended for high polling rate mice/keyboards)
/// </summary>
internal class XhciImodConfigurationService : IConfigurationService
{
    private const string STORE_KEY = @"HKLM\SOFTWARE\SynToolkit\XhciImod";
    private readonly ConfigurationStore _store;

    public XhciImodConfigurationService(
        [FromKeyedServices("XhciImod")] ConfigurationStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Returns true if IMOD is ENABLED (Windows default)
    /// </summary>
    public bool IsEnabled()
    {
        try
        {
            // Return true if IMOD is enabled (NOT disabled)
            return !XhciDeviceHelper.IsAnyImodDisabled();
        }
        catch (Exception ex)
        {
            App.logger.Error($"[XhciImod] Error checking IMOD state: {ex.Message}");
            
            // Fallback to registry state
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\SynToolkit\XhciImod");
            if (key != null)
            {
                var state = key.GetValue("GlobalState");
                // GlobalState 1 = IMOD enabled, 0 = IMOD disabled
                return state == null || Convert.ToInt32(state) == 1;
            }
            return true; // Default to enabled
        }
    }

    /// <summary>
    /// Enables IMOD on all XHCI controllers (Windows default)
    /// </summary>
    public void Enable()
    {
        try
        {
            XhciDeviceHelper.EnableAllImod();
            
            // Save global state (1 = IMOD enabled)
            using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\SynToolkit\XhciImod");
            key?.SetValue("GlobalState", 1, RegistryValueKind.DWord);
            
            _store.CurrentSetting = true;
            App.logger.Info("[XhciImod] IMOD enabled on all XHCI controllers (default)");
        }
        catch (Exception ex)
        {
            App.logger.Error($"[XhciImod] Failed to enable IMOD: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Disables IMOD on all XHCI controllers (performance optimization)
    /// </summary>
    public void Disable()
    {
        try
        {
            XhciDeviceHelper.DisableAllImod();
            
            // Save global state (0 = IMOD disabled)
            using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\SynToolkit\XhciImod");
            key?.SetValue("GlobalState", 0, RegistryValueKind.DWord);
            
            _store.CurrentSetting = false;
            App.logger.Info("[XhciImod] IMOD disabled on all XHCI controllers");
        }
        catch (Exception ex)
        {
            App.logger.Error($"[XhciImod] Failed to disable IMOD: {ex.Message}");
            throw;
        }
    }
}
