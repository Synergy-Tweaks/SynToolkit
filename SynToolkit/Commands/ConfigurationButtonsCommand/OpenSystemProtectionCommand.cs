using System.Threading.Tasks;
using SynToolkit.Utils;

namespace SynToolkit.Commands.ConfigurationButtonsCommand
{
    internal sealed class OpenSystemProtectionCommand : AsyncCommandBase
    {
        protected override async Task ExecuteAsync(object parameter)
        {
            await Task.Run(() => ProcessHelper.StartShellExecute("SystemPropertiesProtection.exe"));
        }
    }
}