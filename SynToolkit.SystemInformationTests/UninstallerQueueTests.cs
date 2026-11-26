using SynToolkit.Services.Uninstaller;

namespace SynToolkit.SystemInformationTests;

internal static class UninstallerQueueTests
{
    internal static void RunAll()
    {
        QueueContinuesAfterFailure();
        CancelStopsAfterCurrentItem();
        SummaryCountsAreCorrect();
    }

    private static void QueueContinuesAfterFailure()
    {
        IReadOnlyList<string> queue = ["Alpha", "Beta", "Gamma"];
        QueueRunResult<string> result = UninstallQueueEngine.RunAsync(
            queue,
            uninstallAction: (item, _) => Task.FromResult(
                item == "Beta"
                    ? new QueueStepOutcome(false, "failed")
                    : new QueueStepOutcome(true, "ok")),
            isCancelRequested: () => false).GetAwaiter().GetResult();

        Equal(2, result.Removed, "Two apps should be removed.");
        Equal(1, result.Failed, "One app should fail.");
        Equal(0, result.Skipped, "No app should be skipped.");
    }

    private static void CancelStopsAfterCurrentItem()
    {
        IReadOnlyList<string> queue = ["One", "Two", "Three"];
        bool cancelRequested = false;
        int started = 0;

        QueueRunResult<string> result = UninstallQueueEngine.RunAsync(
            queue,
            uninstallAction: async (item, _) =>
            {
                started++;
                if (item == "One")
                {
                    cancelRequested = true;
                    await Task.Delay(10);
                }

                return new QueueStepOutcome(true, "ok");
            },
            isCancelRequested: () => cancelRequested).GetAwaiter().GetResult();

        Equal(1, result.Removed, "The current app should finish before cancellation.");
        Equal(2, result.Cancelled, "Remaining queued apps should be cancelled.");
        Equal(2, result.Skipped, "Remaining queued apps should be counted as skipped.");
        Equal(1, started, "Cancellation should prevent starting the next app.");
    }

    private static void SummaryCountsAreCorrect()
    {
        IReadOnlyList<int> queue = [1, 2, 3, 4];
        QueueRunResult<int> result = UninstallQueueEngine.RunAsync(
            queue,
            uninstallAction: (item, _) => Task.FromResult(item switch
            {
                1 => new QueueStepOutcome(true, "ok"),
                2 => new QueueStepOutcome(false, "failed"),
                3 => new QueueStepOutcome(true, "ok"),
                _ => new QueueStepOutcome(false, "failed")
            }),
            isCancelRequested: () => false).GetAwaiter().GetResult();

        Equal(2, result.Removed, "Two successful removals are expected.");
        Equal(2, result.Failed, "Two failures are expected.");
        Equal(0, result.Skipped, "Nothing should be skipped in this scenario.");
        Equal(4, result.Steps.Count, "Each queued app should have one result.");
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected '{expected}', got '{actual}'.");
        }
    }
}
