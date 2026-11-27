// Portions adapted from Roomy (MIT) © 2026 krezio — https://github.com/krezio/roomy
using SynToolkit.Drives.Core;

#nullable enable

namespace SynToolkit.Services;

public sealed class DrivesCleanerService
{
    public Task<IReadOnlyList<CleanupCategory>> ScanAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            ScanContext context = new()
            {
                IsAdmin = Admin.IsElevated,
                Token = cancellationToken
            };

            List<CleanupCategory> categories = [];
            foreach (ScanStep step in Scanner.Steps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CleanupCategory? category = step.Run(context);
                if (category is not null && (category.Blocked || category.Size > 0))
                {
                    categories.Add(category);
                }
            }

            return (IReadOnlyList<CleanupCategory>)categories;
        }, cancellationToken);
    }

    public Task<CleanResult> CleanAsync(
        IReadOnlyList<CleanJob> jobs,
        IProgress<(double Fraction, string Step)>? progress,
        bool recycleReview = false,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Cleaner.Run(jobs, progress, recycleReview, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Walks the drive once and returns both the folder tree and the biggest files
    /// found along the way (previously the drive was walked twice).
    /// </summary>
    public Task<SpaceMapResult> ScanSpaceMapAsync(string driveRoot, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            SpaceScanner scanner = new();
            SpaceNode root = scanner.Scan(driveRoot, cancellationToken);
            return new SpaceMapResult(root, scanner.BigFiles);
        }, cancellationToken);
    }

    public bool IsElevated => Admin.IsElevated;
}
