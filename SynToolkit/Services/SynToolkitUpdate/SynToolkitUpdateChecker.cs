#nullable enable

using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SynToolkit.Utils;

namespace SynToolkit.Services.SynToolkitUpdate
{
    public enum SynToolkitUpdateCheckOutcome
    {
        SkippedDisabled,
        SkippedThrottled,
        SkippedRateLimited,
        UpToDate,
        UpdateAvailable,
        Failed,
        NoReleases
    }

    public sealed record SynToolkitUpdateCheckResult(
        SynToolkitUpdateCheckOutcome Outcome,
        string? InstalledVersion,
        SynToolkitCachedRelease? Latest,
        SynToolkitUpdateNotifyReason NotifyReason,
        bool ShouldNotify,
        string? UserMessageKey);

    public sealed class SynToolkitUpdateChecker
    {
        public static Func<string?> InstalledVersionProvider { get; set; } =
            SynToolkitInstalledVersion.GetInstalledSynToolkitVersion;

        private static readonly Lazy<SynToolkitUpdateChecker> DefaultInstance = new(() =>
            new SynToolkitUpdateChecker(
                new SynToolkitUpdateSettingsStore(),
                CreateDefaultHttpClient(),
                () => DateTimeOffset.UtcNow,
                () => InstalledVersionProvider()));

        private readonly SynToolkitUpdateSettingsStore _settings;
        private readonly HttpClient _httpClient;
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly Func<string?> _getInstalledVersion;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private bool _notifiedThisSession;

        public SynToolkitUpdateChecker(
            SynToolkitUpdateSettingsStore settings,
            HttpClient httpClient,
            Func<DateTimeOffset>? utcNow = null,
            Func<string?>? getInstalledVersion = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
            _getInstalledVersion = getInstalledVersion ?? (() => InstalledVersionProvider());
        }

        public static SynToolkitUpdateChecker Default => DefaultInstance.Value;

        public SynToolkitUpdateSettingsStore Settings => _settings;

        public bool HasNotifiedThisSession => _notifiedThisSession;

        public void MarkNotifiedThisSession() => _notifiedThisSession = true;

