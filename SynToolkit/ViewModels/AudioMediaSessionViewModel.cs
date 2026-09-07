#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using SynToolkit.Services.AudioMixer;
using Windows.Media.Control;

namespace SynToolkit.ViewModels
{
    public partial class AudioMediaSessionViewModel : ObservableObject
    {
        public AudioMediaSessionViewModel(MediaSessionInfo info)
        {
            SessionId = info.SessionId;
            _ = UpdateFromInfoAsync(info);
        }

        public string SessionId { get; }

        [ObservableProperty]
        public partial string SourceAppName { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string Title { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string Subtitle { get; set; } = string.Empty;

        [ObservableProperty]
        public partial bool IsCurrent { get; set; }

        [ObservableProperty]
        public partial GlobalSystemMediaTransportControlsSessionPlaybackStatus PlaybackStatus { get; set; }

        [ObservableProperty]
        public partial bool CanPlay { get; set; }

        [ObservableProperty]
        public partial bool CanPause { get; set; }

        [ObservableProperty]
        public partial bool CanSkipNext { get; set; }

        [ObservableProperty]
        public partial bool CanSkipPrevious { get; set; }

        [ObservableProperty]
        public partial ImageSource? ThumbnailSource { get; set; }

        [ObservableProperty]
        public partial ImageSource? AppIconSource { get; set; }

        [ObservableProperty]
        public partial string? SourceExecutablePath { get; set; }

        public bool HasThumbnail => ThumbnailSource is not null;

        public bool HasAppIcon => AppIconSource is not null;

        public string SourceBadgeText =>
            string.IsNullOrWhiteSpace(SourceAppName) ? "?" : SourceAppName[..1].ToUpperInvariant();

        public string SessionSummary =>
            !string.IsNullOrWhiteSpace(Title)
                ? Title
                : PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                    ? "Playing"
                    : "Ready";

        partial void OnThumbnailSourceChanged(ImageSource? value) => OnPropertyChanged(nameof(HasThumbnail));

        partial void OnAppIconSourceChanged(ImageSource? value) => OnPropertyChanged(nameof(HasAppIcon));

        partial void OnSourceAppNameChanged(string value)
        {
            OnPropertyChanged(nameof(SourceBadgeText));
            OnPropertyChanged(nameof(SessionSummary));
        }

        partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(SessionSummary));

        partial void OnPlaybackStatusChanged(GlobalSystemMediaTransportControlsSessionPlaybackStatus value) =>
            OnPropertyChanged(nameof(SessionSummary));

        public async Task UpdateFromInfoAsync(MediaSessionInfo info)
        {
            SourceAppName = info.SourceAppName;
            Title = string.IsNullOrWhiteSpace(info.Title) ? "Unknown title" : info.Title;
            Subtitle = info.Subtitle;
            IsCurrent = info.IsCurrent;
            PlaybackStatus = info.PlaybackStatus;
            CanPlay = info.CanPlay;
            CanPause = info.CanPause;
            CanSkipNext = info.CanSkipNext;
            CanSkipPrevious = info.CanSkipPrevious;
            SourceExecutablePath = info.SourceExecutablePath;
            ThumbnailSource = await MediaImageLoader.LoadAsync(info.ThumbnailBytes);
            AppIconSource = info.AppIconBytes is not null
                ? await MediaImageLoader.LoadAsync(info.AppIconBytes)
                : await ExecutableIconLoader.LoadAsync(info.SourceExecutablePath);
        }
    }
}
