#nullable enable

using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SynToolkit.Services.AudioMixer;
using Windows.Media.Control;

namespace SynToolkit.ViewModels
{
    public partial class AudioMixerPageViewModel : ObservableObject
    {
        private readonly IAudioMixerService _audioMixerService;
        private readonly IMediaSessionService _mediaSessionService;
        private readonly AudioMixerHotkeyService _hotkeyService;
        private readonly DispatcherQueue _dispatcherQueue;
        private bool _isInitialized;
        private bool _suppressMasterApply;
        private bool _suppressHotkeyApply;

        public AudioMixerPageViewModel(
            IAudioMixerService audioMixerService,
            IMediaSessionService mediaSessionService,
            AudioMixerHotkeyService hotkeyService)
        {
            _audioMixerService = audioMixerService;
            _mediaSessionService = mediaSessionService;
            _hotkeyService = hotkeyService;
            _dispatcherQueue = App.m_window?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();

            Sessions = [];
            MediaSessions = [];
            HotkeyKeys =
            [
                "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
                "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
                "0", "1", "2", "3", "4", "5", "6", "7", "8", "9",
                "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12"
            ];
        }

        public ObservableCollection<AudioMixerSessionViewModel> Sessions { get; }

        public ObservableCollection<AudioMediaSessionViewModel> MediaSessions { get; }

        public IReadOnlyList<string> HotkeyKeys { get; }

        [ObservableProperty]
        public partial int MasterVolume { get; set; }

        [ObservableProperty]
        public partial int DefaultVolume { get; set; }

        [ObservableProperty]
        public partial bool IsTipsOpen { get; set; } = true;

        [ObservableProperty]
        public partial bool HasError { get; set; }

