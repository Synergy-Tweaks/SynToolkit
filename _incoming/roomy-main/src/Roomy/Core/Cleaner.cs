using Roomy.Localization;
namespace Roomy.Core;

public sealed record CleanJob(CleanupCategory Category, IReadOnlyList<CleanupItem> Items);

/// <param name="Recycled">Bytes moved to the Recycle Bin. They are not counted in <paramref name="Freed"/>.</param>
public sealed record CleanResult(long Freed, long Skipped, int Files, long Recycled = 0);

public static class Cleaner
{
    /// <param name="recycleReview">Send "your call" files (Downloads) to the Recycle Bin instead of deleting them.</param>
    public static CleanResult Run(IReadOnlyList<CleanJob> jobs, IProgress<(double Fraction, string Step)>? progress,
        bool recycleReview = false, CancellationToken ct = default)
    {
        long total = Math.Max(1, jobs.Sum(j => j.Items.Sum(i => i.Size)));
        long done = 0, freed = 0, skipped = 0, recycled = 0;
        int files = 0;

        // Empty the bin before recycling anything, so this cleanup never wipes the files it just recycled.
        if (recycleReview) jobs = jobs.OrderBy(j => j.Category.Action == CleanAction.EmptyRecycleBin ? 0 : 1).ToList();

        foreach (var job in jobs)
        {
            var step = Loc.Format("CleaningCategory", job.Category.Title.ToLowerInvariant());
            progress?.Report(((double)done / total, step));

            if (job.Category.Action == CleanAction.EmptyRecycleBin)
            {
                long size = job.Items.Sum(i => i.Size);
                var (before, _) = Native.QueryRecycleBin();
                Native.SHEmptyRecycleBin(IntPtr.Zero, null,
                    Native.SHERB_NOCONFIRMATION | Native.SHERB_NOPROGRESSUI | Native.SHERB_NOSOUND);
                var (after, _) = Native.QueryRecycleBin();
                long got = Math.Max(0, before - after);
                freed += got;
                skipped += Math.Max(0, size - got);
                done += size;
                continue;
            }

            if (job.Category.Action != CleanAction.Delete) continue;
            bool recycle = recycleReview && job.Category.Safety == Safety.Review;

            foreach (var item in job.Items)
            {
                ct.ThrowIfCancellationRequested();
                long got = 0;
                foreach (var p in item.Paths)
                {
                    try
                    {
                        if (recycle && (item.IsFile || item.WholeFolder))
                        {
                            var r = FileOps.Recycle(p, item.MinAge);
                            recycled += r.Freed;
                            files += r.Files;
                            got += r.Freed;
                        }
                        else
                        {
                            var r = FileOps.Delete(p, item.MinAge, ct);
                            freed += r.Freed;
                            got += r.Freed;
                            files += r.Files;
                            if (item.WholeFolder) FileOps.RemoveIfEmpty(p);
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { /* protected or vanished: skip */ }
                }
                skipped += Math.Max(0, item.Size - got);
                done += item.Size;
                progress?.Report(((double)done / total, step));
            }
        }

        progress?.Report((1, S.AllDone));
        return new CleanResult(freed, skipped, files, recycled);
    }
}
