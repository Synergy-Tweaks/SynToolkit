#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SynToolkit.Commands.ConfigurationButtonsCommand;
using SynToolkit.ViewModels;

namespace SynToolkit.Views
{
    public sealed partial class RestorationPanel : UserControl
    {
        private readonly RestorationPanelViewModel _viewModel;
        private bool _activated;

        public RestorationPanel()
        {
            InitializeComponent();
            _viewModel = App._host.Services.GetRequiredService<RestorationPanelViewModel>();
            DataContext = _viewModel;
            DescriptionBox.Header = App.GetValueFromItemList("Restoration_DescriptionLabel");
        }

        public void Activate()
        {
            if (_activated)
            {
                return;
            }

            _activated = true;
            _ = _viewModel.InitializeAsync();
        }

        public void Deactivate()
        {
            _viewModel.CancelPending();
        }

        private void OpenWindowsSystemRestore_Click(object sender, RoutedEventArgs e)
        {
            new OpenSystemProtectionCommand().Execute(null);
        }
    }
}
