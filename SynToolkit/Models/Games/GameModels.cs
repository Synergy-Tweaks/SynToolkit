#nullable enable

using System;

namespace SynToolkit.Models.Games
{
    public enum GameSource
    {
        Manual = 0,
        Steam = 1,
        Epic = 2,
        Gog = 3,
        Xbox = 4
    }

    public sealed class DetectedGame
    {
        public required string ExternalId { get; init; }
        public required string Name { get; init; }
        public required GameSource Source { get; init; }
        public string? InstallPath { get; init; }
        public string? ExecutablePath { get; init; }
        public string? LaunchUri { get; init; }
        public string? LaunchArgs { get; init; }
        public string? IconPath { get; init; }
        public string? WorkingDirectory { get; init; }
    }

    public sealed class GameEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public GameSource Source { get; set; } = GameSource.Manual;
        public string? ExternalId { get; set; }
        public string? InstallPath { get; set; }
        public string? ExecutablePath { get; set; }
        public string? LaunchUri { get; set; }
        public string? LaunchArgs { get; set; }
        public string? WorkingDirectory { get; set; }
        public string? IconPath { get; set; }
        public DateTimeOffset? LastPlayed { get; set; }
        public int? PlaytimeMinutes { get; set; }
        public bool IsManual { get; set; }
    }
}
