#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SynToolkit.Services.Uninstaller;

public sealed record QueueStepOutcome(bool Succeeded, string Message);

public enum QueueStepState
{
    Removed,
    Failed,
    Skipped,
    Cancelled
}

public sealed record QueueStepResult<TItem>(TItem Item, QueueStepState State, string Message);

public sealed record QueueRunResult<TItem>(
    int Removed,
    int Failed,
    int Skipped,
    int Cancelled,
    IReadOnlyList<QueueStepResult<TItem>> Steps);

public static class UninstallQueueEngine
{
    public static async Task<QueueRunResult<TItem>> RunAsync<TItem>(
        IReadOnlyList<TItem> items,
        Func<TItem, CancellationToken, Task<QueueStepOutcome>> uninstallAction,
        Func<bool> isCancelRequested,
        Action<int, int, TItem>? onStepStarting = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(uninstallAction);
        ArgumentNullException.ThrowIfNull(isCancelRequested);

        var steps = new List<QueueStepResult<TItem>>(items.Count);
        int removed = 0;
        int failed = 0;
        int skipped = 0;
        int cancelled = 0;

        for (int index = 0; index < items.Count; index++)
        {
            TItem item = items[index];
            if (isCancelRequested())
            {
                for (int remainingIndex = index; remainingIndex < items.Count; remainingIndex++)
                {
                    steps.Add(new QueueStepResult<TItem>(
                        items[remainingIndex],
                        QueueStepState.Cancelled,
                        "Cancelled before this item started."));
                    skipped++;
                    cancelled++;
                }

                break;
            }

            onStepStarting?.Invoke(index, items.Count, item);
            try
            {
                QueueStepOutcome outcome = await uninstallAction(item, cancellationToken);
                if (outcome.Succeeded)
                {
                    steps.Add(new QueueStepResult<TItem>(item, QueueStepState.Removed, outcome.Message));
                    removed++;
                }
                else
                {
                    steps.Add(new QueueStepResult<TItem>(item, QueueStepState.Failed, outcome.Message));
                    failed++;
                }
            }
            catch (OperationCanceledException)
            {
                steps.Add(new QueueStepResult<TItem>(item, QueueStepState.Cancelled, "Cancelled."));
                skipped++;
                cancelled++;

                for (int remainingIndex = index + 1; remainingIndex < items.Count; remainingIndex++)
                {
                    steps.Add(new QueueStepResult<TItem>(
                        items[remainingIndex],
                        QueueStepState.Cancelled,
                        "Cancelled before this item started."));
                    skipped++;
                    cancelled++;
                }

                break;
            }
            catch (Exception exception)
            {
                steps.Add(new QueueStepResult<TItem>(item, QueueStepState.Failed, exception.Message));
                failed++;
            }
        }

        return new QueueRunResult<TItem>(removed, failed, skipped, cancelled, steps);
    }
}
