#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SynToolkit.Models.Games;

namespace SynToolkit.Services.Games
{
    public interface IGameArtworkService
    {
        /// <summary>
        /// Resolves cover art for a game: custom art wins, then disk cache, then Steam CDN / SteamGridDB.
        /// Updates <see cref="GameEntry.ArtworkPath"/> in place when art is found. Never throws for network failures.
        /// </summary>
        Task<bool> EnsureArtworkAsync(GameEntry entry, CancellationToken cancellationToken = default);

        Task EnsureArtworkForLibraryAsync(
            IEnumerable<GameEntry> entries,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Copies a user-picked image into the artwork cache and returns the cached path.
        /// </summary>
        Task<string?> ImportCustomArtworkAsync(
            string gameId,
            string sourceImagePath,
            CancellationToken cancellationToken = default);

        string GetCacheDirectory();
    }
}
