#nullable enable

namespace SynToolkit.Services.AudioMixer
{
    public interface IAudioMixerService : IDisposable
    {
        event Action<IReadOnlyList<AudioSessionInfo>>? SessionsChanged;
        event Action<float>? MasterVolumeChanged;
        event Action<string>? ErrorOccurred;

        void Start();
        void RequestRefresh();

        IReadOnlyList<AudioSessionInfo> GetCurrentSessions();
        IReadOnlyDictionary<string, float> GetSavedSessions();
        float GetMasterVolume();
        float GetDefaultVolume();
        bool GetTipsDismissed();
        void SetTipsDismissed(bool dismissed);

        void SetSessionVolume(string name, float scalarVolume);
        void RemoveSavedSession(string name);
        void SetMasterVolume(float scalarVolume);
        void SetDefaultVolume(float scalarVolume);
    }
}
