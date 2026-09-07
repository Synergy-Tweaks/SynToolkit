#nullable enable

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace SynToolkit.Services.AudioMixer
{
    public sealed class WindowsMediaSessionService : IMediaSessionService
    {
        private readonly object _lock = new();
        private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
        private readonly Dictionary<string, GlobalSystemMediaTransportControlsSession> _sessionsById = new(StringComparer.Ordinal);
        private readonly Dictionary<nint, TrackedMediaSession> _trackedSessions = [];
        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private List<MediaSessionInfo> _sessions = [];
        private long _nextSessionId;
        private bool _started;
        private bool _disposed;

        public event Action<IReadOnlyList<MediaSessionInfo>>? SessionsChanged;
        public event Action<string>? ErrorOccurred;

        public async Task StartAsync()
        {
            ThrowIfDisposed();

            if (_started)
            {
                await RefreshInternalAsync();
                return;
            }

            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.SessionsChanged += Manager_SessionsChanged;
            _manager.CurrentSessionChanged += Manager_CurrentSessionChanged;
            _started = true;

            await RefreshInternalAsync();
        }

        public void RequestRefresh()
        {
            if (_disposed || !_started)
            {
                return;
            }

            _ = RefreshInternalAsync();
        }

        public IReadOnlyList<MediaSessionInfo> GetSessions()
        {
            lock (_lock)
            {
                return new ReadOnlyCollection<MediaSessionInfo>(_sessions.ToList());
            }
        }

        public MediaSessionInfo? GetCurrentSession()
        {
            lock (_lock)
            {
                return _sessions.FirstOrDefault(session => session.IsCurrent) ?? _sessions.FirstOrDefault();
            }
        }

        public Task<bool> TryPlayAsync(string sessionId) =>
            TryInvokeAsync(sessionId, async session => await session.TryPlayAsync());

        public Task<bool> TryPauseAsync(string sessionId) =>
            TryInvokeAsync(sessionId, async session => await session.TryPauseAsync());

        public async Task<bool> TryTogglePlayPauseAsync(string sessionId)
        {
            GlobalSystemMediaTransportControlsSession? session = GetSession(sessionId);
            if (session is null)
            {
                return false;
            }

            try
            {
                GlobalSystemMediaTransportControlsSessionPlaybackStatus status =
                    session.GetPlaybackInfo()?.PlaybackStatus ??
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed;

                return status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                    ? await session.TryPauseAsync()
                    : await session.TryPlayAsync();
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "Media session toggle failed for {SessionId}.", sessionId);
                ErrorOccurred?.Invoke("Unable to control the current media session.");
                return false;
            }
        }

        public Task<bool> TrySkipNextAsync(string sessionId) =>
            TryInvokeAsync(sessionId, async session => await session.TrySkipNextAsync());

        public Task<bool> TrySkipPreviousAsync(string sessionId) =>
            TryInvokeAsync(sessionId, async session => await session.TrySkipPreviousAsync());

        private async Task<bool> TryInvokeAsync(
            string sessionId,
            Func<GlobalSystemMediaTransportControlsSession, Task<bool>> action)
        {
            GlobalSystemMediaTransportControlsSession? session = GetSession(sessionId);
            if (session is null)
            {
                return false;
            }

            try
            {
                return await action(session);
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "Media session control failed for {SessionId}.", sessionId);
                ErrorOccurred?.Invoke("Unable to control the selected media session.");
                return false;
            }
        }

        private GlobalSystemMediaTransportControlsSession? GetSession(string sessionId)
        {
            lock (_lock)
            {
                return _sessionsById.TryGetValue(sessionId, out GlobalSystemMediaTransportControlsSession? session)
                    ? session
                    : null;
            }
        }

        private async Task RefreshInternalAsync()
        {
            if (_disposed || !_started || _manager is null)
            {
                return;
            }

            await _refreshSemaphore.WaitAsync();
            try
            {
                IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions = _manager.GetSessions();
                GlobalSystemMediaTransportControlsSession? currentSession = _manager.GetCurrentSession();

                IReadOnlyList<TrackedMediaSession> trackedSessions = ReconcileSessionSubscriptions(sessions);

                Dictionary<string, MediaSessionInfo> snapshotById = new(StringComparer.Ordinal);
                List<nint> failedSessionIdentityKeys = [];
                foreach (TrackedMediaSession trackedSession in trackedSessions)
                {
                    MediaSessionInfo mediaSession;
                    try
                    {
                        mediaSession = await CreateSnapshotAsync(
                            trackedSession.Session,
                            trackedSession.SessionId,
                            SameSession(trackedSession.Session, currentSession));
                    }
                    catch (Exception exception)
                    {
                        App.logger.Warn(
                            exception,
                            "Skipping unreadable media session {SessionId}.",
                            trackedSession.SessionId);
                        failedSessionIdentityKeys.Add(trackedSession.IdentityKey);
                        continue;
                    }

                    if (!snapshotById.TryAdd(mediaSession.SessionId, mediaSession))
                    {
                        App.logger.Warn(
                            "Duplicate media session id {SessionId} encountered during refresh. Keeping the latest snapshot.",
                            mediaSession.SessionId);
                        snapshotById[mediaSession.SessionId] = mediaSession;
                    }
                }

                foreach (nint failedSessionIdentityKey in failedSessionIdentityKeys)
                {
                    RemoveTrackedSession(failedSessionIdentityKey);
                }

                List<MediaSessionInfo> snapshot = snapshotById.Values.ToList();

                snapshot.Sort((left, right) =>
                {
                    if (left.IsCurrent != right.IsCurrent)
                    {
                        return left.IsCurrent ? -1 : 1;
                    }

                    bool leftPlaying = left.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    bool rightPlaying = right.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    if (leftPlaying != rightPlaying)
                    {
                        return leftPlaying ? -1 : 1;
                    }

                    return string.Compare(left.SourceAppName, right.SourceAppName, StringComparison.OrdinalIgnoreCase);
                });

                lock (_lock)
                {
                    _sessions = snapshot;
                }

                ErrorOccurred?.Invoke(string.Empty);
                SessionsChanged?.Invoke(snapshot);
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "Unable to refresh media transport sessions.");
                ErrorOccurred?.Invoke("Unable to read active media sessions.");
            }
            finally
            {
                _refreshSemaphore.Release();
            }
        }

        private IReadOnlyList<TrackedMediaSession> ReconcileSessionSubscriptions(IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions)
        {
            HashSet<nint> activeIdentityKeys = [];
            List<TrackedMediaSession> activeTrackedSessions = [];
            _sessionsById.Clear();

            foreach (GlobalSystemMediaTransportControlsSession session in sessions)
            {
                nint identityKey = GetSessionIdentityKey(session);
                if (!activeIdentityKeys.Add(identityKey))
                {
                    App.logger.Warn(
                        "Duplicate media session COM identity {IdentityKey} encountered during refresh; ignoring duplicate wrapper.",
                        identityKey);
                    continue;
                }

                if (_trackedSessions.TryGetValue(identityKey, out TrackedMediaSession? trackedSession))
                {
                    if (!ReferenceEquals(trackedSession.Session, session))
                    {
                        trackedSession.Session.MediaPropertiesChanged -= Session_MediaPropertiesChanged;
                        trackedSession.Session.PlaybackInfoChanged -= Session_PlaybackInfoChanged;
                        trackedSession.Session = session;
                        session.MediaPropertiesChanged += Session_MediaPropertiesChanged;
                        session.PlaybackInfoChanged += Session_PlaybackInfoChanged;
                    }

                    _sessionsById[trackedSession.SessionId] = session;
                    activeTrackedSessions.Add(trackedSession);
                    continue;
                }

                TrackedMediaSession newTrackedSession = new(identityKey, CreateSessionId(session.SourceAppUserModelId), session);
                _trackedSessions[identityKey] = newTrackedSession;
                _sessionsById[newTrackedSession.SessionId] = session;
                session.MediaPropertiesChanged += Session_MediaPropertiesChanged;
                session.PlaybackInfoChanged += Session_PlaybackInfoChanged;
                activeTrackedSessions.Add(newTrackedSession);
            }

            foreach (nint staleIdentityKey in _trackedSessions.Keys.Where(key => !activeIdentityKeys.Contains(key)).ToList())
            {
                TrackedMediaSession staleSession = _trackedSessions[staleIdentityKey];
                staleSession.Session.MediaPropertiesChanged -= Session_MediaPropertiesChanged;
                staleSession.Session.PlaybackInfoChanged -= Session_PlaybackInfoChanged;
                _trackedSessions.Remove(staleIdentityKey);
                _sessionsById.Remove(staleSession.SessionId);
            }

            return activeTrackedSessions;
        }

        private void RemoveTrackedSession(nint identityKey)
        {
            if (!_trackedSessions.TryGetValue(identityKey, out TrackedMediaSession? trackedSession))
            {
                return;
            }

            trackedSession.Session.MediaPropertiesChanged -= Session_MediaPropertiesChanged;
            trackedSession.Session.PlaybackInfoChanged -= Session_PlaybackInfoChanged;
            _trackedSessions.Remove(identityKey);
            _sessionsById.Remove(trackedSession.SessionId);
        }

        private async Task<MediaSessionInfo> CreateSnapshotAsync(
            GlobalSystemMediaTransportControlsSession session,
            string sessionId,
            bool isCurrent)
        {
            string sourceAppId = session.SourceAppUserModelId ?? string.Empty;
            ResolvedSourceApp sourceApp = await ResolveSourceAppAsync(sourceAppId);

            GlobalSystemMediaTransportControlsSessionMediaProperties mediaProperties =
                await session.TryGetMediaPropertiesAsync();
            GlobalSystemMediaTransportControlsSessionPlaybackInfo playbackInfo = session.GetPlaybackInfo();

            return new MediaSessionInfo(
                sessionId,
                sourceAppId,
                sourceApp.DisplayName,
                mediaProperties.Title ?? string.Empty,
                mediaProperties.Artist ?? string.Empty,
                BuildSubtitle(mediaProperties),
                isCurrent,
                playbackInfo?.PlaybackStatus ?? GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed,
                playbackInfo?.Controls?.IsPlayEnabled == true,
                playbackInfo?.Controls?.IsPauseEnabled == true,
                playbackInfo?.Controls?.IsNextEnabled == true,
                playbackInfo?.Controls?.IsPreviousEnabled == true,
                await LoadBytesAsync(mediaProperties.Thumbnail),
                sourceApp.AppIconBytes,
                sourceApp.ExecutablePath);
        }

        private static string BuildSubtitle(GlobalSystemMediaTransportControlsSessionMediaProperties mediaProperties)
        {
            if (!string.IsNullOrWhiteSpace(mediaProperties.Artist))
            {
                return mediaProperties.Artist;
            }

            if (!string.IsNullOrWhiteSpace(mediaProperties.AlbumTitle))
            {
                return mediaProperties.AlbumTitle;
            }

            return "Media session active";
        }

        private static async Task<ResolvedSourceApp> ResolveSourceAppAsync(string sourceAppId)
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                ResolvedSourceApp? packagedApp = await TryResolvePackagedAppAsync(sourceAppId);
                if (packagedApp is not null)
                {
                    return packagedApp;
                }
            }

            return ResolveDesktopSourceApp(sourceAppId);
        }

        [SupportedOSPlatform("windows10.0.19041.0")]
        private static async Task<ResolvedSourceApp?> TryResolvePackagedAppAsync(string sourceAppId)
        {
            if (string.IsNullOrWhiteSpace(sourceAppId))
            {
                return null;
            }

            try
            {
                AppInfo appInfo = AppInfo.GetFromAppUserModelId(sourceAppId);
                string displayName = appInfo.DisplayInfo.DisplayName;
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    return null;
                }

                return new ResolvedSourceApp(
                    displayName,
                    await LoadBytesAsync(appInfo.DisplayInfo.GetLogo(new Size(48, 48))),
                    null);
            }
            catch
            {
                return null;
            }
        }

        private static ResolvedSourceApp ResolveDesktopSourceApp(string sourceAppId)
        {
            string normalizedId = sourceAppId.Contains('!')
                ? sourceAppId[..sourceAppId.IndexOf('!')]
                : sourceAppId;

            string? executablePath = TryResolveExecutablePath(normalizedId);
            string displayName = TryResolveExecutableDisplayName(executablePath)
                ?? HumanizeProcessName(Path.GetFileNameWithoutExtension(normalizedId))
                ?? ResolveFallbackSourceAppName(sourceAppId);

            return new ResolvedSourceApp(displayName, null, executablePath);
        }

        [SupportedOSPlatform("windows10.0.19041.0")]
        private static string ResolveSourceAppName(string sourceAppId)
        {
            if (!string.IsNullOrWhiteSpace(sourceAppId))
            {
                try
                {
                    AppInfo appInfo = AppInfo.GetFromAppUserModelId(sourceAppId);
                    string displayName = appInfo.DisplayInfo.DisplayName;
                    if (!string.IsNullOrWhiteSpace(displayName))
                    {
                        return displayName;
                    }
                }
                catch
                {
                }

                return ResolveFallbackSourceAppName(sourceAppId);
            }

            return "Unknown player";
        }

        private static string ResolveFallbackSourceAppName(string sourceAppId)
        {
            if (string.IsNullOrWhiteSpace(sourceAppId))
            {
                return "Unknown player";
            }

            string compactId = sourceAppId.Contains('!')
                ? sourceAppId[..sourceAppId.IndexOf('!')]
                : sourceAppId;
            if (compactId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                compactId.Contains('\\') ||
                compactId.Contains('/'))
            {
                string fileName = Path.GetFileNameWithoutExtension(compactId);
                return string.IsNullOrWhiteSpace(fileName) ? "Unknown player" : fileName;
            }

            return compactId.Split('.').FirstOrDefault(segment => !string.IsNullOrWhiteSpace(segment))
                ?? "Unknown player";
        }

        [SupportedOSPlatform("windows10.0.19041.0")]
        private static async Task<byte[]?> LoadAppIconBytesAsync(string sourceAppId)
        {
            if (string.IsNullOrWhiteSpace(sourceAppId))
            {
                return null;
            }

            try
            {
                AppInfo appInfo = AppInfo.GetFromAppUserModelId(sourceAppId);
                return await LoadBytesAsync(appInfo.DisplayInfo.GetLogo(new Size(48, 48)));
            }
            catch
            {
                return null;
            }
        }

        private static async Task<byte[]?> LoadBytesAsync(IRandomAccessStreamReference? reference)
        {
            if (reference is null)
            {
                return null;
            }

            try
            {
                using IRandomAccessStreamWithContentType stream = await reference.OpenReadAsync();
                if (stream.Size == 0)
                {
                    return null;
                }

                byte[] bytes = new byte[stream.Size];
                using DataReader reader = new(stream.GetInputStreamAt(0));
                await reader.LoadAsync((uint)stream.Size);
                reader.ReadBytes(bytes);
                return bytes;
            }
            catch
            {
                return null;
            }
        }

        private string CreateSessionId(string? sourceAppUserModelId)
        {
            long sequence = Interlocked.Increment(ref _nextSessionId);
            string prefix = string.IsNullOrWhiteSpace(sourceAppUserModelId)
                ? "MediaSession"
                : sourceAppUserModelId;
            return $"{prefix}|{sequence}";
        }

        private static nint GetSessionIdentityKey(GlobalSystemMediaTransportControlsSession session)
        {
            nint unknown = IntPtr.Zero;
            try
            {
                unknown = Marshal.GetIUnknownForObject(session);
                return unknown;
            }
            finally
            {
                if (unknown != IntPtr.Zero)
                {
                    Marshal.Release(unknown);
                }
            }
        }

        private static bool SameSession(
            GlobalSystemMediaTransportControlsSession? left,
            GlobalSystemMediaTransportControlsSession? right) =>
            left is not null &&
            right is not null &&
            GetSessionIdentityKey(left) == GetSessionIdentityKey(right);

        private static string? TryResolveExecutablePath(string sourceAppId)
        {
            if (string.IsNullOrWhiteSpace(sourceAppId))
            {
                return null;
            }

            string expanded = Environment.ExpandEnvironmentVariables(sourceAppId.Trim('"'));
            if ((expanded.Contains('\\') || expanded.Contains('/')) &&
                expanded.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return File.Exists(expanded) ? expanded : null;
            }

            string processName = Path.GetFileNameWithoutExtension(expanded);
            if (string.IsNullOrWhiteSpace(processName))
            {
                return null;
            }

            foreach (Process process in Process.GetProcessesByName(processName))
            {
                try
                {
                    using (process)
                    {
                        string? path = process.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                        {
                            return path;
                        }
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static string? TryResolveExecutableDisplayName(string? executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                return null;
            }

            try
            {
                FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
                if (!string.IsNullOrWhiteSpace(versionInfo.ProductName))
                {
                    return versionInfo.ProductName;
                }

                if (!string.IsNullOrWhiteSpace(versionInfo.FileDescription))
                {
                    return versionInfo.FileDescription;
                }
            }
            catch
            {
            }

            return HumanizeProcessName(Path.GetFileNameWithoutExtension(executablePath));
        }

        private static string? HumanizeProcessName(string? processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                return null;
            }

            string cleaned = processName.Replace('-', ' ').Replace('_', ' ').Trim();
            if (cleaned.Length == 0)
            {
                return null;
            }

            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(cleaned);
        }

        private void Manager_SessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) =>
            RequestRefresh();

        private void Manager_CurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) =>
            RequestRefresh();

        private void Session_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) =>
            RequestRefresh();

        private void Session_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
            RequestRefresh();

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_manager is not null)
            {
                _manager.SessionsChanged -= Manager_SessionsChanged;
                _manager.CurrentSessionChanged -= Manager_CurrentSessionChanged;
            }

            foreach (TrackedMediaSession trackedSession in _trackedSessions.Values.ToList())
            {
                trackedSession.Session.MediaPropertiesChanged -= Session_MediaPropertiesChanged;
                trackedSession.Session.PlaybackInfoChanged -= Session_PlaybackInfoChanged;
            }

            lock (_lock)
            {
                _sessionsById.Clear();
                _trackedSessions.Clear();
                _sessions.Clear();
            }

            _refreshSemaphore.Dispose();
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        private sealed record ResolvedSourceApp(
            string DisplayName,
            byte[]? AppIconBytes,
            string? ExecutablePath);

        private sealed class TrackedMediaSession
        {
            public TrackedMediaSession(nint identityKey, string sessionId, GlobalSystemMediaTransportControlsSession session)
            {
                IdentityKey = identityKey;
                SessionId = sessionId;
                Session = session;
            }

            public nint IdentityKey { get; }

            public string SessionId { get; }

            public GlobalSystemMediaTransportControlsSession Session { get; set; }
        }
    }
}
