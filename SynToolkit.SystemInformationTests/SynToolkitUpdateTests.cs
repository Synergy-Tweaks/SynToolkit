#nullable enable

using SynToolkit.Services.SynergyOsUpdate;
using SynToolkit.Services.SynToolkitUpdate;
using SynToolkit.Utils;
using System.Globalization;
using System.Text.Json;

internal static class SynToolkitUpdateTests
{
    public static void RunAll()
    {
        RunStep(nameof(TestReleaseUrlAlwaysFixed), TestReleaseUrlAlwaysFixed);
        RunStep(nameof(TestVersionComparisonAssemblyVsTag), TestVersionComparisonAssemblyVsTag);
        RunStep(nameof(TestNotifyDecisionSkipSnoozeDisabled), TestNotifyDecisionSkipSnoozeDisabled);
        RunStep(nameof(TestNoticeContent), TestNoticeContent);
        RunStep(nameof(TestLegacyCachedTagUrlMigrated), TestLegacyCachedTagUrlMigrated);
        RunStep(nameof(TestParsePrefersAssetVersionAndIgnoresHtmlUrl), TestParsePrefersAssetVersionAndIgnoresHtmlUrl);
        RunStep(nameof(TestHelperAndCheckerAgreeOnNewer), TestHelperAndCheckerAgreeOnNewer);
        RunStep(nameof(TestManualCheckNeverReopensNotice), TestManualCheckNeverReopensNotice);
        Console.WriteLine("SynToolkitUpdateTests: all passed.");
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

    private static void TestReleaseUrlAlwaysFixed()
    {
        string fixedUrl = SynToolkitUpdateConstants.ReleasesPageUrl;
        AssertEqual("https://github.com/synergy-tweaks/syntoolkit/releases", fixedUrl);
        AssertFalse(fixedUrl.Contains("/tag/", StringComparison.OrdinalIgnoreCase));
        AssertFalse(fixedUrl.Contains("/latest", StringComparison.OrdinalIgnoreCase));

        AssertEqual(fixedUrl, SynToolkitReleaseUrl.ResolveSafeOpenUrl(
            "https://github.com/Synergy-Tweaks/SynToolkit/releases/tag/v1.7.0"));
        AssertEqual(fixedUrl, SynToolkitReleaseUrl.ResolveSafeOpenUrl(
            "https://evil.com/Synergy-Tweaks/SynToolkit/releases/tag/1"));
        AssertEqual(fixedUrl, SynToolkitReleaseUrl.ResolveSafeOpenUrl(null));
        AssertEqual(fixedUrl, SynToolkitReleaseUrl.ResolveSafeOpenUrl("not a url"));
    }

    private static void TestVersionComparisonAssemblyVsTag()
    {
        AssertEqual(0, SynergyOsSemVer.CompareVersions("1.7.0.0", "v1.7.0"));
        AssertTrue(SynergyOsSemVer.CompareVersions("v1.10", "v1.9") > 0);
        AssertFalse(SynergyOsSemVer.IsNewer("1.7.0", "1.7.0.0"));
        AssertTrue(SynergyOsSemVer.IsNewer("1.8.0", "1.7.0.0"));
    }

    private static void TestNotifyDecisionSkipSnoozeDisabled()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-10-10T12:00:00Z");
        SynToolkitCachedRelease latest = Release("1.8.0");

        AssertFalse(SynToolkitUpdateDecision.ShouldShowNotice(
            "1.7.0.0",
            latest,
            new SynToolkitUpdateSettingsDocument { Enabled = false },
            now,
            updatesEnabled: false,
            notifiedThisSession: false));

        AssertFalse(SynToolkitUpdateDecision.ShouldShowNotice(
            "1.7.0.0",
            latest,
            new SynToolkitUpdateSettingsDocument { Enabled = true },
            now,
            updatesEnabled: true,
            notifiedThisSession: true));

        AssertEqual(
            SynToolkitUpdateNotifyReason.SkippedVersion,
            SynToolkitUpdateDecision.ShouldNotify(
                "1.7.0.0",
                latest,
                new SynToolkitUpdateSettingsDocument { SkippedVersion = "1.8.0" },
                now,
                updatesEnabled: true));

        AssertEqual(
            SynToolkitUpdateNotifyReason.Snoozed,
            SynToolkitUpdateDecision.ShouldNotify(
                "1.7.0.0",
                latest,
                new SynToolkitUpdateSettingsDocument
                {
                    SnoozedVersion = "1.8.0",
                    SnoozeUntilUtc = now.AddDays(1)
                },
                now,
                updatesEnabled: true));

        AssertTrue(SynToolkitUpdateDecision.ShouldShowNotice(
            "1.7.0.0",
            latest,
            new SynToolkitUpdateSettingsDocument { Enabled = true },
            now,
            updatesEnabled: true,
            notifiedThisSession: false));
    }

    private static void TestNoticeContent()
    {
        Dictionary<string, string> strings = new(StringComparer.Ordinal)
        {
            ["SynToolkitUpdate_Title"] = "SynToolkit update available",
            ["SynToolkitUpdate_Body"] = "SynToolkit {0} is out. You're on {1}.",
            ["SynToolkitUpdate_Published"] = "Published {0}",
            ["SynToolkitUpdate_ViewRelease"] = "View release",
            ["SynToolkitUpdate_RemindLater"] = "Remind me later",
            ["SynToolkitUpdate_SkipVersion"] = "Skip this version"
        };

        SynToolkitUpdateNoticeModel notice = SynToolkitUpdateNotice.Create(
            "1.8.0",
            "1.7.0.0",
            DateTimeOffset.Parse("2026-09-01T00:54:56Z"),
            key => strings[key],
            CultureInfo.GetCultureInfo("en-US"));

        AssertEqual("SynToolkit update available", notice.Title);
        AssertEqual("SynToolkit 1.8.0 is out. You're on 1.7.0.0.", notice.BodyLine);
        AssertTrue(notice.HasPublishedLine);
    }

