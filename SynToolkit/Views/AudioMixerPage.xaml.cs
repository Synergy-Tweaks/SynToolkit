#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CommunityToolkit.WinUI.Controls;
using SynToolkit.ViewModels;

namespace SynToolkit.Views
{
    public sealed partial class AudioMixerPage : Page
    {
        private readonly AudioMixerPageViewModel _viewModel;

        public AudioMixerPage()
        {
            InitializeComponent();
            _viewModel = App._host.Services.GetRequiredService<AudioMixerPageViewModel>();
            DataContext = _viewModel;
            Loaded += AudioMixerPage_Loaded;
            Unloaded += AudioMixerPage_Unloaded;
        }

        private async void AudioMixerPage_Loaded(object sender, RoutedEventArgs e)
        {
            await _viewModel.ActivateAsync();
        }

        private void AudioMixerPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _viewModel.Deactivate();
        }

        private void TipsInfoBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
        {
            _viewModel.DismissTips();
        }
    }
}
