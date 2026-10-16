using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    public class HideAppBrowserControlConfigurationService : IConfigurationService
    {
        private const string SYNTOOLKIT_STORE_KEY_NAME = @"HKLM\SOFTWARE\SynToolkit\Services\HideAppBrowserControl";
        private const string STATE_VALUE_NAME = "state";

        private const string APP_BROWSER_PROTECTION_KEY_NAME = @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender Security Center\App and Browser protection";

        private readonly ConfigurationStore _hideAppBrowserControlConfigurationService;

        public HideAppBrowserControlConfigurationService(
            [FromKeyedServices("HideAppBrowserControl")] ConfigurationStore hideAppBrowserControlConfigurationService)
        {
            _hideAppBrowserControlConfigurationService = hideAppBrowserControlConfigurationService;
        }

        public void Disable()
        {
            RegistryHelper.SetValue(APP_BROWSER_PROTECTION_KEY_NAME, "UILockdown", 1, RegistryValueKind.DWord);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 0);

            _hideAppBrowserControlConfigurationService.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.DeleteValue(APP_BROWSER_PROTECTION_KEY_NAME, "UILockdown");
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 1);

            _hideAppBrowserControlConfigurationService.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            try
            {
                string securityHealthUiPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "SystemApps",
                    "Microsoft.Windows.SecHealthUI_cw5n1h2txyewy",
                    "SecHealthUI.exe");
                object value = RegistryHelper.GetValue(APP_BROWSER_PROTECTION_KEY_NAME, "UILockdown");
                // UILockdown=1 hides the page. Missing or zero leaves it visible.
                return System.IO.File.Exists(securityHealthUiPath)
                    && !(value is int hidden && hidden != 0);
            }
            catch (Exception exception)
            {
                App.logger.Warn($"Unable to detect App and browser protection visibility: {exception.Message}");
                return false;
            }
        }
    }
}
