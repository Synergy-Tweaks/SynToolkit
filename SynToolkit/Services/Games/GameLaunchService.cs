#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SynToolkit.Models.Games;

namespace SynToolkit.Services.Games
{
    public sealed class GameLaunchService : IGameLaunchService
    {
        private readonly IGameLibraryService _libraryService;

        public GameLaunchService(IGameLibraryService libraryService)
        {
            _libraryService = libraryService;
        }

        public async Task<GameLaunchResult> LaunchAsync(GameEntry game, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                Process? process = null;

                if (!string.IsNullOrWhiteSpace(game.LaunchUri))
                {
                    process = Process.Start(new ProcessStartInfo
                    {
                        FileName = game.LaunchUri,
                        UseShellExecute = true
                    });
                }
                else if (!string.IsNullOrWhiteSpace(game.ExecutablePath))
                {
                    if (!File.Exists(game.ExecutablePath))
                    {
                        return new GameLaunchResult(false, "The game executable is missing. Re-locate or remove this entry.", null);
                    }

                    process = Process.Start(new ProcessStartInfo
                    {
                        FileName = game.ExecutablePath,
                        Arguments = game.LaunchArgs ?? string.Empty,
                        WorkingDirectory = !string.IsNullOrWhiteSpace(game.WorkingDirectory) && Directory.Exists(game.WorkingDirectory)
                            ? game.WorkingDirectory
                            : Path.GetDirectoryName(game.ExecutablePath) ?? string.Empty,
                        UseShellExecute = true
                    });
                }
                else
                {
                    return new GameLaunchResult(false, "This game has no launch path or URI.", null);
                }

                game.LastPlayed = DateTimeOffset.Now;
                await _libraryService.UpdateGameAsync(game, cancellationToken).ConfigureAwait(false);

                if (process is not null)
                {
                    try
                    {
                        process.EnableRaisingEvents = true;
                        process.Exited += (_, _) =>
                        {
                            App.ClearDiscordPlayingPresence();
                            process.Dispose();
                        };
                    }
                    catch
                    {
                        // Protocol launches often return a short-lived helper process.
                    }

                    App.SetDiscordPlayingPresence(game.Name);
                    return new GameLaunchResult(true, null, process.Id);
                }

                // URI launches may not return a process handle; still set presence briefly.
                App.SetDiscordPlayingPresence(game.Name);
                return new GameLaunchResult(true, null, null);
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, $"Failed to launch game '{game.Name}'.");
                return new GameLaunchResult(false, exception.Message, null);
            }
        }
    }
}
