#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SynToolkit.Models.Games;

namespace SynToolkit.Services.Games.Providers
{
    /// <summary>
    /// Xbox / MS Store detection is intentionally stubbed.
    /// Playnite's XboxLibrary requires Xbox Live account connection to classify installed
    /// UWP packages as games (Programs.GetUWPApps + account title history matching).
    /// Account integration is out of scope for this minimal Games tab, and PackageManager
    /// enumeration alone cannot reliably distinguish games from other UWP apps in our
    /// unpackaged (WindowsPackageType=None) build.
    /// </summary>
    public sealed class XboxGameLibraryProvider : IGameLibraryProvider
    {
        public GameSource Source => GameSource.Xbox;
        public string DisplayName => "Xbox";

        public static string SkipReason { get; } =
            "Xbox/MS Store scanning requires Playnite's Xbox Live account matching. " +
            "PackageManager-only enumeration is unreliable for game classification in unpackaged builds.";

        public Task<IReadOnlyList<DetectedGame>> ScanAsync(CancellationToken cancellationToken = default)
        {
            App.logger.Info($"[Games] Skipping Xbox provider: {SkipReason}");
            return Task.FromResult<IReadOnlyList<DetectedGame>>(Array.Empty<DetectedGame>());
        }
    }
}