        public async Task<SynToolkitUpdateCheckResult> CheckAsync(
            bool forceRefresh = false,
            bool ignoreSessionGate = false,
            CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                DateTimeOffset now = _utcNow();
                SynToolkitUpdateSettingsDocument snapshot = _settings.Snapshot();

                string? installed = _getInstalledVersion();
                if (string.IsNullOrWhiteSpace(installed))
                {
                    return new SynToolkitUpdateCheckResult(
                        SynToolkitUpdateCheckOutcome.Failed,
                        null,
                        snapshot.CachedRelease,
                        SynToolkitUpdateNotifyReason.InstalledUnknown,
                        ShouldNotify: false,
                        "SynToolkitUpdate_CouldNotCheck");
                }

                if (!forceRefresh && !snapshot.Enabled)
                {
                    return new SynToolkitUpdateCheckResult(
                        SynToolkitUpdateCheckOutcome.SkippedDisabled,
                        installed,
                        snapshot.CachedRelease,
                        SynToolkitUpdateNotifyReason.Disabled,
                        ShouldNotify: false,
                        UserMessageKey: null);
                }

                if (SynToolkitUpdateDecision.IsRateLimited(snapshot.RateLimitRetryAfterUtc, now))
                {
                    return FinishWithCache(
                        SynToolkitUpdateCheckOutcome.SkippedRateLimited,
                        installed,
                        snapshot,
                        now,
                        ignoreSessionGate,
                        "SynToolkitUpdate_CouldNotCheck");
                }

                if (!forceRefresh &&
                    SynToolkitUpdateDecision.IsWithinCheckInterval(snapshot.LastSuccessfulCheckUtc, now))
                {
                    return FinishWithCache(
                        SynToolkitUpdateCheckOutcome.SkippedThrottled,
                        installed,
                        snapshot,
                        now,
                        ignoreSessionGate,
                        null);
                }

                FetchResult fetch = await FetchLatestAsync(snapshot, now, cancellationToken)
                    .ConfigureAwait(false);

                snapshot = _settings.Snapshot();
                SynToolkitCachedRelease? latest = fetch.Release ?? snapshot.CachedRelease;

                if (fetch.Status == FetchStatus.NoReleases)
                {
                    return new SynToolkitUpdateCheckResult(
                        SynToolkitUpdateCheckOutcome.NoReleases,
                        installed,
                        null,
                        SynToolkitUpdateNotifyReason.LatestUnknown,
                        ShouldNotify: false,
                        "SynToolkitUpdate_CouldNotCheck");
                }

                if (fetch.Status == FetchStatus.Failed ||
                    (fetch.Status == FetchStatus.NotModified && latest is null) ||
                    (fetch.Status == FetchStatus.Ok && latest is null))
                {
                    return new SynToolkitUpdateCheckResult(
                        SynToolkitUpdateCheckOutcome.Failed,
                        installed,
                        snapshot.CachedRelease,
                        SynToolkitUpdateNotifyReason.LatestUnknown,
                        ShouldNotify: false,
                        "SynToolkitUpdate_CouldNotCheck");
                }

                if (latest is null)
                {
                    return new SynToolkitUpdateCheckResult(
                        SynToolkitUpdateCheckOutcome.Failed,
                        installed,
                        null,
                        SynToolkitUpdateNotifyReason.LatestUnknown,
                        ShouldNotify: false,
                        "SynToolkitUpdate_CouldNotCheck");
                }

                return BuildAvailabilityResult(
                    installed,
                    latest,
                    snapshot,
                    now,
                    ignoreSessionGate);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "SynToolkit update check failed.");
                return new SynToolkitUpdateCheckResult(
                    SynToolkitUpdateCheckOutcome.Failed,
                    null,
                    _settings.Snapshot().CachedRelease,
                    SynToolkitUpdateNotifyReason.LatestUnknown,
                    ShouldNotify: false,
                    "SynToolkitUpdate_CouldNotCheck");
            }
            finally
            {
                _gate.Release();
            }
        }

        public void RemindLater(SynToolkitCachedRelease release)
        {
            DateTimeOffset until = _utcNow().AddDays(SynToolkitUpdateConstants.SnoozeDays);
            _settings.SnoozeVersion(release.Version, until);
        }

        public void SkipVersion(SynToolkitCachedRelease release) =>
            _settings.SkipVersion(release.Version);

        private SynToolkitUpdateCheckResult FinishWithCache(
            SynToolkitUpdateCheckOutcome outcome,
            string installed,
            SynToolkitUpdateSettingsDocument snapshot,
            DateTimeOffset now,
            bool ignoreSessionGate,
            string? fallbackMessageKey)
        {
            if (outcome is SynToolkitUpdateCheckOutcome.SkippedRateLimited)
            {
                SynToolkitUpdateNotifyReason limitedReason = SynToolkitUpdateDecision.ShouldNotify(
                    installed,
                    snapshot.CachedRelease,
                    snapshot,
                    now,
                    updatesEnabled: true);
                return new SynToolkitUpdateCheckResult(
                    outcome,
                    installed,
                    snapshot.CachedRelease,
                    limitedReason,
                    ShouldNotify: false,
                    fallbackMessageKey);
            }

            if (snapshot.CachedRelease is null)
            {
                return new SynToolkitUpdateCheckResult(
                    outcome,
                    installed,
                    null,
                    SynToolkitUpdateNotifyReason.LatestUnknown,
                    ShouldNotify: false,
                    fallbackMessageKey);
            }

            return BuildAvailabilityResult(
                installed,
                snapshot.CachedRelease,
                snapshot,
                now,
                ignoreSessionGate);
        }

        private SynToolkitUpdateCheckResult BuildAvailabilityResult(
            string installed,
            SynToolkitCachedRelease latest,
            SynToolkitUpdateSettingsDocument snapshot,
            DateTimeOffset now,
            bool ignoreSessionGate)
        {
            SynToolkitUpdateAvailability availability =
                SynToolkitUpdateDecision.GetUpdateStatus(installed, latest);
            SynToolkitUpdateNotifyReason notifyReason = SynToolkitUpdateDecision.ShouldNotify(
                installed,
                latest,
                snapshot,
                now,
                updatesEnabled: true);

            SynToolkitUpdateCheckOutcome outcome = availability switch
            {
                SynToolkitUpdateAvailability.UpdateAvailable => SynToolkitUpdateCheckOutcome.UpdateAvailable,
                SynToolkitUpdateAvailability.UpToDate => SynToolkitUpdateCheckOutcome.UpToDate,
                _ => SynToolkitUpdateCheckOutcome.Failed,
            };

            bool shouldNotify = SynToolkitUpdateDecision.ShouldShowNotice(
                installed,
                latest,
                snapshot,
                now,
                updatesEnabled: true,
                // A manual Settings check refreshes the status but must never
                // re-open the update notice, so treat it as already notified.
                notifiedThisSession: ignoreSessionGate || _notifiedThisSession);

            string? messageKey = outcome switch
            {
                SynToolkitUpdateCheckOutcome.UpdateAvailable => "SynToolkitUpdate_Available",
                SynToolkitUpdateCheckOutcome.UpToDate => "SynToolkitUpdate_UpToDate",
                _ => "SynToolkitUpdate_CouldNotCheck",
            };

            return new SynToolkitUpdateCheckResult(
                outcome,
                installed,
                latest,
                notifyReason,
                shouldNotify,
                messageKey);
        }

        private enum FetchStatus
        {
            Ok,
            NotModified,
            NoReleases,
            Failed
        }

        private readonly record struct FetchResult(FetchStatus Status, SynToolkitCachedRelease? Release);

        private async Task<FetchResult> FetchLatestAsync(
            SynToolkitUpdateSettingsDocument snapshot,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, SynToolkitUpdateConstants.ReleasesApiUrl);
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
                App.logger.Debug($"SynToolkit update check rate-limited until {retryAfter:O}.");
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
                App.logger.Debug($"SynToolkit update check received HTTP {(int)response.StatusCode}.");
                return new FetchResult(FetchStatus.Failed, null);
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            SynToolkitCachedRelease? release = TryParseRelease(json);
            if (release is null)
            {
                App.logger.Debug("SynToolkit update check received malformed JSON or ignored draft/prerelease.");
                return new FetchResult(FetchStatus.Failed, null);
            }

            string? etag = response.Headers.ETag?.Tag;
            _settings.MarkSuccessfulCheck(now, etag, release);
            _settings.ClearRateLimitRetryAfter();
            return new FetchResult(FetchStatus.Ok, release);
        }

        internal static SynToolkitCachedRelease? TryParseRelease(string json)
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

                // Same version source as SynToolkitUpdateHelper / Needs Attention:
                // installer asset name first, then tag_name.
                string? normalized = SynToolkitUpdateHelper.TryResolveReleaseVersionDisplay(root);
                if (normalized is null)
                {
                    return null;
                }

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

                return new SynToolkitCachedRelease
                {
                    Version = normalized,
                    HtmlUrl = SynToolkitReleaseUrl.ResolveSafeOpenUrl(),
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
                Timeout = TimeSpan.FromSeconds(SynToolkitUpdateConstants.RequestTimeoutSeconds)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(SynToolkitUpdateConstants.UserAgent);
            client.DefaultRequestHeaders.Accept.ParseAdd(SynToolkitUpdateConstants.AcceptHeader);
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "X-GitHub-Api-Version",
                SynToolkitUpdateConstants.ApiVersionHeader);
            return client;
        }
    }
}
