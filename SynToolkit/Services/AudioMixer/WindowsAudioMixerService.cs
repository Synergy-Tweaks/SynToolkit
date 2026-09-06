#nullable enable

namespace SynToolkit.Services.AudioMixer
{
    public sealed class WindowsAudioMixerService : IAudioMixerService
    {
        private readonly AudioMixerSettingsStore _settingsStore;
        private readonly VolumePolicy _volumePolicy;
        private readonly object _lock = new();
        private AudioSessionMonitor? _monitor;
        private bool _started;
        private List<AudioSessionInfo> _currentSessions = [];
        private float _masterVolume = 1f;

        public event Action<IReadOnlyList<AudioSessionInfo>>? SessionsChanged;
        public event Action<float>? MasterVolumeChanged;
        public event Action<string>? ErrorOccurred;

        public WindowsAudioMixerService(AudioMixerSettingsStore settingsStore)
        {
            _settingsStore = settingsStore;
            _volumePolicy = new VolumePolicy
            {
                DefaultVolumeScalar = settingsStore.GetDefaultVolumeScalar()
            };
            _volumePolicy.Load(settingsStore.GetSavedVolumes().ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase));
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_started)
                {
                    return;
                }

                _monitor = new AudioSessionMonitor(_volumePolicy);
                _monitor.SessionsChanged += HandleSessionsChanged;
                _monitor.MasterVolumeChanged += HandleMasterVolumeChanged;
                _monitor.ErrorOccurred += message => ErrorOccurred?.Invoke(message);
                _monitor.Start();
                _started = true;
            }
        }

        public void RequestRefresh() => _monitor?.RequestRefresh();

        public IReadOnlyList<AudioSessionInfo> GetCurrentSessions()
        {
            lock (_lock)
            {
                return _currentSessions.ToList();
            }
        }

        public IReadOnlyDictionary<string, float> GetSavedSessions() => _settingsStore.GetSavedVolumes();

        public float GetMasterVolume()
        {
            lock (_lock)
            {
                return _masterVolume;
            }
        }

        public float GetDefaultVolume() => _volumePolicy.DefaultVolumeScalar;

        public bool GetTipsDismissed() => _settingsStore.GetTipsDismissed();

        public void SetTipsDismissed(bool dismissed) => _settingsStore.SetTipsDismissed(dismissed);

        public void SetSessionVolume(string name, float scalarVolume)
        {
            float clamped = Math.Clamp(scalarVolume, 0f, 1f);
            _volumePolicy.SetSaved(name, clamped);
            _settingsStore.SaveSessionVolume(name, clamped);
            _monitor?.SetSessionVolume(name, clamped);
        }

        public void RemoveSavedSession(string name)
        {
            _volumePolicy.RemoveSaved(name);
            _settingsStore.RemoveSavedSession(name);
        }

        public void SetMasterVolume(float scalarVolume)
        {
            float clamped = Math.Clamp(scalarVolume, 0f, 1f);
            lock (_lock)
            {
                _masterVolume = clamped;
            }

            _monitor?.SetMasterVolume(clamped);
            MasterVolumeChanged?.Invoke(clamped);
        }

        public void SetDefaultVolume(float scalarVolume)
        {
            float clamped = Math.Clamp(scalarVolume, 0f, 1f);
            _volumePolicy.DefaultVolumeScalar = clamped;
            _settingsStore.SetDefaultVolumeScalar(clamped);
        }

        private void HandleSessionsChanged(IReadOnlyList<AudioSessionInfo> sessions)
        {
            lock (_lock)
            {
                _currentSessions = sessions.ToList();
            }

            SessionsChanged?.Invoke(sessions);
        }

        private void HandleMasterVolumeChanged(float scalar)
        {
            lock (_lock)
            {
                _masterVolume = Math.Clamp(scalar, 0f, 1f);
            }

            MasterVolumeChanged?.Invoke(_masterVolume);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _monitor?.Dispose();
                _monitor = null;
                _started = false;
            }
        }
    }
}
