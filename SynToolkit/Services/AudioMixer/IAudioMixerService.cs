#nullable enable

namespace SynToolkit.Services.AudioMixer
{
    public interface IAudioMixerService : IDisposable
    {
        event Action<IReadOnlyList<AudioSessionInfo>>? SessionsChanged;
        event Action<float>? MasterVolumeChanged;
        event Action<string>? ErrorOccurred;
        event Action? DevicesChanged;

        void Start();
        void RequestRefresh();

        IReadOnlyList<AudioSessionInfo> GetCurrentSessions();
        IReadOnlyDictionary<string, float> GetSavedSessions();
        IReadOnlyList<AudioDeviceInfo> GetOutputDevices();
        IReadOnlyList<AudioDeviceInfo> GetInputDevices();
        float GetMasterVolume();
        float GetDefaultVolume();
        bool GetTipsDismissed();
        void SetTipsDismissed(bool dismissed);

        void SetSessionVolume(string name, float scalarVolume);
        void RemoveSavedSession(string name);
        void SetMasterVolume(float scalarVolume);
        void SetDefaultVolume(float scalarVolume);
        void SetDefaultDevice(string deviceId, AudioDeviceDirection direction);
    }
}
