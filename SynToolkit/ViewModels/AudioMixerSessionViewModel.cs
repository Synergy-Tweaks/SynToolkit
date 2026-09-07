#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using SynToolkit.Services.AudioMixer;

namespace SynToolkit.ViewModels
{
    public partial class AudioMixerSessionViewModel : ObservableObject
    {
        private readonly AudioMixerPageViewModel _owner;
        private bool _suppressApply;

        public AudioMixerSessionViewModel(
            AudioMixerPageViewModel owner,
            string name,
            int volume,
            bool isActive,
            bool isSaved,
            bool isSystemSounds,
            string statusText,
            string? executablePath)
        {
            _owner = owner;
            Name = name;
            DisplayName = SessionNaming.GetDisplayName(name);
            ForgetCommand = new RelayCommand(() => _owner.ForgetSession(this), () => IsSaved);

            _suppressApply = true;
            Volume = Math.Clamp(volume, 0, 100);
            IsActive = isActive;
            IsSaved = isSaved;
            IsSystemSounds = isSystemSounds;
            StatusText = statusText;
            ExecutablePath = executablePath;
            _suppressApply = false;
        }

        public string Name { get; }

        public string DisplayName { get; }

        public bool IsSystemSounds { get; }

        [ObservableProperty]
        public partial string? ExecutablePath { get; set; }

        [ObservableProperty]
        public partial int Volume { get; set; }

        [ObservableProperty]
        public partial bool IsActive { get; set; }

        [ObservableProperty]
        public partial bool IsSaved { get; set; }

        [ObservableProperty]
        public partial string StatusText { get; set; }

        [ObservableProperty]
        public partial ImageSource? IconSource { get; set; }

        public bool HasCustomIcon => IconSource is not null;

        public string VolumeText => $"{Volume} %";

        public IRelayCommand ForgetCommand { get; }

        partial void OnVolumeChanged(int value)
        {
            OnPropertyChanged(nameof(VolumeText));
            if (!_suppressApply)
            {
                _owner.ApplySessionVolume(this, value);
            }
        }

        partial void OnIsSavedChanged(bool value)
        {
            ForgetCommand?.NotifyCanExecuteChanged();
        }

        partial void OnIconSourceChanged(ImageSource? value)
        {
            OnPropertyChanged(nameof(HasCustomIcon));
        }

        public void UpdateFromSnapshot(int volume, bool isActive, bool isSaved, string statusText, string? executablePath)
        {
            _suppressApply = true;
            Volume = Math.Clamp(volume, 0, 100);
            _suppressApply = false;

            IsActive = isActive;
            IsSaved = isSaved;
            StatusText = statusText;
            if (!string.Equals(ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase))
            {
                ExecutablePath = executablePath;
                _ = LoadIconAsync();
            }
        }

        public void UpdateVolumeFromDevice(float scalarVolume)
        {
            _suppressApply = true;
            Volume = (int)Math.Round(Math.Clamp(scalarVolume, 0f, 1f) * 100f);
            _suppressApply = false;
        }

        public async Task LoadIconAsync()
        {
            if (IsSystemSounds)
            {
                return;
            }

            IconSource = await ExecutableIconLoader.LoadAsync(ExecutablePath);
        }
    }
}
