#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SynToolkit.Models.Games;
using SynToolkit.Utils;

namespace SynToolkit.Services.Games
{
    /// <summary>
    /// Fetches and caches game cover art under %LocalAppData%\SynToolkit\GameArtwork
    /// (sibling of the existing GameIcons cache). Steam uses the public CDN by appid;
    /// Epic/GOG/Xbox/Manual use SteamGridDB when an API key is configured.
    /// </summary>
    public sealed class GameArtworkService : IGameArtworkService
    {
        private const string RegistryKeyPath = @"HKLM\SOFTWARE\SynToolkit";
        private const string ApiKeyValueName = "SteamGridDbApiKey";
        private const string SteamCdnPortrait = "https://cdn.akamai.steamstatic.com/steam/apps/{0}/library_600x900.jpg";
        private const string SteamCdnHeader = "https://cdn.akamai.steamstatic.com/steam/apps/{0}/header.jpg";
        private const string SteamGridDbBase = "https://www.steamgriddb.com/api/v2";

        private static readonly HttpClient Http = CreateHttpClient();
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly string _cacheDirectory;
        private readonly SemaphoreSlim _fetchGate = new(4, 4);

        public GameArtworkService()
        {
            _cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SynToolkit",
                "GameArtwork");
            Directory.CreateDirectory(_cacheDirectory);
        }

        public string GetCacheDirectory() => _cacheDirectory;

