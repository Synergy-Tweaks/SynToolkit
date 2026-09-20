#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SynToolkit.Models.Games;

namespace SynToolkit.Services.Games
{
    public interface IGameLibraryProvider
    {
        GameSource Source { get; }
        string DisplayName { get; }
        Task<IReadOnlyList<DetectedGame>> ScanAsync(CancellationToken cancellationToken = default);
    }

    public interface IGameLibraryService
    {
        Task<IReadOnlyList<GameEntry>> GetLibraryAsync(CancellationToken cancellationToken = default);
        Task SaveLibraryAsync(IEnumerable<GameEntry> games, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<GameEntry>> ScanAndMergeAsync(CancellationToken cancellationToken = default);
        Task<GameEntry> AddManualGameAsync(
            string name,
            string executablePath,
            string? iconPath = null,
            string? launchArgs = null,
            CancellationToken cancellationToken = default);
        Task RemoveGameAsync(string gameId, CancellationToken cancellationToken = default);
        Task UpdateGameAsync(GameEntry entry, CancellationToken cancellationToken = default);
    }

    public interface IGameLaunchService
    {
        Task<GameLaunchResult> LaunchAsync(GameEntry game, CancellationToken cancellationToken = default);
    }

    public sealed record GameLaunchResult(bool Success, string? Message, int? ProcessId);
}
