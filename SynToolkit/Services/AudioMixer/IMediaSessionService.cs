#nullable enable

namespace SynToolkit.Services.AudioMixer
{
    public interface IMediaSessionService : IDisposable
    {
        event Action<IReadOnlyList<MediaSessionInfo>>? SessionsChanged;
        event Action<string>? ErrorOccurred;

        Task StartAsync();
        void RequestRefresh();

        IReadOnlyList<MediaSessionInfo> GetSessions();
        MediaSessionInfo? GetCurrentSession();

        Task<bool> TryPlayAsync(string sessionId);
        Task<bool> TryPauseAsync(string sessionId);
        Task<bool> TryTogglePlayPauseAsync(string sessionId);
        Task<bool> TrySkipNextAsync(string sessionId);
        Task<bool> TrySkipPreviousAsync(string sessionId);
    }
}
