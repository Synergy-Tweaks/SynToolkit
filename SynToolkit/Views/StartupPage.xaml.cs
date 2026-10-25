#nullable enable

using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SynToolkit.Models.Startup;
using SynToolkit.ViewModels;

namespace SynToolkit.Views
{
    public sealed partial class StartupPage : Page
    {
        private readonly StartupPageViewModel _viewModel;

        public StartupPage()
        {
            _viewModel = new StartupPageViewModel();
            _viewModel.ConfirmToggle = ConfirmToggleAsync;
            InitializeComponent();
            DataContext = _viewModel;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _ = _viewModel.EnsureScannedAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _viewModel.CancelPending();
            base.OnNavigatedFrom(e);
        }

        private async Task<bool> ConfirmToggleAsync(StartupEntry entry)
        {
            string body = string.Format(
                CultureInfo.CurrentCulture,
                App.GetValueFromItemList("Startup_ConfirmBody"),
                entry.Name,
                entry.CategoryLabel,
                entry.RiskLabel);
            if (entry.Category == StartupCategory.Service)
            {
                body += Environment.NewLine + Environment.NewLine + App.GetValueFromItemList("Startup_ConfirmServiceWarning");
            }

            if (entry.IsSensitive)
            {
                body += Environment.NewLine + Environment.NewLine + App.GetValueFromItemList("Startup_ConfirmSystemWarning");
            }

            ContentDialog dialog = new()
            {
                XamlRoot = XamlRoot ?? App.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = App.GetValueFromItemList("Startup_ConfirmTitle"),
                Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = entry.IsEnabled
                    ? App.GetValueFromItemList("Startup_ConfirmPrimaryDisable")
                    : App.GetValueFromItemList("Startup_ConfirmPrimaryEnable"),
                CloseButtonText = App.GetValueFromItemList("Startup_Cancel"),
                DefaultButton = ContentDialogButton.Close
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
    }
}
