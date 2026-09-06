#nullable enable

using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace SynToolkit.Services.AudioMixer
{
    internal sealed class DeviceNotificationClient : IMMNotificationClient
    {
        private readonly AudioSessionMonitor _service;

        public DeviceNotificationClient(AudioSessionMonitor service)
        {
            _service = service;
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia)
            {
                _service.HandleDefaultDeviceChanged(defaultDeviceId);
            }
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => _service.HandleDeviceTopologyChanged();

        public void OnDeviceAdded(string pwstrDeviceId) => _service.HandleDeviceTopologyChanged();

        public void OnDeviceRemoved(string deviceId) => _service.HandleDeviceTopologyChanged();

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
        }
    }
}
