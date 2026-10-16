using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SynToolkit.Utils;

namespace SynToolkit.Commands.ConfigurationButtonsCommand
{
    public class ToggleDefenderCommand : AsyncCommandBase
    {
        protected override async Task ExecuteAsync(object parameter)
        {
            await Task.Run(() => {
                ProcessHelper.StartShellExecute($@"{Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)}\Synergy\Scripts\toggleDefender.cmd");
            });
        }
    }
}
