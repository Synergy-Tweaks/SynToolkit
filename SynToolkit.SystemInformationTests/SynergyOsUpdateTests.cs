#nullable enable

using SynToolkit.Services.SynergyOsUpdate;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

internal static class SynergyOsUpdateTests
{
    public static void RunAll()
    {
        RunStep(nameof(TestVersionComparison), TestVersionComparison);
        RunStep(nameof(TestNotifyDecision), TestNotifyDecision);
        RunStep(nameof(TestNoticeContent), TestNoticeContent);
        RunStep(nameof(TestReleaseUrlValidation), TestReleaseUrlValidation);
        RunStep(nameof(TestSettingsPersistenceDefaults), TestSettingsPersistenceDefaults);
        RunStep(nameof(TestLegacyCachedNotesIgnored), TestLegacyCachedNotesIgnored);
        RunStep(nameof(TestParseUsesTagNotName), TestParseUsesTagNotName);
        RunStep(nameof(TestFetchHandling), () => TestFetchHandling().GetAwaiter().GetResult());
        RunStep(nameof(TestThrottleAndManualBypass), () => TestThrottleAndManualBypass().GetAwaiter().GetResult());
        RunStep(nameof(TestRequiredHeaders), () => TestRequiredHeaders().GetAwaiter().GetResult());
        Console.WriteLine("SynergyOsUpdateTests: all passed.");
    }

