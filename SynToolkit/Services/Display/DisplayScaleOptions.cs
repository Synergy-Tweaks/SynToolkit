#nullable enable

using System;
using System.Collections.Generic;

namespace SynToolkit.Services.Display;

public sealed record DisplayScaleOption(int Step, string Label);

public sealed class DisplayScaleLearnedMap
{
    private readonly Dictionary<string, Dictionary<int, int>> _bySource = new(StringComparer.OrdinalIgnoreCase);

    public void Observe(string sourceKey, int step, int percentAtStep)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
            return;

        if (percentAtStep < DisplayScaleOptions.MinPublishedPercent || percentAtStep > DisplayScaleOptions.MaxPublishedPercent)
            return;

        if (!_bySource.TryGetValue(sourceKey, out var steps))
        {
            steps = new Dictionary<int, int>();
            _bySource[sourceKey] = steps;
        }

        steps[step] = percentAtStep;
    }

    public void Clear()
    {
        _bySource.Clear();
    }

    public bool TryGetPercent(string? sourceKey, int step, out int percentAtStep)
    {
        percentAtStep = 0;
        return !string.IsNullOrWhiteSpace(sourceKey)
            && _bySource.TryGetValue(sourceKey, out var steps)
            && steps.TryGetValue(step, out percentAtStep);
    }
}

public static class DisplayScaleOptions
{
    public const int MinPublishedPercent = 100;

    public const int MaxPublishedPercent = 500;

    public const string UnknownStepLabel = "Scale not reported. Change it in Windows Settings.";

    private static readonly int[] PublishedPercents = [100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500];

    public static string GetStepLabel(int step, int curRelativeScaleStep, int percentAtCurrentRung) =>
        GetStepLabel(step, curRelativeScaleStep, percentAtCurrentRung, sourceKey: null, learned: null);

    public static string GetStepLabel(
        int step,
        int curRelativeScaleStep,
        int percentAtCurrentRung,
        string? sourceKey,
        DisplayScaleLearnedMap? learned)
    {
        if (learned is not null && learned.TryGetPercent(sourceKey, step, out var observed))
            return $"{observed}%";

        if (percentAtCurrentRung <= 0)
            return UnknownStepLabel;

        if (step == curRelativeScaleStep)
            return $"{percentAtCurrentRung}%";

        var currentRung = Array.IndexOf(PublishedPercents, percentAtCurrentRung);
        if (currentRung < 0)
            return UnknownStepLabel;

        var targetRung = currentRung + (step - curRelativeScaleStep);
        if (targetRung < 0 || targetRung >= PublishedPercents.Length)
            return UnknownStepLabel;

        return $"{PublishedPercents[targetRung]}%";
    }

    public static IReadOnlyList<DisplayScaleOption> GetLabeledOptions(
        int minRelativeScaleStep, int curRelativeScaleStep, int maxRelativeScaleStep, int percentAtCurrentRung) =>
        GetLabeledOptions(minRelativeScaleStep, curRelativeScaleStep, maxRelativeScaleStep, percentAtCurrentRung, sourceKey: null, learned: null);

    public static IReadOnlyList<DisplayScaleOption> GetLabeledOptions(
        int minRelativeScaleStep,
        int curRelativeScaleStep,
        int maxRelativeScaleStep,
        int percentAtCurrentRung,
        string? sourceKey,
        DisplayScaleLearnedMap? learned)
    {
        var options = new List<DisplayScaleOption>();
        var steps = GetSteps(minRelativeScaleStep, maxRelativeScaleStep);
        for (var i = 0; i < steps.Count; i++)
        {
            var label = GetStepLabel(steps[i], curRelativeScaleStep, percentAtCurrentRung, sourceKey, learned);
            if (label == UnknownStepLabel)
                label = $"Step {i + 1} of {steps.Count}";
            options.Add(new DisplayScaleOption(steps[i], label));
        }

        return options;
    }

    public static string EmptyListMessage(bool readSucceeded) => readSucceeded
        ? "No scale steps were reported for this display, so there is nothing to pick. Change the scale in Windows Settings instead."
        : "Couldn't read the scale steps for this display, so there is nothing to pick. Select the display again to retry.";

    public static string NoSelectionMessage() =>
        "Select a display to see its scale options.";

    public static IReadOnlyList<int> GetSteps(int minRelativeScaleStep, int maxRelativeScaleStep)
    {
        if (maxRelativeScaleStep < minRelativeScaleStep)
            return Array.Empty<int>();

        var steps = new List<int>(maxRelativeScaleStep - minRelativeScaleStep + 1);
        for (var step = minRelativeScaleStep; step <= maxRelativeScaleStep; step++)
            steps.Add(step);

        return steps;
    }

    public static int ClampStep(int requestedStep, int minRelativeScaleStep, int maxRelativeScaleStep)
    {
        if (maxRelativeScaleStep < minRelativeScaleStep)
            return requestedStep;

        if (requestedStep < minRelativeScaleStep)
            return minRelativeScaleStep;

        if (requestedStep > maxRelativeScaleStep)
            return maxRelativeScaleStep;

        return requestedStep;
    }
}
