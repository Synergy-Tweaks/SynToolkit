#nullable enable

using Windows.Media.Control;

namespace SynToolkit.Services.AudioMixer
{
    public sealed record MediaSessionInfo(
        string SessionId,
        string SourceAppId,
        string SourceAppName,
        string Title,
        string Artist,
        string Subtitle,
        bool IsCurrent,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus PlaybackStatus,
        bool CanPlay,
        bool CanPause,
        bool CanSkipNext,
        bool CanSkipPrevious,
        byte[]? ThumbnailBytes,
        byte[]? AppIconBytes,
        string? SourceExecutablePath);
}
