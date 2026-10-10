#nullable enable

using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SynToolkit.Services.SynergyOsUpdate
{
    public enum SynergyOsUpdateCheckOutcome
    {
        SkippedNotSynergyOs,
        SkippedDisabled,
        SkippedThrottled,
        SkippedRateLimited,
        UpToDate,
        UpdateAvailable,
        Failed,
        NoReleases
    }

    public sealed record SynergyOsUpdateCheckResult(
        SynergyOsUpdateCheckOutcome Outcome,
        string? InstalledVersion,
        SynergyOsCachedRelease? Latest,
        SynergyOsUpdateNotifyReason NotifyReason,
        bool ShouldNotify,
        string? UserMessageKey);

    public sealed class SynergyOsUpdateChecker
    {
        /// <summary>
        /// Override for tests or host wiring. Defaults to a no-op until the app assigns
        /// <see cref="SynergyOsInstalledVersion.GetInstalledSynergyOSVersion"/>.
        /// </summary>
        public static Func<string?> InstalledVersionProvider { get; set; } = static () => null;

        private static readonly Lazy<SynergyOsUpdateChecker> DefaultInstance = new(() =>
            new SynergyOsUpdateChecker(
                new SynergyOsUpdateSettingsStore(),
                CreateDefaultHttpClient(),
                () => DateTimeOffset.UtcNow,
                () => InstalledVersionProvider()));

        private readonly SynergyOsUpdateSettingsStore _settings;
        private readonly HttpClient _httpClient;
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly Func<string?> _getInstalledVersion;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private bool _notifiedThisSession;

        public SynergyOsUpdateChecker(
            SynergyOsUpdateSettingsStore settings,
            HttpClient httpClient,
            Func<DateTimeOffset>? utcNow = null,
            Func<string?>? getInstalledVersion = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
            _getInstalledVersion = getInstalledVersion ?? (() => InstalledVersionProvider());
        }

        public static SynergyOsUpdateChecker Default => DefaultInstance.Value;

        public SynergyOsUpdateSettingsStore Settings => _settings;

        public bool HasNotifiedThisSession => _notifiedThisSession;

        public void MarkNotifiedThisSession() => _notifiedThisSession = true;

        public async Task<SynergyOsUpdateCheckResult> CheckAsync(
            bool forceRefresh = false,
            bool ignoreSessionGate = false,
            CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                DateTimeOffset now = _utcNow();
                SynergyOsUpdateSettingsDocument snapshot = _settings.Snapshot();

                string? installed = _getInstalledVersion();
                if (string.IsNullOrWhiteSpace(installed))
                {
                    return new SynergyOsUpdateCheckResult(
                        SynergyOsUpdateCheckOutcome.SkippedNotSynergyOs,
                        null,
                        snapshot.CachedRelease,
                        SynergyOsUpdateNotifyReason.InstalledUnknown,
                        ShouldNotify: false,
                        UserMessageKey: null);
                }

                if (!forceRefresh && !snapshot.Enabled)
                {
                    return new SynergyOsUpdateCheckResult(
                        SynergyOsUpdateCheckOutcome.SkippedDisabled,
                        installed,
                        snapshot.CachedRelease,
                        SynergyOsUpdateNotifyReason.Disabled,
                        ShouldNotify: false,
                        UserMessageKey: null);
                }

                if (SynergyOsUpdateDecision.IsRateLimited(snapshot.RateLimitRetryAfterUtc, now))
                {
                    return FinishWithCache(
                        SynergyOsUpdateCheckOutcome.SkippedRateLimited,
                        installed,
                        snapshot,
                        now,
                        forceRefresh,
                        ignoreSessionGate,
                        "SynergyOsUpdate_CouldNotCheck");
                }

                if (!forceRefresh &&
                    SynergyOsUpdateDecision.IsWithinCheckInterval(snapshot.LastSuccessfulCheckUtc, now))
                {
                    return FinishWithCache(
                        SynergyOsUpdateCheckOutcome.SkippedThrottled,
                        installed,
                        snapshot,
                        now,
                        forceRefresh,
                        ignoreSessionGate,
                        null);
                }

                FetchResult fetch = await FetchLatestAsync(snapshot, now, cancellationToken)
                    .ConfigureAwait(false);

                snapshot = _settings.Snapshot();
                SynergyOsCachedRelease? latest = fetch.Release ?? snapshot.CachedRelease;

                if (fetch.Status == FetchStatus.NoReleases)
                {
                    return new SynergyOsUpdateCheckResult(
                        SynergyOsUpdateCheckOutcome.NoReleases,
                        installed,
                        null,
                        SynergyOsUpdateNotifyReason.LatestUnknown,
                        ShouldNotify: false,
                        "SynergyOsUpdate_UpToDate");
                }

                if (fetch.Status == FetchStatus.Failed || latest is null)
                {
                    return new SynergyOsUpdateCheckResult(
                        SynergyOsUpdateCheckOutcome.Failed,
                        installed,
                        snapshot.CachedRelease,
                        SynergyOsUpdateNotifyReason.LatestUnknown,
                        ShouldNotify: false,
                        "SynergyOsUpdate_CouldNotCheck");
                }

                SynergyOsUpdateNotifyReason notifyReason = SynergyOsUpdateDecision.ShouldNotify(
                    installed,
                    latest,
                    snapshot,
                    now,
                    updatesEnabled: true);

                if (notifyReason == SynergyOsUpdateNotifyReason.Notify)
                {
                    bool notify = ignoreSessionGate || !_notifiedThisSession;
                    return new SynergyOsUpdateCheckResult(
                        SynergyOsUpdateCheckOutcome.UpdateAvailable,
                        installed,
                        latest,
                        notifyReason,
                        notify,
                        "SynergyOsUpdate_Available");
                }

                return new SynergyOsUpdateCheckResult(
                    SynergyOsUpdateCheckOutcome.UpToDate,
                    installed,
                    latest,
                    notifyReason,
                    ShouldNotify: false,
                    "SynergyOsUpdate_UpToDate");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "SynergyOS update check failed.");
                return new SynergyOsUpdateCheckResult(
                    SynergyOsUpdateCheckOutcome.Failed,
                    null,
                    _settings.Snapshot().CachedRelease,
                    SynergyOsUpdateNotifyReason.LatestUnknown,
                    ShouldNotify: false,
                    "SynergyOsUpdate_CouldNotCheck");
            }
            finally
            {
                _gate.Release();
            }
        }

        public void RemindLater(SynergyOsCachedRelease release)
        {
            DateTimeOffset until = _utcNow().AddDays(SynergyOsUpdateConstants.SnoozeDays);
            _settings.SnoozeVersion(release.Version, until);
        }

        public void SkipVersion(SynergyOsCachedRelease release) =>
            _settings.SkipVersion(release.Version);

        private SynergyOsUpdateCheckResult FinishWithCache(
            SynergyOsUpdateCheckOutcome outcome,
            string installed,
            SynergyOsUpdateSettingsDocument snapshot,
            DateTimeOffset now,
            bool forceRefresh,
            bool ignoreSessionGate,
            string? fallbackMessageKey)
        {
            SynergyOsUpdateNotifyReason reason = SynergyOsUpdateDecision.ShouldNotify(
                installed,
                snapshot.CachedRelease,
                snapshot,
                now,
                updatesEnabled: true);

            if (reason == SynergyOsUpdateNotifyReason.Notify && snapshot.CachedRelease is not null)
            {
                bool notify = ignoreSessionGate || !_notifiedThisSession;
                return new SynergyOsUpdateCheckResult(
                    SynergyOsUpdateCheckOutcome.UpdateAvailable,
                    installed,
                    snapshot.CachedRelease,
                    reason,
                    notify,
                    "SynergyOsUpdate_Available");
            }

            if (outcome == SynergyOsUpdateCheckOutcome.SkippedThrottled &&
                reason == SynergyOsUpdateNotifyReason.NotNewer)
            {
                return new SynergyOsUpdateCheckResult(
                    SynergyOsUpdateCheckOutcome.UpToDate,
                    installed,
                    snapshot.CachedRelease,
                    reason,
                    ShouldNotify: false,
                    forceRefresh ? "SynergyOsUpdate_UpToDate" : null);
            }

            return new SynergyOsUpdateCheckResult(
                outcome,
                installed,
                snapshot.CachedRelease,
                reason,
                ShouldNotify: false,
                fallbackMessageKey);
        }

        private enum FetchStatus
        {
            Ok,
            NotModified,
            NoReleases,
            Failed
        }

        private readonly record struct FetchResult(FetchStatus Status, SynergyOsCachedRelease? Release);

        private async Task<FetchResult> FetchLatestAsync(
            SynergyOsUpdateSettingsDocument snapshot,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, SynergyOsUpdateConstants.ReleasesApiUrl);
            if (!string.IsNullOrWhiteSpace(snapshot.ETag))
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", snapshot.ETag);
            }

            using HttpResponseMessage response = await _httpClient
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                _settings.MarkSuccessfulCheck(now, snapshot.ETag, snapshot.CachedRelease);
                _settings.ClearRateLimitRetryAfter();
                return new FetchResult(FetchStatus.NotModified, snapshot.CachedRelease);
            }

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                DateTimeOffset retryAfter = ResolveRetryAfter(response, now);
                _settings.SetRateLimitRetryAfter(retryAfter);
                App.logger.Debug($"SynergyOS update check rate-limited until {retryAfter:O}.");
                return new FetchResult(FetchStatus.Failed, null);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _settings.MarkSuccessfulCheck(now, etag: null, release: null);
                _settings.ClearRateLimitRetryAfter();
                return new FetchResult(FetchStatus.NoReleases, null);
            }

            if ((int)response.StatusCode >= 500 || !response.IsSuccessStatusCode)
            {
                App.logger.Debug($"SynergyOS update check received HTTP {(int)response.StatusCode}.");
                return new FetchResult(FetchStatus.Failed, null);
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            SynergyOsCachedRelease? release = TryParseRelease(json);
            if (release is null)
            {
                App.logger.Debug("SynergyOS update check received malformed JSON or ignored draft/prerelease.");
                return new FetchResult(FetchStatus.Failed, null);
            }

            string? etag = response.Headers.ETag?.Tag;
            _settings.MarkSuccessfulCheck(now, etag, release);
            _settings.ClearRateLimitRetryAfter();
            return new FetchResult(FetchStatus.Ok, release);
        }

        internal static SynergyOsCachedRelease? TryParseRelease(string json)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;

                bool draft = root.TryGetProperty("draft", out JsonElement draftElement) &&
                    draftElement.ValueKind == JsonValueKind.True;
                bool prerelease = root.TryGetProperty("prerelease", out JsonElement preElement) &&
                    preElement.ValueKind == JsonValueKind.True;
                if (draft || prerelease)
                {
                    return null;
                }

                string? tagName = root.TryGetProperty("tag_name", out JsonElement tagElement)
                    ? tagElement.GetString()
                    : null;
                string? normalized = SynergyOsSemVer.NormalizeDisplay(tagName);
                if (normalized is null)
                {
                    return null;
                }

                string? name = root.TryGetProperty("name", out JsonElement nameElement)
                    ? nameElement.GetString()
                    : null;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    string? nameVersion = SynergyOsSemVer.NormalizeDisplay(name);
                    if (nameVersion is null ||
                        !string.Equals(nameVersion, normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        App.logger.Debug(
                            $"SynergyOS release name '{name.Trim()}' does not match tag-derived version '{normalized}'.");
                    }
                }

                string? htmlUrl = root.TryGetProperty("html_url", out JsonElement urlElement)
                    ? urlElement.GetString()
                    : null;
                string safeUrl = SynergyOsReleaseUrl.ResolveSafeOpenUrl(htmlUrl);

                DateTimeOffset? publishedAt = null;
                if (root.TryGetProperty("published_at", out JsonElement publishedElement) &&
                    publishedElement.ValueKind == JsonValueKind.String &&
                    DateTimeOffset.TryParse(
                        publishedElement.GetString(),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out DateTimeOffset parsedPublished))
                {
                    publishedAt = parsedPublished.ToUniversalTime();
                }

                return new SynergyOsCachedRelease
                {
                    Version = normalized,
                    HtmlUrl = safeUrl,
                    PublishedAtUtc = publishedAt
                };
            }
            catch
            {
                return null;
            }
        }

        private static DateTimeOffset ResolveRetryAfter(HttpResponseMessage response, DateTimeOffset now)
        {
            if (response.Headers.RetryAfter?.Delta is TimeSpan delta)
            {
                return now.Add(delta);
            }

            if (response.Headers.RetryAfter?.Date is DateTimeOffset date)
            {
                return date.ToUniversalTime();
            }

            if (response.Headers.TryGetValues("x-ratelimit-reset", out var values))
            {
                foreach (string value in values)
                {
                    if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long epoch) &&
                        epoch > 0)
                    {
                        return DateTimeOffset.FromUnixTimeSeconds(epoch);
                    }
                }
            }

            return now.AddHours(1);
        }

        private static HttpClient CreateDefaultHttpClient()
        {
            HttpClient client = new()
            {
                Timeout = TimeSpan.FromSeconds(SynergyOsUpdateConstants.RequestTimeoutSeconds)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(SynergyOsUpdateConstants.UserAgent);
            client.DefaultRequestHeaders.Accept.ParseAdd(SynergyOsUpdateConstants.AcceptHeader);
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "X-GitHub-Api-Version",
                SynergyOsUpdateConstants.ApiVersionHeader);
            return client;
        }
    }
}
