#nullable enable

using System.Collections.Concurrent;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace SynToolkit.Services.AudioMixer
{
    internal sealed class AudioSessionMonitor : IDisposable
    {
        private static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(30);

        public event Action<float>? MasterVolumeChanged;
        public event Action<IReadOnlyList<AudioSessionInfo>>? SessionsChanged;
        public event Action<string, float>? SessionVolumeChanged;
        public event Action<string>? ErrorOccurred;

        private readonly VolumePolicy _policy;
        private readonly BlockingCollection<Action> _queue = new();
        private readonly Thread _thread;
        private Timer? _reconcileTimer;

        private MMDeviceEnumerator? _enumerator;
        private DeviceNotificationClient? _notificationClient;
        private MMDevice? _currentDevice;
        private AudioSessionManager? _sessionManager;
        private AudioEndpointVolume? _endpointVolume;
        private string? _currentDeviceId;
        private readonly Dictionary<string, TrackedSession> _sessions = new(StringComparer.Ordinal);

        private sealed record TrackedSession(
            AudioSessionControl Control,
            SessionEventsHandler Handler,
            string InstanceId,
            string Name,
            int ProcessId,
            string? ExecutablePath);

        public AudioSessionMonitor(VolumePolicy policy)
        {
            _policy = policy;
            _thread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "SynToolkitAudioMixer"
            };
            _thread.SetApartmentState(ApartmentState.MTA);
        }

        public void Start()
        {
            _thread.Start();
            Post(InitializeInternal);
            _reconcileTimer = new Timer(_ => Post(RefreshSessionsInternal), null, ReconcileInterval, ReconcileInterval);
        }

        public void SetMasterVolume(float scalar) => Post(() =>
        {
            if (_endpointVolume is not null)
            {
                _endpointVolume.MasterVolumeLevelScalar = Math.Clamp(scalar, 0f, 1f);
            }
        });

        public void SetSessionVolume(string name, float scalar) => Post(() =>
        {
            float clamped = Math.Clamp(scalar, 0f, 1f);
            foreach (TrackedSession session in _sessions.Values)
            {
                if (string.Equals(session.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    Try(() => session.Control.SimpleAudioVolume.Volume = clamped);
                }
            }
        });

        public void RequestRefresh() => Post(RefreshSessionsInternal);

        public void HandleSessionVolumeChanged(string instanceId, float volume) => Post(() =>
        {
            if (_sessions.TryGetValue(instanceId, out TrackedSession? session))
            {
                SessionVolumeChanged?.Invoke(session.Name, volume);
            }
        });

        public void HandleSessionExpired(string instanceId) => Post(() => RemoveSessionInternal(instanceId, true));

        public void HandleDefaultDeviceChanged(string defaultDeviceId) => Post(() =>
        {
            if (!string.Equals(defaultDeviceId, _currentDeviceId, StringComparison.Ordinal))
            {
                SelectDeviceInternal(defaultDeviceId);
            }
        });

        public void HandleDeviceTopologyChanged() => Post(() =>
        {
            string? defaultId = GetDefaultDeviceId();
            if (defaultId is not null && !string.Equals(defaultId, _currentDeviceId, StringComparison.Ordinal))
            {
                SelectDeviceInternal(defaultId);
            }
            else
            {
                RefreshSessionsInternal();
            }
        });

        private void InitializeInternal()
        {
            _enumerator = new MMDeviceEnumerator();
            _notificationClient = new DeviceNotificationClient(this);
            _enumerator.RegisterEndpointNotificationCallback(_notificationClient);

            string? defaultId = GetDefaultDeviceId();
            if (defaultId is null)
            {
                ErrorOccurred?.Invoke("No active audio output device found.");
                return;
            }

            SelectDeviceInternal(defaultId);
        }

        private string? GetDefaultDeviceId()
        {
            try
            {
                using MMDevice defaultDevice = _enumerator!.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                return defaultDevice.ID;
            }
            catch
            {
                return null;
            }
        }

        private void SelectDeviceInternal(string deviceId)
        {
            if (_enumerator is null || string.Equals(deviceId, _currentDeviceId, StringComparison.Ordinal))
            {
                return;
            }

            CleanupDeviceInternal();

            _currentDevice = _enumerator.GetDevice(deviceId);
            _currentDeviceId = deviceId;

            _endpointVolume = _currentDevice.AudioEndpointVolume;
            _endpointVolume.OnVolumeNotification += OnMasterVolumeNotification;

            _sessionManager = _currentDevice.AudioSessionManager;
            _sessionManager.OnSessionCreated += OnSessionCreatedCallback;

            RefreshSessionsInternal();
            MasterVolumeChanged?.Invoke(_endpointVolume.MasterVolumeLevelScalar);
        }

        private void RefreshSessionsInternal()
        {
            if (_sessionManager is null)
            {
                SessionsChanged?.Invoke([]);
                return;
            }

            _sessionManager.RefreshSessions();
            SessionCollection sessions = _sessionManager.Sessions;
            HashSet<string> seen = new(StringComparer.Ordinal);
            bool changed = false;

            for (int index = 0; index < sessions.Count; index++)
            {
                AudioSessionControl control;
                try
                {
                    control = sessions[index];
                }
                catch
                {
                    continue;
                }

                string? instanceId = Try(() => control.GetSessionInstanceIdentifier);
                if (string.IsNullOrWhiteSpace(instanceId) || !seen.Add(instanceId) || IsExpired(control))
                {
                    control.Dispose();
                    continue;
                }

                if (_sessions.ContainsKey(instanceId))
                {
                    control.Dispose();
                    continue;
                }

                TrackSessionInternal(control, instanceId);
                changed = true;
            }

            foreach (string staleId in _sessions.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                RemoveSessionInternal(staleId, false);
                changed = true;
            }

            if (changed)
            {
                RaiseSessionsSnapshot();
            }
        }

        private void TrackSessionInternal(AudioSessionControl control, string instanceId)
        {
            string name = SessionNaming.Resolve(control);
            Try(() => control.SimpleAudioVolume.Volume = _policy.GetDesiredVolume(name));

            uint rawProcessId = Try(() => control.GetProcessID, 0u);
            int processId = rawProcessId > int.MaxValue ? 0 : (int)rawProcessId;
            string? executablePath = ResolveExecutablePath(processId);

            SessionEventsHandler handler = new(this, instanceId);
            Try(() => control.RegisterEventClient(handler));

            _sessions[instanceId] = new TrackedSession(control, handler, instanceId, name, processId, executablePath);
        }

        private static string? ResolveExecutablePath(int processId)
        {
            if (processId <= 0)
            {
                return null;
            }

            try
            {
                using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(processId);
                return process.MainModule?.FileName;
            }
            catch
            {
                return null;
            }
        }

        private void RemoveSessionInternal(string instanceId, bool raiseSnapshot)
        {
            if (!_sessions.Remove(instanceId, out TrackedSession? session))
            {
                return;
            }

            Try(() => session.Control.UnRegisterEventClient(session.Handler));
            Try(session.Control.Dispose);

            if (raiseSnapshot)
            {
                RaiseSessionsSnapshot();
            }
        }

        private void RaiseSessionsSnapshot()
        {
            List<AudioSessionInfo> snapshot = [];

            foreach (TrackedSession session in _sessions.Values)
            {
                float volume = Try(() => session.Control.SimpleAudioVolume.Volume, 0f);
                snapshot.Add(new AudioSessionInfo(
                    session.InstanceId,
                    session.Name,
                    volume,
                    string.Equals(session.Name, SessionNaming.SystemSoundsName, StringComparison.Ordinal),
                    session.ProcessId,
                    session.ExecutablePath));
            }

            SessionsChanged?.Invoke(snapshot);
        }

        private void CleanupDeviceInternal()
        {
            foreach (string instanceId in _sessions.Keys.ToList())
            {
                RemoveSessionInternal(instanceId, false);
            }

            if (_sessionManager is not null)
            {
                _sessionManager.OnSessionCreated -= OnSessionCreatedCallback;
                Try(_sessionManager.Dispose);
                _sessionManager = null;
            }

            if (_endpointVolume is not null)
            {
                _endpointVolume.OnVolumeNotification -= OnMasterVolumeNotification;
                Try(_endpointVolume.Dispose);
                _endpointVolume = null;
            }

            if (_currentDevice is not null)
            {
                Try(_currentDevice.Dispose);
                _currentDevice = null;
            }

            _currentDeviceId = null;
        }

        private void OnSessionCreatedCallback(object sender, IAudioSessionControl newSession) => Post(RefreshSessionsInternal);

        private void OnMasterVolumeNotification(AudioVolumeNotificationData data) =>
            MasterVolumeChanged?.Invoke(data.MasterVolume);

        private void WorkerLoop()
        {
            foreach (Action action in _queue.GetConsumingEnumerable())
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    App.logger.Error(exception, "Audio mixer worker action failed.");
                    ErrorOccurred?.Invoke("Audio mixer error: " + exception.Message);
                }
            }

            try
            {
                CleanupDeviceInternal();

                if (_enumerator is not null)
                {
                    if (_notificationClient is not null)
                    {
                        Try(() => _enumerator.UnregisterEndpointNotificationCallback(_notificationClient));
                    }

                    Try(_enumerator.Dispose);
                    _enumerator = null;
                }
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "Audio mixer cleanup failed.");
            }
        }

        private void Post(Action action)
        {
            try
            {
                if (!_queue.IsAddingCompleted)
                {
                    _queue.Add(action);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        private static void Try(Action action)
        {
            try
            {
                action();
            }
            catch
            {
            }
        }

        private static T? Try<T>(Func<T> func)
        {
            try
            {
                return func();
            }
            catch
            {
                return default;
            }
        }

        private static T Try<T>(Func<T> func, T fallback)
        {
            try
            {
                return func();
            }
            catch
            {
                return fallback;
            }
        }

        private static bool IsExpired(AudioSessionControl control)
        {
            try
            {
                return control.State == AudioSessionState.AudioSessionStateExpired;
            }
            catch
            {
                return true;
            }
        }

        public void Dispose()
        {
            _reconcileTimer?.Dispose();
            _queue.CompleteAdding();

            if (_thread.IsAlive)
            {
                _thread.Join(TimeSpan.FromSeconds(3));
            }

            _queue.Dispose();
        }
    }
}