        public static string? GetConfiguredApiKey()
        {
            try
            {
                object? value = RegistryHelper.GetValue(RegistryKeyPath, ApiKeyValueName);
                string? key = value as string;
                return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "Unable to read SteamGridDB API key.");
                return null;
            }
        }

        public static void SetConfiguredApiKey(string? apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                RegistryHelper.DeleteValue(RegistryKeyPath, ApiKeyValueName);
                return;
            }

            RegistryHelper.SetValue(
                RegistryKeyPath,
                ApiKeyValueName,
                apiKey.Trim(),
                Microsoft.Win32.RegistryValueKind.String);
        }

        public async Task EnsureArtworkForLibraryAsync(
            IEnumerable<GameEntry> entries,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<Task> tasks = entries.Select(entry => EnsureArtworkAsync(entry, cancellationToken));
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        public async Task<bool> EnsureArtworkAsync(GameEntry entry, CancellationToken cancellationToken = default)
        {
            if (entry.IsCustomArtwork && IsUsableImage(entry.ArtworkPath))
            {
                return false;
            }

            string cachePathJpg = GetCachePath(entry.Id, ".jpg");
            string cachePathPng = GetCachePath(entry.Id, ".png");

            if (IsUsableImage(entry.ArtworkPath))
            {
                return false;
            }

            if (IsUsableImage(cachePathJpg))
            {
                entry.ArtworkPath = cachePathJpg;
                entry.IsCustomArtwork = false;
                return true;
            }

            if (IsUsableImage(cachePathPng))
            {
                entry.ArtworkPath = cachePathPng;
                entry.IsCustomArtwork = false;
                return true;
            }

            await _fetchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (entry.Source == GameSource.Steam &&
                    !string.IsNullOrWhiteSpace(entry.ExternalId) &&
                    await TryDownloadSteamAsync(entry.ExternalId!, cachePathJpg, cancellationToken).ConfigureAwait(false))
                {
                    entry.ArtworkPath = cachePathJpg;
                    entry.IsCustomArtwork = false;
                    return true;
                }

                string? apiKey = GetConfiguredApiKey();
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    return false;
                }

                string? downloaded = await TryDownloadSteamGridDbAsync(
                    entry.Name,
                    apiKey,
                    entry.Id,
                    cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(downloaded) || !IsUsableImage(downloaded))
                {
                    return false;
                }

                entry.ArtworkPath = downloaded;
                entry.IsCustomArtwork = false;
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, $"Artwork fetch failed for '{entry.Name}'.");
                return false;
            }
            finally
            {
                _fetchGate.Release();
            }
        }

        public async Task<string?> ImportCustomArtworkAsync(
            string gameId,
            string sourceImagePath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(gameId) ||
                string.IsNullOrWhiteSpace(sourceImagePath) ||
                !File.Exists(sourceImagePath))
            {
                return null;
            }

            string extension = Path.GetExtension(sourceImagePath);
            if (string.IsNullOrWhiteSpace(extension) ||
                !IsSupportedImageExtension(extension))
            {
                extension = ".png";
            }

            Directory.CreateDirectory(_cacheDirectory);
            string destination = GetCachePath(gameId, extension);

            // Clear prior cached art for this game (any extension) before writing the custom file.
            foreach (string leftover in Directory.EnumerateFiles(_cacheDirectory, $"{SanitizeFileName(gameId)}.*"))
            {
                if (!string.Equals(leftover, destination, StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(leftover);
                }
            }

            await using FileStream source = new(
                sourceImagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                useAsync: true);
            await using FileStream dest = new(
                destination,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                useAsync: true);
            await source.CopyToAsync(dest, cancellationToken).ConfigureAwait(false);
            return destination;
        }

        private static bool IsSupportedImageExtension(string extension) =>
            extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase);

        private async Task<bool> TryDownloadSteamAsync(
            string appId,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            string portraitUrl = string.Format(SteamCdnPortrait, appId);
            if (await TryDownloadUrlAsync(portraitUrl, destinationPath, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            string headerUrl = string.Format(SteamCdnHeader, appId);
            return await TryDownloadUrlAsync(headerUrl, destinationPath, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string?> TryDownloadSteamGridDbAsync(
            string gameName,
            string apiKey,
            string gameId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(gameName))
            {
                return null;
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{SteamGridDbBase}/search/autocomplete/{Uri.EscapeDataString(gameName.Trim())}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using HttpResponseMessage searchResponse = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!searchResponse.IsSuccessStatusCode)
            {
                App.logger.Debug($"SteamGridDB search returned {(int)searchResponse.StatusCode} for '{gameName}'.");
                return null;
            }

            await using Stream searchStream = await searchResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            SteamGridDbSearchResponse? search = await JsonSerializer
                .DeserializeAsync<SteamGridDbSearchResponse>(searchStream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            int? sgdbId = search?.Data?.FirstOrDefault()?.Id;
            if (sgdbId is null)
            {
                return null;
            }

            string gridsUrl =
                $"{SteamGridDbBase}/grids/game/{sgdbId.Value}?dimensions=600x900&types=static&nsfw=false";
            using var gridsRequest = new HttpRequestMessage(HttpMethod.Get, gridsUrl);
            gridsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using HttpResponseMessage gridsResponse = await Http.SendAsync(gridsRequest, cancellationToken).ConfigureAwait(false);
            if (!gridsResponse.IsSuccessStatusCode)
            {
                // Fall back without dimension filter.
                gridsUrl = $"{SteamGridDbBase}/grids/game/{sgdbId.Value}?types=static&nsfw=false";
                using var fallbackRequest = new HttpRequestMessage(HttpMethod.Get, gridsUrl);
                fallbackRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                using HttpResponseMessage fallbackResponse = await Http.SendAsync(fallbackRequest, cancellationToken).ConfigureAwait(false);
                if (!fallbackResponse.IsSuccessStatusCode)
                {
                    return null;
                }

                return await DownloadFirstGridAsync(fallbackResponse, gameId, cancellationToken).ConfigureAwait(false);
            }

            return await DownloadFirstGridAsync(gridsResponse, gameId, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string?> DownloadFirstGridAsync(
            HttpResponseMessage gridsResponse,
            string gameId,
            CancellationToken cancellationToken)
        {
            await using Stream stream = await gridsResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            SteamGridDbGridsResponse? grids = await JsonSerializer
                .DeserializeAsync<SteamGridDbGridsResponse>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            string? imageUrl = grids?.Data?.FirstOrDefault()?.Url;
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                return null;
            }

            string extension = imageUrl.Contains(".png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
            string destination = GetCachePath(gameId, extension);
            if (!await TryDownloadUrlAsync(imageUrl, destination, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return destination;
        }

        private static async Task<bool> TryDownloadUrlAsync(
            string url,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            try
            {
                using HttpResponseMessage response = await Http.GetAsync(
                    url,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                string? contentType = response.Content.Headers.ContentType?.MediaType;
                if (!string.IsNullOrWhiteSpace(contentType) &&
                    !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                string? directory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string tempPath = destinationPath + ".partial";
                await using (Stream network = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (FileStream file = new(
                    tempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    useAsync: true))
                {
                    await network.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
                }

                if (new FileInfo(tempPath).Length < 256)
                {
                    TryDelete(tempPath);
                    return false;
                }

                File.Copy(tempPath, destinationPath, overwrite: true);
                TryDelete(tempPath);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, $"Failed to download artwork from {url}");
                TryDelete(destinationPath + ".partial");
                return false;
            }
        }

        private string GetCachePath(string gameId, string extension) =>
            Path.Combine(_cacheDirectory, $"{SanitizeFileName(gameId)}{extension}");

        private static string SanitizeFileName(string gameId)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            return string.Concat(gameId.Select(c => invalid.Contains(c) ? '_' : c));
        }

        private static bool IsUsableImage(string? path) =>
            !string.IsNullOrWhiteSpace(path) && File.Exists(path) && new FileInfo(path).Length > 0;

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(20)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SynToolkit/1.0");
            return client;
        }

        private sealed class SteamGridDbSearchResponse
        {
            public List<SteamGridDbGame>? Data { get; set; }
        }

        private sealed class SteamGridDbGame
        {
            public int Id { get; set; }
            public string? Name { get; set; }
        }

        private sealed class SteamGridDbGridsResponse
        {
            public List<SteamGridDbGrid>? Data { get; set; }
        }

        private sealed class SteamGridDbGrid
        {
            public string? Url { get; set; }
        }
    }
}