    private static void RunStep(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"{name}: {exception.Message}", exception);
        }
    }

    private static void TestVersionComparison()
    {
        AssertEqual(0, SynergyOsSemVer.CompareVersions("v1.4.0", "1.4.0"));
        AssertTrue(SynergyOsSemVer.CompareVersions("1.10.0", "1.9.0") > 0);
        AssertEqual(0, SynergyOsSemVer.CompareVersions("1.4", "1.4.0"));
        AssertTrue(SynergyOsSemVer.CompareVersions("1.4.0-beta.1", "1.4.0") < 0);
        AssertTrue(SynergyOsSemVer.CompareVersions("1.5.0", "1.4.9") > 0);
        AssertNull(SynergyOsSemVer.CompareVersions("not-a-version", "1.0.0"));
        AssertNull(SynergyOsSemVer.CompareVersions("1.0.0", "??"));
        AssertFalse(SynergyOsSemVer.IsNewer("1.4.0", "1.4.0"));
        AssertTrue(SynergyOsSemVer.IsNewer("1.5.0", "1.4.0"));
    }

    private static void TestNotifyDecision()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-10-10T12:00:00Z");
        SynergyOsCachedRelease latest = Release("1.7.0");
        SynergyOsUpdateSettingsDocument settings = new();

        AssertEqual(
            SynergyOsUpdateNotifyReason.Notify,
            SynergyOsUpdateDecision.ShouldNotify("1.6.0", latest, settings, now, true));
        AssertEqual(
            SynergyOsUpdateNotifyReason.NotNewer,
            SynergyOsUpdateDecision.ShouldNotify("1.7.0", latest, settings, now, true));
        AssertEqual(
            SynergyOsUpdateNotifyReason.NotNewer,
            SynergyOsUpdateDecision.ShouldNotify("1.8.0", latest, settings, now, true));
        AssertEqual(
            SynergyOsUpdateNotifyReason.InstalledUnknown,
            SynergyOsUpdateDecision.ShouldNotify(null, latest, settings, now, true));
        // Draft/prerelease are filtered before decision; null latest => LatestUnknown.
        AssertEqual(
            SynergyOsUpdateNotifyReason.LatestUnknown,
            SynergyOsUpdateDecision.ShouldNotify("1.6.0", null, settings, now, true));

        settings.SkippedVersion = "1.7.0";
        AssertEqual(
            SynergyOsUpdateNotifyReason.SkippedVersion,
            SynergyOsUpdateDecision.ShouldNotify("1.6.0", latest, settings, now, true));

        settings.SkippedVersion = "1.7.0";
        SynergyOsCachedRelease newer = Release("1.8.0");
        AssertEqual(
            SynergyOsUpdateNotifyReason.Notify,
            SynergyOsUpdateDecision.ShouldNotify("1.6.0", newer, settings, now, true));

        settings = new SynergyOsUpdateSettingsDocument
        {
            SnoozedVersion = "1.7.0",
            SnoozeUntilUtc = now.AddDays(1)
        };
        AssertEqual(
            SynergyOsUpdateNotifyReason.Snoozed,
            SynergyOsUpdateDecision.ShouldNotify("1.6.0", latest, settings, now, true));

        settings.SnoozeUntilUtc = now.AddMinutes(-1);
        AssertEqual(
            SynergyOsUpdateNotifyReason.Notify,
            SynergyOsUpdateDecision.ShouldNotify("1.6.0", latest, settings, now, true));

        AssertEqual(
            SynergyOsUpdateNotifyReason.Disabled,
            SynergyOsUpdateDecision.ShouldNotify("1.6.0", latest, settings, now, false));

        AssertTrue(SynergyOsUpdateDecision.IsWithinCheckInterval(now.AddHours(-1), now));
        AssertFalse(SynergyOsUpdateDecision.IsWithinCheckInterval(now.AddHours(-25), now));
    }

    private static void TestNoticeContent()
    {
        Dictionary<string, string> strings = new(StringComparer.Ordinal)
        {
            ["SynergyOsUpdate_Title"] = "SynergyOS update available",
            ["SynergyOsUpdate_Body"] = "SynergyOS {0} is out. You're on {1}.",
            ["SynergyOsUpdate_Published"] = "Published {0}",
            ["SynergyOsUpdate_ViewRelease"] = "View release",
            ["SynergyOsUpdate_RemindLater"] = "Remind me later",
            ["SynergyOsUpdate_SkipVersion"] = "Skip this version"
        };

        SynergyOsUpdateNoticeModel notice = SynergyOsUpdateNotice.Create(
            "1.6",
            "1.5",
            DateTimeOffset.Parse("2026-09-01T00:54:56Z"),
            key => strings[key],
            CultureInfo.GetCultureInfo("en-US"));

        AssertEqual("SynergyOS update available", notice.Title);
        AssertEqual("SynergyOS 1.6 is out. You're on 1.5.", notice.BodyLine);
        AssertTrue(notice.HasPublishedLine);
        AssertTrue(notice.PublishedLine!.StartsWith("Published ", StringComparison.Ordinal));
        AssertFalse(notice.PublishedLine.Contains("Tuesday", StringComparison.Ordinal));
        AssertEqual("View release", notice.ViewReleaseLabel);
        AssertEqual("Remind me later", notice.RemindLaterLabel);
        AssertEqual("Skip this version", notice.SkipVersionLabel);

        SynergyOsUpdateNoticeModel noDate = SynergyOsUpdateNotice.Create(
            "1.6",
            "1.5",
            publishedAtUtc: null,
            key => strings[key],
            CultureInfo.GetCultureInfo("en-US"));
        AssertFalse(noDate.HasPublishedLine);

        // Long markdown body must not appear in the notice model at all.
        SynergyOsCachedRelease? parsed = SynergyOsUpdateChecker.TryParseRelease(
            SampleReleaseJson("1.6", name: "SynergyOS v0.6", body: "# Notes\n\n" + new string('x', 500)));
        AssertTrue(parsed is not null);
        SynergyOsUpdateNoticeModel fromParsed = SynergyOsUpdateNotice.Create(
            parsed!.Version,
            "1.5",
            parsed.PublishedAtUtc,
            key => strings[key],
            CultureInfo.GetCultureInfo("en-US"));
        AssertEqual("1.6", parsed.Version);
        AssertFalse(fromParsed.BodyLine.Contains("Notes", StringComparison.Ordinal));
        AssertFalse(fromParsed.BodyLine.Contains("xxxx", StringComparison.Ordinal));
        AssertFalse(fromParsed.Title.Contains("v0.6", StringComparison.Ordinal));
    }

    private static void TestParseUsesTagNotName()
    {
        SynergyOsCachedRelease? release = SynergyOsUpdateChecker.TryParseRelease(
            SampleReleaseJson("1.6", name: "SynergyOS v0.6"));
        AssertTrue(release is not null);
        AssertEqual("1.6", release!.Version);
    }

    private static void TestLegacyCachedNotesIgnored()
    {
        string path = Path.Combine(Path.GetTempPath(), "SynToolkit-tests", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            """
            {
              "Enabled": true,
              "CachedRelease": {
                "Version": "1.6",
                "HtmlUrl": "https://github.com/Synergy-Tweaks/SynergyOS/releases/tag/1.6",
                "PublishedAtUtc": "2026-09-02T00:54:56Z",
                "NotesExcerpt": "should not appear",
                "Title": "SynergyOS v0.6"
              }
            }
            """);

        SynergyOsUpdateSettingsStore store = new(path);
        SynergyOsCachedRelease? cached = store.Snapshot().CachedRelease;
        AssertTrue(cached is not null);
        AssertEqual("1.6", cached!.Version);

        store.MarkSuccessfulCheck(DateTimeOffset.UtcNow, "etag", cached);
        string rewritten = File.ReadAllText(path);
        AssertFalse(rewritten.Contains("NotesExcerpt", StringComparison.Ordinal));
        AssertFalse(rewritten.Contains("should not appear", StringComparison.Ordinal));
        AssertFalse(rewritten.Contains("SynergyOS v0.6", StringComparison.Ordinal));
        File.Delete(path);
    }

    private static void TestReleaseUrlValidation()
    {
        AssertTrue(SynergyOsReleaseUrl.TryValidateReleaseUrl(
            "https://github.com/Synergy-Tweaks/SynergyOS/releases/tag/1.7.0",
            out string? valid));
        AssertEqual("https://github.com/Synergy-Tweaks/SynergyOS/releases/tag/1.7.0", valid);

        AssertEqual(
            SynergyOsUpdateConstants.ReleasesLatestPageUrl,
            SynergyOsReleaseUrl.ResolveSafeOpenUrl("http://github.com/Synergy-Tweaks/SynergyOS/releases/tag/1"));
        AssertEqual(
            SynergyOsUpdateConstants.ReleasesLatestPageUrl,
            SynergyOsReleaseUrl.ResolveSafeOpenUrl("https://evil.com/Synergy-Tweaks/SynergyOS/releases/tag/1"));
        AssertEqual(
            SynergyOsUpdateConstants.ReleasesLatestPageUrl,
            SynergyOsReleaseUrl.ResolveSafeOpenUrl("https://github.com/other/repo/releases/tag/1"));
        AssertEqual(
            SynergyOsUpdateConstants.ReleasesLatestPageUrl,
            SynergyOsReleaseUrl.ResolveSafeOpenUrl("not a url"));
    }

    private static void TestSettingsPersistenceDefaults()
    {
        string path = Path.Combine(Path.GetTempPath(), "SynToolkit-tests", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");
        SynergyOsUpdateSettingsStore store = new(path);
        AssertTrue(store.IsEnabled);
        AssertNull(store.Snapshot().CachedRelease);
        File.Delete(path);
    }

    private static async Task TestFetchHandling()
    {
        await using TempSettings settings = new();
        DateTimeOffset now = DateTimeOffset.Parse("2026-10-10T12:00:00Z");

        // 200 OK
        {
            RecordingHandler handler = new(_ => JsonResponse(200, SampleReleaseJson("1.7.0"), "etag-1"));
            SynergyOsUpdateChecker checker = CreateChecker(settings.Store, handler, now, () => "1.6.0");
            SynergyOsUpdateCheckResult result = await checker.CheckAsync(forceRefresh: true);
            AssertEqual(SynergyOsUpdateCheckOutcome.UpdateAvailable, result.Outcome);
            AssertTrue(result.ShouldNotify);
            AssertEqual("1.7.0", result.Latest?.Version);
            AssertTrue(settings.Store.Snapshot().ETag?.Contains("etag-1", StringComparison.Ordinal) == true);
        }

        // 304 reuses cache + sends If-None-Match
        {
            RecordingHandler handler = new(request =>
            {
                AssertTrue(request.Headers.TryGetValues("If-None-Match", out var values));
                AssertTrue(values is not null && values.First().Contains("etag-1", StringComparison.Ordinal));
                return new HttpResponseMessage(HttpStatusCode.NotModified);
            });
            SynergyOsUpdateChecker checker = CreateChecker(settings.Store, handler, now.AddHours(25), () => "1.6.0");
            SynergyOsUpdateCheckResult result = await checker.CheckAsync(forceRefresh: true);
            AssertEqual(SynergyOsUpdateCheckOutcome.UpdateAvailable, result.Outcome);
            AssertEqual("1.7.0", result.Latest?.Version);
        }

        // 404
        {
            await using TempSettings empty = new();
            RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
            SynergyOsUpdateChecker checker = CreateChecker(empty.Store, handler, now, () => "1.6.0");
            SynergyOsUpdateCheckResult result = await checker.CheckAsync(forceRefresh: true);
            AssertEqual(SynergyOsUpdateCheckOutcome.NoReleases, result.Outcome);
            AssertFalse(result.ShouldNotify);
        }

        // 429 sets back-off
        {
            await using TempSettings limited = new();
            RecordingHandler handler = new(_ =>
            {
                HttpResponseMessage response = new(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(2));
                return response;
            });
            SynergyOsUpdateChecker checker = CreateChecker(limited.Store, handler, now, () => "1.6.0");
            SynergyOsUpdateCheckResult result = await checker.CheckAsync(forceRefresh: true);
            AssertEqual(SynergyOsUpdateCheckOutcome.Failed, result.Outcome);
            AssertTrue(limited.Store.Snapshot().RateLimitRetryAfterUtc > now);

            SynergyOsUpdateCheckResult blocked = await checker.CheckAsync(forceRefresh: true);
            AssertEqual(SynergyOsUpdateCheckOutcome.SkippedRateLimited, blocked.Outcome);
        }

        // malformed JSON
        {
            await using TempSettings bad = new();
            RecordingHandler handler = new(_ => JsonResponse(200, "{bad", null));
            SynergyOsUpdateChecker checker = CreateChecker(bad.Store, handler, now, () => "1.6.0");
            SynergyOsUpdateCheckResult result = await checker.CheckAsync(forceRefresh: true);
            AssertEqual(SynergyOsUpdateCheckOutcome.Failed, result.Outcome);
            AssertFalse(result.ShouldNotify);
        }

        // draft ignored
        {
            await using TempSettings draft = new();
            RecordingHandler handler = new(_ => JsonResponse(200, SampleReleaseJson("1.9.0", draft: true), null));
            SynergyOsUpdateChecker checker = CreateChecker(draft.Store, handler, now, () => "1.6.0");
            SynergyOsUpdateCheckResult result = await checker.CheckAsync(forceRefresh: true);
            AssertEqual(SynergyOsUpdateCheckOutcome.Failed, result.Outcome);
            AssertFalse(result.ShouldNotify);
        }

        // disabled automatic check makes no network call
        {
            await using TempSettings disabled = new();
            disabled.Store.IsEnabled = false;
            int calls = 0;
            RecordingHandler handler = new(_ =>
            {
                calls++;
                return JsonResponse(200, SampleReleaseJson("1.7.0"), null);
            });
            SynergyOsUpdateChecker checker = CreateChecker(disabled.Store, handler, now, () => "1.6.0");
            SynergyOsUpdateCheckResult result = await checker.CheckAsync(forceRefresh: false);
            AssertEqual(SynergyOsUpdateCheckOutcome.SkippedDisabled, result.Outcome);
            AssertEqual(0, calls);
        }
    }

    private static async Task TestThrottleAndManualBypass()
    {
        await using TempSettings settings = new();
        DateTimeOffset now = DateTimeOffset.Parse("2026-10-10T12:00:00Z");
        int calls = 0;
        RecordingHandler handler = new(_ =>
        {
            calls++;
            return JsonResponse(200, SampleReleaseJson("1.7.0"), "etag-x");
        });

        SynergyOsUpdateChecker checker = CreateChecker(settings.Store, handler, now, () => "1.6.0");
        await checker.CheckAsync(forceRefresh: true);
        AssertEqual(1, calls);

        SynergyOsUpdateCheckResult throttled = await checker.CheckAsync(forceRefresh: false);
        AssertEqual(SynergyOsUpdateCheckOutcome.UpdateAvailable, throttled.Outcome);
        AssertEqual(1, calls);

        SynergyOsUpdateCheckResult manual = await checker.CheckAsync(forceRefresh: true);
        AssertEqual(2, calls);
        AssertEqual(SynergyOsUpdateCheckOutcome.UpdateAvailable, manual.Outcome);
    }

    private static async Task TestRequiredHeaders()
    {
        await using TempSettings settings = new();
        DateTimeOffset now = DateTimeOffset.Parse("2026-10-10T12:00:00Z");
        RecordingHandler handler = new(request =>
        {
            AssertTrue(request.Headers.UserAgent.ToString().Contains("Syntoolkit", StringComparison.OrdinalIgnoreCase));
            AssertTrue(request.Headers.Accept.ToString().Contains("application/vnd.github+json", StringComparison.Ordinal));
            AssertTrue(request.Headers.TryGetValues("X-GitHub-Api-Version", out var versions));
            AssertTrue(versions is not null);
            AssertEqual(SynergyOsUpdateConstants.ApiVersionHeader, versions!.First());
            return JsonResponse(200, SampleReleaseJson("1.7.0"), null);
        });

        HttpClient client = new(handler)
        {
            Timeout = TimeSpan.FromSeconds(SynergyOsUpdateConstants.RequestTimeoutSeconds)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(SynergyOsUpdateConstants.UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd(SynergyOsUpdateConstants.AcceptHeader);
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-GitHub-Api-Version",
            SynergyOsUpdateConstants.ApiVersionHeader);

        SynergyOsUpdateChecker checker = new(settings.Store, client, () => now, () => "1.6.0");
        await checker.CheckAsync(forceRefresh: true);
    }

    private static SynergyOsUpdateChecker CreateChecker(
        SynergyOsUpdateSettingsStore store,
        HttpMessageHandler handler,
        DateTimeOffset now,
        Func<string?> installed)
    {
        HttpClient client = new(handler)
        {
            Timeout = TimeSpan.FromSeconds(SynergyOsUpdateConstants.RequestTimeoutSeconds)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(SynergyOsUpdateConstants.UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd(SynergyOsUpdateConstants.AcceptHeader);
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-GitHub-Api-Version",
            SynergyOsUpdateConstants.ApiVersionHeader);
        return new SynergyOsUpdateChecker(store, client, () => now, installed);
    }

    private static SynergyOsCachedRelease Release(string version) =>
        new()
        {
            Version = version,
            HtmlUrl = SynergyOsUpdateConstants.ReleasesLatestPageUrl,
            PublishedAtUtc = DateTimeOffset.Parse("2026-09-01T00:00:00Z")
        };

    private static string SampleReleaseJson(
        string tag,
        bool draft = false,
        bool prerelease = false,
        string? name = null,
        string? body = null)
    {
        string releaseName = name ?? $"SynergyOS {tag}";
        string releaseBody = body ?? "# Notes\n\nHello **world**";
        string escapedName = releaseName.Replace("\\", "\\\\").Replace("\"", "\\\"");
        string escapedBody = releaseBody
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "");
        return $$"""
        {
          "tag_name": "{{tag}}",
          "name": "{{escapedName}}",
          "html_url": "https://github.com/Synergy-Tweaks/SynergyOS/releases/tag/{{tag}}",
          "published_at": "2026-09-02T00:54:56Z",
          "body": "{{escapedBody}}",
          "draft": {{(draft ? "true" : "false")}},
          "prerelease": {{(prerelease ? "true" : "false")}}
        }
        """;
    }

    private static HttpResponseMessage JsonResponse(int statusCode, string json, string? etag)
    {
        HttpResponseMessage response = new((HttpStatusCode)statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(etag))
        {
            response.Headers.ETag = new EntityTagHeaderValue(etag.StartsWith('"') ? etag : $"\"{etag}\"");
        }

        return response;
    }

    private static void AssertTrue(bool value)
    {
        if (!value)
        {
            throw new InvalidOperationException("Expected true.");
        }
    }

    private static void AssertFalse(bool value)
    {
        if (value)
        {
            throw new InvalidOperationException("Expected false.");
        }
    }

    private static void AssertNull(object? value)
    {
        if (value is not null)
        {
            throw new InvalidOperationException("Expected null.");
        }
    }

    private static void AssertEqual<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }

    private sealed class TempSettings : IAsyncDisposable
    {
        public SynergyOsUpdateSettingsStore Store { get; }
        private readonly string _path;

        public TempSettings()
        {
            _path = Path.Combine(Path.GetTempPath(), "SynToolkit-tests", Guid.NewGuid().ToString("N") + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            Store = new SynergyOsUpdateSettingsStore(_path);
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }
            }
            catch
            {
            }

            return ValueTask.CompletedTask;
        }
    }
}
