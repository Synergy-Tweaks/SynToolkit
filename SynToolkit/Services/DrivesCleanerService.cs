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

    public Task<SpaceNode> ScanSpaceMapAsync(string driveRoot, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            SpaceScanner scanner = new();
            return scanner.Scan(driveRoot, cancellationToken);
        }, cancellationToken);
    }

    public bool IsElevated => Admin.IsElevated;
}
