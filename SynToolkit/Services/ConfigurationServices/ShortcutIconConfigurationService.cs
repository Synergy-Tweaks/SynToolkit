using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.IO;

namespace SynToolkit.Services.ConfigurationServices
{
    internal class ShortcutIconConfigurationService : IMultiOptionConfigurationServices
    {
        private const string SHELL_ICONS_KEY_NAME = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons";
        private const string SHORTCUT_ICON_VALUE_NAME = "29";


        private static readonly string SHORTCUT_ICON_REG_FILE_PATH = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Synergy", "ConfigurationServices", "ShortcutIcon", "ShortcutIcon_");

        private List<string> options = new List<string>()
        {
            "Default Windows",
            "SynToolkit",
            "None (security risk)",
            "Custom icon (detected)",
        };

        private readonly MultiOptionConfigurationStore _shortcutIconConfigurationService;

        public ShortcutIconConfigurationService(
            [FromKeyedServices("ShortcutIcon")] MultiOptionConfigurationStore shortcutIconConfigurationService)
        {
            _shortcutIconConfigurationService = shortcutIconConfigurationService;
            _shortcutIconConfigurationService.Options = options;
        }

        public void ChangeStatus(int status)
        {
            if (status < 0 || status > 2)
            {
                _shortcutIconConfigurationService.CurrentSetting = Status();
                return;
            }

            string payloadPath = SHORTCUT_ICON_REG_FILE_PATH + (status + 1).ToString() + ".reg";
            if (!File.Exists(payloadPath))
            {
                throw new FileNotFoundException("The shortcut icon payload is missing.", payloadPath);
            }

            RegistryHelper.MergeRegFile(payloadPath);

            if (status == 1 || status == 2)
            {
                // The payload hardcodes C:\ProgramData: rewrite with the real
                // path so relocated ProgramData still resolves (and Status verifies it).
                string iconFile = status == 1 ? "SynToolkit.ico" : "Blank.ico";
                string iconPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Synergy", "Assets", iconFile) + ",0";
                RegistryHelper.SetValue(SHELL_ICONS_KEY_NAME, SHORTCUT_ICON_VALUE_NAME, iconPath);
            }

            string detectedStatus = Status();
            _shortcutIconConfigurationService.CurrentSetting = detectedStatus;

            if (!string.Equals(detectedStatus, options[status], StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Windows did not report the requested shortcut-icon state.");
            }
        }

        public string Status()
        {
            string shortcutIcon = RegistryHelper.GetValue(SHELL_ICONS_KEY_NAME, SHORTCUT_ICON_VALUE_NAME)?.ToString();
            if (string.IsNullOrWhiteSpace(shortcutIcon))
            {
                return options[0];
            }

            string expectedSynToolkitIcon = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Synergy", "Assets", "SynToolkit.ico") + ",0";
            string expectedBlankIcon = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Synergy", "Assets", "Blank.ico") + ",0";

            if (string.Equals(shortcutIcon, expectedSynToolkitIcon, StringComparison.OrdinalIgnoreCase))
            {
                return options[1];
            }

            return string.Equals(shortcutIcon, expectedBlankIcon, StringComparison.OrdinalIgnoreCase)
                ? options[2]
                : options[3];
        }
    }
}
