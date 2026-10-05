#nullable enable

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
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
        public event Action? DevicesChanged;

        private readonly VolumePolicy _policy;
        private readonly BlockingCollection<Action> _queue = new();
        private readonly Thread _thread;
        private Timer? _reconcileTimer;

        private MMDeviceEnumerator? _enumerator;
        private DeviceNotificationClient? _notificationClient;
        private MMDevice? _currentDevice;
        private AudioEndpointVolume? _endpointVolume;
        private string? _currentDeviceId;
        private readonly Dictionary<string, TrackedSession> _sessions = new(StringComparer.Ordinal);

        private readonly Dictionary<string, MonitoredEndpoint> _endpoints = new(StringComparer.Ordinal);
        private IReadOnlyList<AudioDeviceInfo> _outputDevices = [];
        private IReadOnlyList<AudioDeviceInfo> _inputDevices = [];

        private sealed record TrackedSession(
            AudioSessionControl Control,
            SessionEventsHandler Handler,
            string InstanceId,
            string Name,
            int ProcessId,
            string? ExecutablePath);

        private sealed record MonitoredEndpoint(MMDevice Device, AudioSessionManager Manager);

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
            _reconcileTimer = new Timer(_ => Post(ReconcileInternal), null, ReconcileInterval, ReconcileInterval);
        }

        public IReadOnlyList<AudioDeviceInfo> GetOutputDevices() => _outputDevices;

        public IReadOnlyList<AudioDeviceInfo> GetInputDevices() => _inputDevices;

        public void SetDefaultDevice(string deviceId, AudioDeviceDirection direction) => Post(() =>
        {
            try
            {
                PolicyConfigClient.SetDefaultEndpoint(deviceId);
                if (direction == AudioDeviceDirection.Render)
                {
                    SelectDeviceInternal(deviceId);
                }
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "Audio mixer could not switch the default device.");
                ErrorOccurred?.Invoke(string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    App.GetValueFromItemList("AudioMixerPage_DeviceSwitchFailed"),
                    exception.Message));
            }

            RefreshDeviceListsInternal();
            RefreshSessionsInternal();
        });

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

        public void RequestRefresh() => Post(ReconcileInternal);

        public void HandleSessionVolumeChanged(string instanceId, float volume) => Post(() =>
        {
            if (_sessions.TryGetValue(instanceId, out TrackedSession? session))
            {
                SessionVolumeChanged?.Invoke(session.Name, volume);
            }
        });

        public void HandleSessionExpired(string instanceId) => Post(() => RemoveSessionInternal(instanceId, true));

        public void HandleDefaultDeviceChanged(DataFlow flow, string defaultDeviceId) => Post(() =>
        {
            if (flow == DataFlow.Render && !string.Equals(defaultDeviceId, _currentDeviceId, StringComparison.Ordinal))
            {
                SelectDeviceInternal(defaultDeviceId);
            }

            RefreshDeviceListsInternal();
        });

        public void HandleDeviceTopologyChanged() => Post(() =>
        {
            RefreshEndpointsInternal();
            string? defaultId = TryGetDefaultDeviceId(DataFlow.Render);
            if (defaultId is not null && !string.Equals(defaultId, _currentDeviceId, StringComparison.Ordinal))
            {
                SelectDeviceInternal(defaultId);
            }

            RefreshDeviceListsInternal();
            RefreshSessionsInternal();
        });

        private void ReconcileInternal()
        {
            RefreshEndpointsInternal();
            RefreshSessionsInternal();
            RaiseSessionsSnapshot();
        }

        private void InitializeInternal()
        {
            _enumerator = new MMDeviceEnumerator();
            _notificationClient = new DeviceNotificationClient(this);
            _enumerator.RegisterEndpointNotificationCallback(_notificationClient);

            RefreshEndpointsInternal();
            RefreshDeviceListsInternal();

            string? defaultId = TryGetDefaultDeviceId(DataFlow.Render);
            if (defaultId is null)
            {
                ErrorOccurred?.Invoke(App.GetValueFromItemList("AudioMixerPage_NoOutputDevice"));
                return;
            }

            SelectDeviceInternal(defaultId);
            RaiseSessionsSnapshot();
        }

        private string? TryGetDefaultDeviceId(DataFlow flow)
        {
            try
            {
                using MMDevice defaultDevice = _enumerator!.GetDefaultAudioEndpoint(flow, Role.Multimedia);
                return defaultDevice.ID;
            }
            catch
            {
                return null;
            }
        }

        private void SelectDeviceInternal(string deviceId)
        {
            if (_enumerator is null)
            {
                return;
            }

            if (string.Equals(deviceId, _currentDeviceId, StringComparison.Ordinal) && _currentDevice is not null)
            {
                return;
            }

            ReleaseMasterVolumeDevice();

            try
            {
                _currentDevice = _enumerator.GetDevice(deviceId);
                _currentDeviceId = deviceId;

                _endpointVolume = _currentDevice.AudioEndpointVolume;
                _endpointVolume.OnVolumeNotification += OnMasterVolumeNotification;
                MasterVolumeChanged?.Invoke(_endpointVolume.MasterVolumeLevelScalar);
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "Audio mixer could not open the default output device.");
                _currentDeviceId = null;
                return;
            }

            RefreshSessionsInternal();
        }

        private void RefreshEndpointsInternal()
        {
            if (_enumerator is null)
            {
                return;
            }

            HashSet<string> activeIds = new(StringComparer.Ordinal);
            MMDeviceCollection? devices = null;

            try
            {
                devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                foreach (MMDevice device in devices)
                {
                    try
                    {
                        activeIds.Add(device.ID);
                    }
                    finally
                    {
                        Try(device.Dispose);
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "Audio mixer could not enumerate render endpoints.");
            }

            foreach (string id in activeIds)
            {
                if (_endpoints.ContainsKey(id))
                {
                    continue;
                }

                try
                {
                    MMDevice device = _enumerator.GetDevice(id);
                    AudioSessionManager manager = device.AudioSessionManager;
                    manager.OnSessionCreated += OnSessionCreatedCallback;
                    _endpoints[id] = new MonitoredEndpoint(device, manager);
                }
                catch (Exception exception)
                {
                    App.logger.Debug(exception, $"Audio mixer could not open endpoint {id}.");
                }
            }

            foreach (string id in _endpoints.Keys.Where(id => !activeIds.Contains(id)).ToList())
            {
                ReleaseEndpoint(id);
            }
        }

        private void RefreshDeviceListsInternal()
        {
            IReadOnlyList<AudioDeviceInfo> outputs = BuildDeviceList(DataFlow.Render, AudioDeviceDirection.Render);
            IReadOnlyList<AudioDeviceInfo> inputs = BuildDeviceList(DataFlow.Capture, AudioDeviceDirection.Capture);

            bool changed = !DeviceListsEqual(_outputDevices, outputs) || !DeviceListsEqual(_inputDevices, inputs);
            _outputDevices = outputs;
            _inputDevices = inputs;

            if (changed)
            {
                DevicesChanged?.Invoke();
            }
        }

        private List<AudioDeviceInfo> BuildDeviceList(DataFlow flow, AudioDeviceDirection direction)
        {
            var list = new List<AudioDeviceInfo>();
            if (_enumerator is null)
            {
                return list;
            }

            string? defaultId = TryGetDefaultDeviceId(flow);
            MMDeviceCollection? devices = null;

            try
            {
                devices = _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
                foreach (MMDevice device in devices)
                {
                    try
                    {
                        string id = device.ID;
                        string name = Try(() => device.FriendlyName, string.Empty);
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            name = App.GetValueFromItemList("AudioMixerPage_UnknownDevice");
                        }

                        list.Add(new AudioDeviceInfo(
                            id,
                            name,
                            direction,
                            string.Equals(id, defaultId, StringComparison.Ordinal)));
                    }
                    finally
                    {
                        Try(device.Dispose);
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "Audio mixer could not list audio devices.");
            }

            list.Sort((left, right) =>
            {
                if (left.IsDefault != right.IsDefault)
                {
                    return left.IsDefault ? -1 : 1;
                }

                return string.Compare(left.FriendlyName, right.FriendlyName, StringComparison.OrdinalIgnoreCase);
            });

            return list;
        }

        private static bool DeviceListsEqual(IReadOnlyList<AudioDeviceInfo> left, IReadOnlyList<AudioDeviceInfo> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int index = 0; index < left.Count; index++)
            {
                if (!string.Equals(left[index].Id, right[index].Id, StringComparison.Ordinal) ||
                    left[index].IsDefault != right[index].IsDefault ||
                    !string.Equals(left[index].FriendlyName, right[index].FriendlyName, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private void RefreshSessionsInternal()
        {
            if (_endpoints.Count == 0)
            {
                if (_sessions.Count > 0)
                {
                    foreach (string instanceId in _sessions.Keys.ToList())
                    {
                        RemoveSessionInternal(instanceId, false);
                    }

                    RaiseSessionsSnapshot();
                }

                return;
            }

            HashSet<string> seen = new(StringComparer.Ordinal);
            bool changed = false;

            foreach (MonitoredEndpoint endpoint in _endpoints.Values)
            {
                SessionCollection sessions;
                try
                {
                    endpoint.Manager.RefreshSessions();
                    sessions = endpoint.Manager.Sessions;
                }
                catch
                {
                    continue;
                }

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
            HashSet<string> liveKeys = new(StringComparer.OrdinalIgnoreCase);

            foreach (TrackedSession session in _sessions.Values)
            {
                float volume = Try(() => session.Control.SimpleAudioVolume.Volume, 0f);
                liveKeys.Add(NormalizeAppKey(session.Name));
                snapshot.Add(new AudioSessionInfo(
                    session.InstanceId,
                    session.Name,
                    volume,
                    string.Equals(session.Name, SessionNaming.SystemSoundsName, StringComparison.Ordinal),
                    session.ProcessId,
                    session.ExecutablePath,
                    true));
            }

            foreach (AudioAppProcessInfo app in OpenAppEnumerator.GetOpenApps())
            {
                if (liveKeys.Contains(NormalizeAppKey(app.Name)))
                {
                    continue;
                }

                snapshot.Add(new AudioSessionInfo(
                    "process:" + app.ProcessId,
                    app.Name,
                    _policy.GetDesiredVolume(app.Name),
                    false,
                    app.ProcessId,
                    app.ExecutablePath,
                    false));
            }

            SessionsChanged?.Invoke(snapshot);
        }

        private static string NormalizeAppKey(string name) =>
            name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

        private void ReleaseMasterVolumeDevice()
        {
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

        private void ReleaseEndpoint(string endpointId)
        {
            if (!_endpoints.Remove(endpointId, out MonitoredEndpoint? endpoint))
            {
                return;
            }

            Try(() => endpoint.Manager.OnSessionCreated -= OnSessionCreatedCallback);
            Try(endpoint.Manager.Dispose);
            Try(endpoint.Device.Dispose);
        }

        private void ReleaseEndpoints()
        {
            foreach (string instanceId in _sessions.Keys.ToList())
            {
                RemoveSessionInternal(instanceId, false);
            }

            foreach (string endpointId in _endpoints.Keys.ToList())
            {
                ReleaseEndpoint(endpointId);
            }
        }

        private void CleanupAllInternal()
        {
            ReleaseEndpoints();
            ReleaseMasterVolumeDevice();
        }

        private void OnSessionCreatedCallback(object sender, IAudioSessionControl newSession) => Post(RefreshSessionsInternal);

        private void OnMasterVolumeNotification(AudioVolumeNotificationData data) =>
            MasterVolumeChanged?.Invoke(data.MasterVolume);

        private void WorkerLoop()
        {
            int comResult = CoInitializeEx(IntPtr.Zero, CoInitMultithreaded);

            try
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
                        ErrorOccurred?.Invoke(string.Format(
                            System.Globalization.CultureInfo.CurrentCulture,
                            App.GetValueFromItemList("AudioMixerPage_WorkerError"),
                            exception.Message));
                    }
                }
            }
            finally
            {
                if (comResult >= 0)
                {
                    Try(CoUninitialize);
                }
            }

            try
            {
                CleanupAllInternal();

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

        private const uint CoInitMultithreaded = 0x0;

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();

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
