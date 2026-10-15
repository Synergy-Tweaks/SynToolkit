#nullable enable

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace SynToolkit.Services
{
    public sealed class GitHubReleaseService
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private const string ReleasesApiUrl = "https://api.github.com/repos/Synergy-Tweaks/SynergyOS/releases";
        private const string ReleasesPageUrl = "https://github.com/synergy-tweaks/synergyos/releases";
        private static readonly HttpClient Client = CreateHttpClient();

        public static string ReleasesUrl => ReleasesPageUrl;

        public async Task<GitHubRelease?> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                string json = await Client.GetStringAsync($"{ReleasesApiUrl}/latest", cancellationToken);
                return ParseRelease(json);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Logger.Warn(exception, "Failed to fetch the latest GitHub release.");
                return null;
            }
        }

        public async Task<List<GitHubRelease>> GetRecentReleasesAsync(int count = 3, CancellationToken cancellationToken = default)
        {
            List<GitHubRelease> releases = new();
            try
            {
                string json = await Client.GetStringAsync($"{ReleasesApiUrl}?per_page={count}", cancellationToken);
                using JsonDocument document = JsonDocument.Parse(json);

                foreach (JsonElement element in document.RootElement.EnumerateArray())
                {
                    GitHubRelease? release = ParseReleaseElement(element);
                    if (release is not null)
                    {
                        releases.Add(release);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Logger.Warn(exception, "Failed to fetch recent GitHub releases.");
            }

            return releases;
        }

        private static GitHubRelease? ParseRelease(string json)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                return ParseReleaseElement(document.RootElement);
            }
            catch (Exception exception)
            {
                Logger.Warn(exception, "Failed to parse GitHub release JSON.");
                return null;
            }
        }

        private static GitHubRelease? ParseReleaseElement(JsonElement element)
        {
            try
            {
                string? tagName = element.GetProperty("tag_name").GetString();
                string? name = element.GetProperty("name").GetString();
                string? body = element.GetProperty("body").GetString();
                _ = element.TryGetProperty("html_url", out _);
                string? publishedAt = element.GetProperty("published_at").GetString();
                bool isPrerelease = element.GetProperty("prerelease").GetBoolean();

                if (string.IsNullOrWhiteSpace(tagName))
                {
                    return null;
                }

                DateTime? publishedDate = null;
                if (DateTime.TryParse(publishedAt, out DateTime parsed))
                {
                    publishedDate = parsed;
                }

                return new GitHubRelease
                {
                    TagName = tagName,
                    Name = string.IsNullOrWhiteSpace(name) ? tagName : name,
                    Body = body ?? string.Empty,
                    // Never store a tag-pinned API url for navigation.
                    HtmlUrl = ReleasesPageUrl,
                    PublishedAt = publishedDate,
                    IsPrerelease = isPrerelease
                };
            }
            catch (Exception exception)
            {
                Logger.Debug(exception, "Failed to parse a release element.");
                return null;
            }
        }

        private static HttpClient CreateHttpClient()
        {
            HttpClient client = new()
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SynToolkit/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }
    }

    public sealed class GitHubRelease
    {
        public string TagName { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Body { get; init; } = string.Empty;
        public string HtmlUrl { get; init; } = string.Empty;
        public DateTime? PublishedAt { get; init; }
        public bool IsPrerelease { get; init; }

        public string FormattedDate => PublishedAt?.ToString("MMM d, yyyy") ?? "Unknown";

        public string ShortBody
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Body))
                {
                    return "No release notes available.";
                }

                string trimmed = Body.Trim();
                if (trimmed.Length <= 300)
                {
                    return trimmed;
                }

                return trimmed[..297] + "...";
            }
        }
    }
}
