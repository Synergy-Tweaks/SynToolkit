// Portions adapted from Roomy (MIT) © 2026 krezio — https://github.com/krezio/roomy
#nullable enable

namespace SynToolkit.Drives.Core;

public sealed record CleanJob(CleanupCategory Category, IReadOnlyList<CleanupItem> Items);
public sealed record CleanResult(long Freed, long Skipped, int Files, long Recycled = 0);

public static class Cleaner
{
    public static CleanResult Run(
        IReadOnlyList<CleanJob> jobs,
        IProgress<(double Fraction, string Step)>? progress,
        bool recycleReview = false,
        CancellationToken ct = default)
    {
        long total = Math.Max(1, jobs.Sum(job => job.Items.Sum(item => item.Size)));
        long done = 0;
        long freed = 0;
        long skipped = 0;
        long recycled = 0;
        int files = 0;

        if (recycleReview)
        {
            jobs = jobs.OrderBy(job => job.Category.Action == CleanAction.EmptyRecycleBin ? 0 : 1).ToList();
        }

        foreach (CleanJob job in jobs)
        {
            string step = $"Cleaning {job.Category.Title.ToLowerInvariant()}...";
            progress?.Report(((double)done / total, step));

            if (job.Category.Action == CleanAction.EmptyRecycleBin)
            {
                long size = job.Items.Sum(item => item.Size);
                (long before, _) = Native.QueryRecycleBin();
                Native.SHEmptyRecycleBin(IntPtr.Zero, null, Native.SHERB_NOCONFIRMATION | Native.SHERB_NOPROGRESSUI | Native.SHERB_NOSOUND);
                (long after, _) = Native.QueryRecycleBin();
                long cleaned = Math.Max(0, before - after);
                freed += cleaned;
                skipped += Math.Max(0, size - cleaned);
                done += size;
                continue;
            }

            if (job.Category.Action != CleanAction.Delete)
            {
                continue;
            }

            bool recycle = recycleReview && job.Category.Safety == Safety.Review;
            foreach (CleanupItem item in job.Items)
            {
                ct.ThrowIfCancellationRequested();
                long got = 0;
                foreach (string path in item.Paths)
                {
                    try
                    {
                        if (recycle && (item.IsFile || item.WholeFolder))
                        {
                            FileOps.DeleteResult recycleResult = FileOps.Recycle(path, item.MinAge);
                            recycled += recycleResult.Freed;
                            files += recycleResult.Files;
                            got += recycleResult.Freed;
                        }
                        else
                        {
                            FileOps.DeleteResult deleteResult = FileOps.Delete(path, item.MinAge, ct);
                            freed += deleteResult.Freed;
                            got += deleteResult.Freed;
                            files += deleteResult.Files;
                            if (item.WholeFolder)
                            {
                                FileOps.RemoveIfEmpty(path);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                    }
                }

                skipped += Math.Max(0, item.Size - got);
                done += item.Size;
                progress?.Report(((double)done / total, step));
            }
        }

        progress?.Report((1, "All done"));
        return new CleanResult(freed, skipped, files, recycled);
    }
}
