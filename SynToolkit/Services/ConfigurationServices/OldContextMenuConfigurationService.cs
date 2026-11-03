using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Services.ConfigurationServices
{
    public class OldContextMenuConfigurationService : IConfigurationService
    {
        
        private const string SYNTOOLKIT_STORE_KEY_NAME = @"HKLM\SOFTWARE\SynToolkit\Services\OldContextMenu";
        private const string STATE_VALUE_NAME = "state";

        private const string INCROP_SERVER_32 = @"HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";


        private readonly ConfigurationStore _oldContextMenuConfigurationService;

        public OldContextMenuConfigurationService(
            [FromKeyedServices("OldContextMenu")] ConfigurationStore oldContextMenuConfigurationService)
        {
            _oldContextMenuConfigurationService = oldContextMenuConfigurationService;
        }

        public void Disable()
        {
            RegistryHelper.DeleteKey(INCROP_SERVER_32);

            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 0);

            _oldContextMenuConfigurationService.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.SetValue(INCROP_SERVER_32, "", "");

            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 1);

            _oldContextMenuConfigurationService.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            try
            {
                // Windows 10 already uses the legacy menu by default.
                if (!WindowsVersionService.IsWindows11)
                {
                    return true;
                }

                return RegistryHelper.IsMatch(INCROP_SERVER_32, string.Empty, string.Empty);
            }
            catch (Exception exception)
            {
                App.logger.Warn($"Unable to detect legacy context-menu state: {exception.Message}");
                return false;
            }
        }
    }
}
