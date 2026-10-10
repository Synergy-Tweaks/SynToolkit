#nullable enable

using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// Desktop right-click power-plan switcher for already-installed plans,
    /// plus double-click import/activate for .pow files. Does not ship or import
    /// any third-party power plans.
    /// </summary>
    internal sealed class PowerPlanShellConfigurationService : IConfigurationService
    {
        private const string MenuKey =
            @"HKLM\SOFTWARE\Classes\DesktopBackground\Shell\SynToolkit.PowerPlans";
        private const string PowExtensionKey = @"HKLM\SOFTWARE\Classes\.pow";
        private const string PowProgIdKey = @"HKLM\SOFTWARE\Classes\SynToolkit.PowerPlanFile";
        private const string StoreKey = @"HKLM\SOFTWARE\SynToolkit\Services\PowerPlanShell";
        private const string ProgIdName = "SynToolkit.PowerPlanFile";
        private const string MenuVerb = "Power Plans";
        private const int MaxMenuPlans = 30;

        private static readonly Regex GuidRegex = new(
            @"[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly string ToolsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SynToolkit",
            "PowerPlanShell");

        private readonly ConfigurationStore _store;

        public PowerPlanShellConfigurationService(
            [FromKeyedServices("PowerPlanShell")] ConfigurationStore store)
        {
            _store = store;
        }

        public void Disable()
        {
            RegistryHelper.DeleteKey(MenuKey);
            RemovePowAssociationIfOwned();
            RegistryHelper.SetValue(StoreKey, "state", 0, RegistryValueKind.DWord);
            _store.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            DeployTools();
            WriteImportScript();
            RegisterDesktopMenu();
            RegisterPowAssociation();
            RegistryHelper.SetValue(StoreKey, "state", 1, RegistryValueKind.DWord);
            _store.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            return RegistryHelper.IsMatch(MenuKey, "MUIVerb", MenuVerb)
                && RegistryHelper.IsMatch(PowExtensionKey, null, ProgIdName)
                && RegistryHelper.KeyExists($@"{PowProgIdKey}\shell\open\command");
        }

        private static void DeployTools()
        {
            Directory.CreateDirectory(Path.Combine(ToolsDirectory, "Icons"));
            string sourceIcons = Path.Combine(AppContext.BaseDirectory, "Assets", "PowerPlanShell", "Icons");
            if (!Directory.Exists(sourceIcons))
            {
                throw new DirectoryNotFoundException(
                    "Bundled PowerPlanShell icons are missing. Rebuild SynToolkit so Assets\\PowerPlanShell is copied.");
            }

            foreach (string iconPath in Directory.EnumerateFiles(sourceIcons, "*.ico"))
            {
                File.Copy(iconPath, Path.Combine(ToolsDirectory, "Icons", Path.GetFileName(iconPath)), overwrite: true);
            }
        }

        private static void WriteImportScript()
        {
            string scriptPath = Path.Combine(ToolsDirectory, "ImportPow.ps1");
            const string script = """
param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework | Out-Null

function Import-AndActivate([string]$PowPath) {
    if (-not (Test-Path -LiteralPath $PowPath)) {
        throw "The power-plan file could not be found."
    }

    $powerCfg = Join-Path $env:SystemRoot 'System32\powercfg.exe'
    $output = & $powerCfg -import $PowPath 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw ("powercfg import failed:`n" + $output)
    }

    $match = [regex]::Match($output, '[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}')
    if (-not $match.Success) {
        throw ("Import succeeded but no scheme GUID was returned:`n" + $output)
    }

    & $powerCfg -setactive $match.Value | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw ("The plan was imported but could not be activated (`nGUID " + $match.Value + ").")
    }
}

try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]$identity
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        $arg = '-NoLogo -NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -Path "' + $Path + '"'
        Start-Process -FilePath 'powershell.exe' -ArgumentList $arg -Verb RunAs -Wait | Out-Null
        exit 0
    }

    Import-AndActivate -PowPath $Path
}
catch {
    [System.Windows.MessageBox]::Show(
        $_.Exception.Message,
        'SynToolkit Power Plan Import',
        'OK',
        'Error') | Out-Null
    exit 1
}
""";
            File.WriteAllText(scriptPath, script, Encoding.UTF8);
        }

        private void RegisterDesktopMenu()
        {
            RegistryHelper.DeleteKey(MenuKey);

            string parentIcon = Path.Combine(ToolsDirectory, "Icons", "Power Options Icon.ico");
            string powerSaverIcon = Path.Combine(ToolsDirectory, "Icons", "Power Saver.ico");
            RegistryHelper.SetValue(MenuKey, "Icon", parentIcon + ",0");
            RegistryHelper.SetValue(MenuKey, "MUIVerb", MenuVerb);
            RegistryHelper.SetValue(MenuKey, "Position", "Bottom");
            RegistryHelper.SetValue(MenuKey, "SubCommands", string.Empty);

            Dictionary<string, Guid> schemes = ListPowerSchemes();
            int index = 0;
            foreach (KeyValuePair<string, Guid> scheme in schemes
                .OrderBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                if (index >= MaxMenuPlans)
                {
                    break;
                }

                string shellKeyName = $"Plan{index:D2}";
                string entryKey = $@"{MenuKey}\Shell\{shellKeyName}";
                bool isPowerSaver = scheme.Key.Contains("Power saver", StringComparison.OrdinalIgnoreCase)
                    || scheme.Key.Contains("Power Saver", StringComparison.OrdinalIgnoreCase);
                string iconPath = isPowerSaver && File.Exists(powerSaverIcon) ? powerSaverIcon : parentIcon;

                RegistryHelper.SetValue(entryKey, "MUIVerb", TruncateMenuLabel(scheme.Key));
                RegistryHelper.SetValue(entryKey, "Icon", iconPath + ",0");
                RegistryHelper.SetValue(
                    $@"{entryKey}\command",
                    null,
                    BuildSetActiveCommand(scheme.Value),
                    RegistryValueKind.String);
                index++;
            }
        }

        private static string BuildSetActiveCommand(Guid schemeId)
        {
            string powerCfg = Path.Combine(Environment.SystemDirectory, "powercfg.exe");
            return $"\"{powerCfg}\" -setactive {schemeId:D}";
        }

        private static string TruncateMenuLabel(string name) =>
            name.Length <= 48 ? name : name[..45] + "...";

        private void RegisterPowAssociation()
        {
            object? existing = RegistryHelper.GetValue(PowExtensionKey, null);
            string? existingProgId = existing as string;
            if (!string.IsNullOrWhiteSpace(existingProgId)
                && !string.Equals(existingProgId, ProgIdName, StringComparison.OrdinalIgnoreCase))
            {
                RegistryHelper.SetValue(StoreKey, "PreviousPowProgId", existingProgId, RegistryValueKind.String);
            }

            string scriptPath = Path.Combine(ToolsDirectory, "ImportPow.ps1");
            string iconPath = Path.Combine(ToolsDirectory, "Icons", "Power Options Icon.ico");
            string openCommand =
                $"powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -Path \"%1\"";

            RegistryHelper.SetValue(PowExtensionKey, null, ProgIdName, RegistryValueKind.String);
            RegistryHelper.SetValue(PowProgIdKey, null, "Windows Power Plan", RegistryValueKind.String);
            RegistryHelper.SetValue($@"{PowProgIdKey}\DefaultIcon", null, iconPath + ",0", RegistryValueKind.String);
            RegistryHelper.SetValue($@"{PowProgIdKey}\shell\open", null, "Import && Activate", RegistryValueKind.String);
            RegistryHelper.SetValue($@"{PowProgIdKey}\shell\open", "Icon", iconPath + ",0", RegistryValueKind.String);
            RegistryHelper.SetValue(
                $@"{PowProgIdKey}\shell\open\command",
                null,
                openCommand,
                RegistryValueKind.String);
        }

        private static void RemovePowAssociationIfOwned()
        {
            object? current = RegistryHelper.GetValue(PowExtensionKey, null);
            if (current is string progId
                && string.Equals(progId, ProgIdName, StringComparison.OrdinalIgnoreCase))
            {
                object? previous = RegistryHelper.GetValue(StoreKey, "PreviousPowProgId");
                if (previous is string previousProgId && !string.IsNullOrWhiteSpace(previousProgId))
                {
                    RegistryHelper.SetValue(PowExtensionKey, null, previousProgId, RegistryValueKind.String);
                }
                else
                {
                    RegistryHelper.DeleteValue(PowExtensionKey, string.Empty);
                }
            }

            RegistryHelper.DeleteKey(PowProgIdKey);
            RegistryHelper.DeleteValue(StoreKey, "PreviousPowProgId");
        }

        private static Dictionary<string, Guid> ListPowerSchemes()
        {
            string powerCfg = Path.Combine(Environment.SystemDirectory, "powercfg.exe");
            CommandResult result = CommandPromptHelper.RunProcessResult(
                powerCfg,
                new[] { "/list" },
                timeoutMilliseconds: 30_000);

            var schemes = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            if (result.ExitCode != 0)
            {
                return schemes;
            }

            foreach (string line in result.StandardOutput.Split('\n'))
            {
                Match guidMatch = GuidRegex.Match(line);
                if (!guidMatch.Success || !Guid.TryParse(guidMatch.Value, out Guid schemeId))
                {
                    continue;
                }

                int nameStart = line.IndexOf('(');
                int nameEnd = line.LastIndexOf(')');
                if (nameStart < 0 || nameEnd <= nameStart)
                {
                    continue;
                }

                string name = line[(nameStart + 1)..nameEnd].Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    schemes[name] = schemeId;
                }
            }

            return schemes;
        }
    }
}