        [ObservableProperty]
        public partial string ErrorMessage { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string HotkeyDisplayText { get; set; } = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasMediaSession))]
        [NotifyPropertyChangedFor(nameof(CanTogglePlayback))]
        [NotifyPropertyChangedFor(nameof(CanSkipNext))]
        [NotifyPropertyChangedFor(nameof(CanSkipPrevious))]
        [NotifyPropertyChangedFor(nameof(PlayPauseGlyph))]
        [NotifyPropertyChangedFor(nameof(PlayPauseText))]
        public partial AudioMediaSessionViewModel? SelectedMediaSession { get; set; }

        [ObservableProperty]
        public partial string SelectedHotkeyKey { get; set; } = "V";

        [ObservableProperty]
        public partial bool HotkeyCtrl { get; set; } = true;

        [ObservableProperty]
        public partial bool HotkeyShift { get; set; } = true;

        [ObservableProperty]
        public partial bool HotkeyAlt { get; set; }

        [ObservableProperty]
        public partial bool HotkeyWin { get; set; }

        public string MasterVolumeText => $"{MasterVolume} %";

        public string DefaultVolumeText => $"{DefaultVolume} %";

        public bool HasSessions => Sessions.Count > 0;

        public bool HasMediaSession => SelectedMediaSession is not null;

        public bool CanTogglePlayback => SelectedMediaSession is not null &&
            (SelectedMediaSession.CanPause || SelectedMediaSession.CanPlay);

        public bool CanSkipNext => SelectedMediaSession?.CanSkipNext == true;

        public bool CanSkipPrevious => SelectedMediaSession?.CanSkipPrevious == true;

        public string PlayPauseGlyph => SelectedMediaSession?.PlaybackStatus ==
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing ? "\uE769" : "\uE768";

        public string PlayPauseText => SelectedMediaSession?.PlaybackStatus ==
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing ? "Pause" : "Play";

        public string TipsMessage =>
            $"Manage live per-app audio levels here. Press {HotkeyDisplayText} from anywhere to jump straight to this mixer.";

        public async Task ActivateAsync()
        {
            if (_isInitialized)
            {
                RefreshFromService();
                _mediaSessionService.RequestRefresh();
                return;
            }

            _audioMixerService.SessionsChanged += AudioMixerService_SessionsChanged;
            _audioMixerService.MasterVolumeChanged += AudioMixerService_MasterVolumeChanged;
            _audioMixerService.ErrorOccurred += AudioMixerService_ErrorOccurred;
            _mediaSessionService.SessionsChanged += MediaSessionService_SessionsChanged;
            _mediaSessionService.ErrorOccurred += MediaSessionService_ErrorOccurred;

            await _mediaSessionService.StartAsync();
            RefreshFromService();
            _audioMixerService.RequestRefresh();
            _mediaSessionService.RequestRefresh();
            _isInitialized = true;

            foreach (AudioMixerSessionViewModel session in Sessions)
            {
                await session.LoadIconAsync();
            }
        }

        public void Deactivate()
        {
            if (!_isInitialized)
            {
                return;
            }

            _audioMixerService.SessionsChanged -= AudioMixerService_SessionsChanged;
            _audioMixerService.MasterVolumeChanged -= AudioMixerService_MasterVolumeChanged;
            _audioMixerService.ErrorOccurred -= AudioMixerService_ErrorOccurred;
            _mediaSessionService.SessionsChanged -= MediaSessionService_SessionsChanged;
            _mediaSessionService.ErrorOccurred -= MediaSessionService_ErrorOccurred;
            foreach (AudioMediaSessionViewModel session in MediaSessions)
            {
                session.PropertyChanged -= MediaSessionViewModel_PropertyChanged;
            }
            _isInitialized = false;
        }

        partial void OnMasterVolumeChanged(int value)
        {
            OnPropertyChanged(nameof(MasterVolumeText));
            if (!_suppressMasterApply)
            {
                _audioMixerService.SetMasterVolume(value / 100f);
            }
        }

        partial void OnDefaultVolumeChanged(int value)
        {
            OnPropertyChanged(nameof(DefaultVolumeText));
            _audioMixerService.SetDefaultVolume(value / 100f);
        }

        partial void OnSelectedHotkeyKeyChanged(string value) => ApplyHotkeyIfReady();

        partial void OnHotkeyCtrlChanged(bool value) => ApplyHotkeyIfReady();

        partial void OnHotkeyShiftChanged(bool value) => ApplyHotkeyIfReady();

        partial void OnHotkeyAltChanged(bool value) => ApplyHotkeyIfReady();

        partial void OnHotkeyWinChanged(bool value) => ApplyHotkeyIfReady();

        partial void OnSelectedMediaSessionChanged(AudioMediaSessionViewModel? value)
        {
            UpdateSelectedMediaSessionState();
        }

        public void DismissTips()
        {
            IsTipsOpen = false;
            _audioMixerService.SetTipsDismissed(true);
        }

        public void ApplySessionVolume(AudioMixerSessionViewModel session, int value)
        {
            _audioMixerService.SetSessionVolume(session.Name, value / 100f);
            session.IsSaved = true;
            session.StatusText = session.IsActive
                ? "Running now. This level will be remembered."
                : "Saved level. It will be applied next time this app plays audio.";
        }

        public void ForgetSession(AudioMixerSessionViewModel session)
        {
            _audioMixerService.RemoveSavedSession(session.Name);
            session.IsSaved = false;
            session.StatusText = session.IsActive
                ? "Running now."
                : "Not currently running.";

            if (!session.IsActive)
            {
                Sessions.Remove(session);
                OnPropertyChanged(nameof(HasSessions));
            }
        }

        [RelayCommand(CanExecute = nameof(CanSkipPrevious))]
        private async Task SkipPreviousAsync()
        {
            if (SelectedMediaSession is null)
            {
                return;
            }

            await _mediaSessionService.TrySkipPreviousAsync(SelectedMediaSession.SessionId);
        }

        [RelayCommand(CanExecute = nameof(CanTogglePlayback))]
        private async Task TogglePlaybackAsync()
        {
            if (SelectedMediaSession is null)
            {
                return;
            }

            await _mediaSessionService.TryTogglePlayPauseAsync(SelectedMediaSession.SessionId);
        }

        [RelayCommand(CanExecute = nameof(CanSkipNext))]
        private async Task SkipNextAsync()
        {
            if (SelectedMediaSession is null)
            {
                return;
            }

            await _mediaSessionService.TrySkipNextAsync(SelectedMediaSession.SessionId);
        }

        private void RefreshFromService()
        {
            _suppressMasterApply = true;
            MasterVolume = (int)Math.Round(Math.Clamp(_audioMixerService.GetMasterVolume(), 0f, 1f) * 100f);
            _suppressMasterApply = false;

            DefaultVolume = (int)Math.Round(Math.Clamp(_audioMixerService.GetDefaultVolume(), 0f, 1f) * 100f);
            IsTipsOpen = !_audioMixerService.GetTipsDismissed();

            ApplyHotkeySnapshot(_hotkeyService.CurrentHotkey);
            RebuildSessions(_audioMixerService.GetCurrentSessions());
            _ = RebuildMediaSessionsAsync(_mediaSessionService.GetSessions());
        }

        private void AudioMixerService_SessionsChanged(IReadOnlyList<AudioSessionInfo> sessions) =>
            _dispatcherQueue.TryEnqueue(async () =>
            {
                RebuildSessions(sessions);
                foreach (AudioMixerSessionViewModel session in Sessions.Where(session => !session.HasCustomIcon))
                {
                    await session.LoadIconAsync();
                }
            });

        private void AudioMixerService_MasterVolumeChanged(float scalar) =>
            _dispatcherQueue.TryEnqueue(() =>
            {
                _suppressMasterApply = true;
                MasterVolume = (int)Math.Round(Math.Clamp(scalar, 0f, 1f) * 100f);
                _suppressMasterApply = false;
            });

        private void AudioMixerService_ErrorOccurred(string message) =>
            _dispatcherQueue.TryEnqueue(() =>
            {
                ErrorMessage = message;
                HasError = !string.IsNullOrWhiteSpace(message);
            });

        private void MediaSessionService_SessionsChanged(IReadOnlyList<MediaSessionInfo> sessions) =>
            _dispatcherQueue.TryEnqueue(async () => await RebuildMediaSessionsAsync(sessions));

        private void MediaSessionService_ErrorOccurred(string message) =>
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (string.IsNullOrWhiteSpace(message))
                {
                    if (string.Equals(ErrorMessage, "Unable to read active media sessions.", StringComparison.Ordinal))
                    {
                        ErrorMessage = string.Empty;
                        HasError = false;
                    }

                    return;
                }

                ErrorMessage = message;
                HasError = true;
            });

        private void RebuildSessions(IReadOnlyList<AudioSessionInfo> snapshot)
        {
            Dictionary<string, float> savedSessions = _audioMixerService.GetSavedSessions()
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, AudioSessionInfo> activeByName = new(StringComparer.OrdinalIgnoreCase);

            foreach (AudioSessionInfo info in snapshot)
            {
                activeByName.TryAdd(info.Name, info);
            }

            Dictionary<string, AudioMixerSessionViewModel> existing = Sessions.ToDictionary(session => session.Name, StringComparer.OrdinalIgnoreCase);
            List<AudioMixerSessionRow> rows = [];

            foreach ((string name, AudioSessionInfo info) in activeByName)
            {
                bool isSaved = savedSessions.ContainsKey(name);
                rows.Add(new AudioMixerSessionRow(
                    name,
                    SessionNaming.GetDisplayName(name),
                    true,
                    isSaved,
                    info.IsSystemSounds,
                    (int)Math.Round(Math.Clamp(info.ScalarVolume, 0f, 1f) * 100f),
                    isSaved ? "Running now. This level will be remembered." : "Running now.",
                    info.ExecutablePath));
            }

            foreach ((string name, float scalar) in savedSessions)
            {
                if (activeByName.ContainsKey(name))
                {
                    continue;
                }

                rows.Add(new AudioMixerSessionRow(
                    name,
                    SessionNaming.GetDisplayName(name),
                    false,
                    true,
                    string.Equals(name, SessionNaming.SystemSoundsName, StringComparison.Ordinal),
                    (int)Math.Round(Math.Clamp(scalar, 0f, 1f) * 100f),
                    "Saved level. It will be applied next time this app plays audio.",
                    null));
            }

            rows.Sort((left, right) =>
            {
                if (left.IsSystemSounds != right.IsSystemSounds)
                {
                    return left.IsSystemSounds ? -1 : 1;
                }

                if (left.IsActive != right.IsActive)
                {
                    return left.IsActive ? -1 : 1;
                }

                return string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
            });

            Sessions.Clear();
            foreach (AudioMixerSessionRow row in rows)
            {
                if (existing.TryGetValue(row.Name, out AudioMixerSessionViewModel? existingRow))
                {
                    existingRow.UpdateFromSnapshot(row.Volume, row.IsActive, row.IsSaved, row.StatusText, row.ExecutablePath);
                    Sessions.Add(existingRow);
                }
                else
                {
                    Sessions.Add(new AudioMixerSessionViewModel(
                        this,
                        row.Name,
                        row.Volume,
                        row.IsActive,
                        row.IsSaved,
                        row.IsSystemSounds,
                        row.StatusText,
                        row.ExecutablePath));
                }
            }

            OnPropertyChanged(nameof(HasSessions));
        }

        private async Task RebuildMediaSessionsAsync(IReadOnlyList<MediaSessionInfo> snapshot)
        {
            Dictionary<string, AudioMediaSessionViewModel> existing = new(StringComparer.Ordinal);
            foreach (AudioMediaSessionViewModel session in MediaSessions)
            {
                if (!existing.TryAdd(session.SessionId, session))
                {
                    App.logger.Warn(
                        "Duplicate media session view-model id {SessionId} encountered during rebuild. Keeping the latest instance.",
                        session.SessionId);
                    existing[session.SessionId] = session;
                }
            }

            Dictionary<string, MediaSessionInfo> uniqueSnapshot = new(StringComparer.Ordinal);
            foreach (MediaSessionInfo info in snapshot)
            {
                if (!uniqueSnapshot.TryAdd(info.SessionId, info))
                {
                    App.logger.Warn(
                        "Duplicate media session snapshot id {SessionId} encountered during rebuild. Keeping the latest item.",
                        info.SessionId);
                    uniqueSnapshot[info.SessionId] = info;
                }
            }

            MediaSessionInfo[] visibleSessions = uniqueSnapshot.Values
                .Where(session => session.IsCurrent)
                .Take(1)
                .DefaultIfEmpty(uniqueSnapshot.Values.FirstOrDefault())
                .Where(session => session is not null)
                .Cast<MediaSessionInfo>()
                .ToArray();

            foreach (AudioMediaSessionViewModel existingSession in MediaSessions)
            {
                existingSession.PropertyChanged -= MediaSessionViewModel_PropertyChanged;
            }

            MediaSessions.Clear();
            foreach (MediaSessionInfo info in visibleSessions)
            {
                if (existing.TryGetValue(info.SessionId, out AudioMediaSessionViewModel? viewModel))
                {
                    await viewModel.UpdateFromInfoAsync(info);
                    MediaSessions.Add(viewModel);
                    viewModel.PropertyChanged += MediaSessionViewModel_PropertyChanged;
                }
                else
                {
                    AudioMediaSessionViewModel newViewModel = new(info);
                    await newViewModel.UpdateFromInfoAsync(info);
                    MediaSessions.Add(newViewModel);
                    newViewModel.PropertyChanged += MediaSessionViewModel_PropertyChanged;
                }
            }

            SelectedMediaSession = MediaSessions.FirstOrDefault();

            UpdateSelectedMediaSessionState();
        }

        private void MediaSessionViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
        {
            if (!ReferenceEquals(sender, SelectedMediaSession))
            {
                return;
            }

            if (eventArgs.PropertyName is nameof(AudioMediaSessionViewModel.PlaybackStatus) or
                nameof(AudioMediaSessionViewModel.CanPlay) or
                nameof(AudioMediaSessionViewModel.CanPause) or
                nameof(AudioMediaSessionViewModel.CanSkipNext) or
                nameof(AudioMediaSessionViewModel.CanSkipPrevious))
            {
                UpdateSelectedMediaSessionState();
            }
        }

        private void UpdateSelectedMediaSessionState()
        {
            TogglePlaybackCommand.NotifyCanExecuteChanged();
            SkipNextCommand.NotifyCanExecuteChanged();
            SkipPreviousCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(HasMediaSession));
            OnPropertyChanged(nameof(CanTogglePlayback));
            OnPropertyChanged(nameof(CanSkipNext));
            OnPropertyChanged(nameof(CanSkipPrevious));
            OnPropertyChanged(nameof(PlayPauseGlyph));
            OnPropertyChanged(nameof(PlayPauseText));
        }

        private void ApplyHotkeyIfReady()
        {
            if (_suppressHotkeyApply)
            {
                return;
            }

            AudioMixerHotkey hotkey = BuildHotkey();
            if (!_hotkeyService.TryUpdateHotkey(hotkey))
            {
                ErrorMessage = "That hotkey could not be registered. It may already be in use by another app.";
                HasError = true;
                return;
            }

            HasError = false;
            ErrorMessage = string.Empty;
            HotkeyDisplayText = AudioMixerHotkeyService.Describe(hotkey);
            OnPropertyChanged(nameof(TipsMessage));
        }

        private void ApplyHotkeySnapshot(AudioMixerHotkey hotkey)
        {
            _suppressHotkeyApply = true;
            HotkeyCtrl = (hotkey.Modifiers & AudioMixerHotkeyService.ModControl) != 0;
            HotkeyShift = (hotkey.Modifiers & AudioMixerHotkeyService.ModShift) != 0;
            HotkeyAlt = (hotkey.Modifiers & AudioMixerHotkeyService.ModAlt) != 0;
            HotkeyWin = (hotkey.Modifiers & AudioMixerHotkeyService.ModWin) != 0;
            SelectedHotkeyKey = AudioMixerHotkeyService.DescribeVirtualKey(hotkey.VirtualKey);
            HotkeyDisplayText = AudioMixerHotkeyService.Describe(hotkey);
            _suppressHotkeyApply = false;
            OnPropertyChanged(nameof(TipsMessage));
        }

        private AudioMixerHotkey BuildHotkey()
        {
            uint modifiers = 0;
            if (HotkeyCtrl)
            {
                modifiers |= AudioMixerHotkeyService.ModControl;
            }

            if (HotkeyShift)
            {
                modifiers |= AudioMixerHotkeyService.ModShift;
            }

            if (HotkeyAlt)
            {
                modifiers |= AudioMixerHotkeyService.ModAlt;
            }

            if (HotkeyWin)
            {
                modifiers |= AudioMixerHotkeyService.ModWin;
            }

            return new AudioMixerHotkey(modifiers, ParseVirtualKey(SelectedHotkeyKey));
        }

        private static uint ParseVirtualKey(string? key) =>
            key switch
            {
                null or "" => AudioMixerHotkey.Default.VirtualKey,
                "F1" => 0x70,
                "F2" => 0x71,
                "F3" => 0x72,
                "F4" => 0x73,
                "F5" => 0x74,
                "F6" => 0x75,
                "F7" => 0x76,
                "F8" => 0x77,
                "F9" => 0x78,
                "F10" => 0x79,
                "F11" => 0x7A,
                "F12" => 0x7B,
                _ when key.Length == 1 => char.ToUpperInvariant(key[0]),
                _ => AudioMixerHotkey.Default.VirtualKey
            };
    }
}
