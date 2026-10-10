using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// SynergyOS disables Nagle (TcpAckFrequency=1, TcpDelAckTicks=0) on adapters
    /// and turns off TCP timestamps.
    /// Toggle on = optimizations applied.
    /// </summary>
    internal class NetworkLatencyConfigurationService : IConfigurationService
    {
        private const string STORE_KEY = @"HKLM\SOFTWARE\SynToolkit\Services\NetworkLatency";
        private readonly ConfigurationStore _store;

        public NetworkLatencyConfigurationService(
            [FromKeyedServices("NetworkLatency")] ConfigurationStore store)
        {
            _store = store;
        }

        public void Disable()
        {
            CommandPromptHelper.RunCommand(
                "powershell.exe -NoProfile -Command \"Get-ChildItem 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces' | ForEach-Object { Remove-ItemProperty -Path $_.PSPath -Name TcpAckFrequency,TcpDelAckTicks -ErrorAction SilentlyContinue }\"");
            CommandPromptHelper.RunCommand("netsh int tcp set global timestamps=default");
            RegistryHelper.SetValue(STORE_KEY, "state", 0, RegistryValueKind.DWord);
            _store.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            CommandPromptHelper.RunCommand(
                "powershell.exe -NoProfile -Command \"Get-CimInstance Win32_NetworkAdapter | Where-Object { $_.GUID } | ForEach-Object { $p = 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces\\' + $_.GUID; if (Test-Path $p) { New-ItemProperty -Path $p -Name TcpAckFrequency -PropertyType DWord -Value 1 -Force | Out-Null; New-ItemProperty -Path $p -Name TcpDelAckTicks -PropertyType DWord -Value 0 -Force | Out-Null } }\"");
            CommandPromptHelper.RunCommand("netsh int tcp set global timestamps=disabled");
            RegistryHelper.SetValue(STORE_KEY, "state", 1, RegistryValueKind.DWord);
            _store.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled() => RegistryHelper.IsMatch(STORE_KEY, "state", 1);
    }
}
