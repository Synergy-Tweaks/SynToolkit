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
using System.IO;

namespace SynToolkit.Services.ConfigurationServices
{
    public class SecurityHealthTrayConfigurationService : IConfigurationService
    {
        private const string SYNTOOLKIT_STORE_KEY_NAME = @"HKLM\SOFTWARE\SynToolkit\Services\SecurityHealthTray";
        private const string STATE_VALUE_NAME = "state";
        private const string WINDOWS_RUN_KEY_NAME = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string SECURITY_HEALTH_VALUE_NAME = "SecurityHealth";

        private readonly ConfigurationStore _securityHealthTrayConfigurationService;

        public SecurityHealthTrayConfigurationService(
            [FromKeyedServices("SecurityHealthTray")] ConfigurationStore securityHealthTrayConfigurationService)
        {
            _securityHealthTrayConfigurationService = securityHealthTrayConfigurationService;
        }

        public void Disable()
        {
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 0);
            RegistryHelper.MergeRegFile(@$"{Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)}\Synergy\Scripts\SecurityHealthTray\RemoveTray.reg");

            _securityHealthTrayConfigurationService.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 1);
            RegistryHelper.MergeRegFile(@$"{Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)}\Synergy\Scripts\SecurityHealthTray\AddTray.reg");

            _securityHealthTrayConfigurationService.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            try
            {
                string configuredPath = RegistryHelper.GetValue(WINDOWS_RUN_KEY_NAME, SECURITY_HEALTH_VALUE_NAME) as string;
                if (string.IsNullOrWhiteSpace(configuredPath))
                {
                    return false;
                }

                string expectedPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "System32",
                    "SecurityHealthSystray.exe");
                string expandedPath = Environment.ExpandEnvironmentVariables(configuredPath).Trim().Trim('"');

                return File.Exists(expectedPath)
                    && string.Equals(
                        Path.GetFullPath(expandedPath),
                        Path.GetFullPath(expectedPath),
                        StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[SECURITYHEALTHTRAY] Unable to inspect the Security Health startup entry.");
                return false;
            }
        }
    }
}