    private static void TestLegacyCachedTagUrlMigrated()
    {
        string path = Path.Combine(Path.GetTempPath(), "SynToolkit-tests", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            """
            {
              "Enabled": true,
              "CachedRelease": {
                "Version": "1.6.0",
                "HtmlUrl": "https://github.com/Synergy-Tweaks/SynToolkit/releases/tag/v1.6.0",
                "PublishedAtUtc": "2026-09-02T00:54:56Z"
              }
            }
            """);

        SynToolkitUpdateSettingsStore store = new(path);
        SynToolkitCachedRelease? cached = store.Snapshot().CachedRelease;
        AssertTrue(cached is not null);
        AssertEqual(SynToolkitUpdateConstants.ReleasesPageUrl, cached!.HtmlUrl);
        AssertFalse(cached.HtmlUrl.Contains("/tag/", StringComparison.OrdinalIgnoreCase));
        AssertEqual(
            SynToolkitUpdateConstants.ReleasesPageUrl,
            SynToolkitReleaseUrl.ResolveSafeOpenUrl(cached.HtmlUrl));
        File.Delete(path);
    }

    private static void TestParsePrefersAssetVersionAndIgnoresHtmlUrl()
    {
        string json = """
        {
          "tag_name": "v1.0.0",
          "name": "SynToolkit 1.0.0",
          "html_url": "https://github.com/Synergy-Tweaks/SynToolkit/releases/tag/v1.0.0",
          "published_at": "2026-09-02T00:54:56Z",
          "draft": false,
          "prerelease": false,
          "assets": [
            {
              "name": "SynToolkit-Setup-1.8.0-Stable.exe",
              "browser_download_url": "https://example.com/setup.exe"
            }
          ]
        }
        """;

        SynToolkitCachedRelease? release = SynToolkitUpdateChecker.TryParseRelease(json);
        AssertTrue(release is not null);
        AssertEqual("1.8.0", release!.Version);
        AssertEqual(SynToolkitUpdateConstants.ReleasesPageUrl, release.HtmlUrl);

        using JsonDocument document = JsonDocument.Parse(json);
        AssertTrue(SynToolkitUpdateHelper.TryResolveReleaseVersion(document.RootElement, out Version? helperVersion));
        AssertEqual("1.8.0", helperVersion!.ToString());
    }

    private static void TestHelperAndCheckerAgreeOnNewer()
    {
        Version current = new(1, 7, 0, 0);
        Version available = new(1, 8, 0);
        SynToolkitUpdateStatus status = new(current, available, "https://example.com/setup.exe");
        AssertTrue(status.IsUpdateAvailable);

        AssertEqual(
            SynToolkitUpdateAvailability.UpdateAvailable,
            SynToolkitUpdateDecision.GetUpdateStatus("1.7.0.0", Release("1.8.0")));
        AssertEqual(
            SynToolkitUpdateAvailability.UpToDate,
            SynToolkitUpdateDecision.GetUpdateStatus("1.7.0.0", Release("1.7.0")));
    }

    private static void TestManualCheckNeverReopensNotice()
    {
        string path = Path.Combine(Path.GetTempPath(), "SynToolkit-tests", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        const string releaseJson = """
        {
          "tag_name": "v1.8.0",
          "html_url": "https://github.com/Synergy-Tweaks/SynToolkit/releases/tag/v1.8.0",
          "published_at": "2026-09-02T00:54:56Z",
          "draft": false,
          "prerelease": false,
          "assets": [
            { "name": "SynToolkit-Setup-1.8.0-Stable.exe", "browser_download_url": "https://github.com/x/y.exe" }
          ]
        }
        """;

        SynToolkitUpdateChecker NewChecker() => new(
            new SynToolkitUpdateSettingsStore(path),
            new HttpClient(new StaticJsonHandler(releaseJson)),
            () => DateTimeOffset.Parse("2026-10-10T12:00:00Z"),
            () => "1.7.0.0");

        // Automatic startup check: the user should see the notice once.
        SynToolkitUpdateCheckResult startup = NewChecker().CheckAsync().GetAwaiter().GetResult();
        AssertEqual(SynToolkitUpdateCheckOutcome.UpdateAvailable, startup.Outcome);
        AssertTrue(startup.ShouldNotify);

        // Manual Settings check: refreshes status but must not re-open the notice.
        SynToolkitUpdateCheckResult manual = NewChecker()
            .CheckAsync(forceRefresh: true, ignoreSessionGate: true)
            .GetAwaiter()
            .GetResult();
        AssertEqual(SynToolkitUpdateCheckOutcome.UpdateAvailable, manual.Outcome);
        AssertFalse(manual.ShouldNotify);

        File.Delete(path);
    }

    private sealed class StaticJsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
    }

    private static SynToolkitCachedRelease Release(string version) =>
        new()
        {
            Version = version,
            HtmlUrl = SynToolkitUpdateConstants.ReleasesPageUrl,
            PublishedAtUtc = DateTimeOffset.Parse("2026-09-01T00:00:00Z")
        };

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

    private static void AssertEqual<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }
}
