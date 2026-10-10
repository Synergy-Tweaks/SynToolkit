using SynToolkit.Services.WindowedGames;

namespace SynToolkit.SystemInformationTests;

internal static class WindowedGamesOptimizationTests
{
    public static void DetectionRules()
    {
        AssertKind(true, null, WindowedGamesOptimizationValues.DetectionKind.MissingString);
        AssertKind(true, "SwapEffectUpgradeEnable=1;", WindowedGamesOptimizationValues.DetectionKind.On);
        AssertKind(true, "SwapEffectUpgradeEnable=0;", WindowedGamesOptimizationValues.DetectionKind.Off);
        AssertKind(true, "VRROptimizeEnable=1;", WindowedGamesOptimizationValues.DetectionKind.MissingPair);
        AssertKind(false, null, WindowedGamesOptimizationValues.DetectionKind.Error);

        var malformed = WindowedGamesOptimizationValues.DetectCurrentState(true, ";;;not-a-pair;;;");
        // Malformed segments are tolerated; missing SwapEffect pair → MissingPair
        True(
            malformed.Kind is WindowedGamesOptimizationValues.DetectionKind.MissingPair
                or WindowedGamesOptimizationValues.DetectionKind.Unsupported,
            "Malformed string handled without throwing.");
    }

    public static void WritingPreservesOtherPairs()
    {
        const string existing = "VRROptimizeEnable=1;SwapEffectUpgradeEnable=0;Foo=Bar;";
        string on = WindowedGamesOptimizationValues.BuildUpdatedString(existing, enable: true);
        True(on.Contains("VRROptimizeEnable=1;", StringComparison.Ordinal), "Preserves VRROptimizeEnable");
        True(on.Contains("Foo=Bar;", StringComparison.Ordinal), "Preserves Foo");
        True(on.Contains("SwapEffectUpgradeEnable=1;", StringComparison.Ordinal), "Sets SwapEffect on");

        string off = WindowedGamesOptimizationValues.BuildUpdatedString(existing, enable: false);
        True(off.Contains("VRROptimizeEnable=1;", StringComparison.Ordinal), "Still preserves VRR");
        True(off.Contains("SwapEffectUpgradeEnable=0;", StringComparison.Ordinal), "Sets SwapEffect off");

        string fromMissing = WindowedGamesOptimizationValues.BuildUpdatedString(null, enable: true);
        Equal("SwapEffectUpgradeEnable=1;", fromMissing, "Creates pair when string missing");
    }

    public static void DetectionIsFreshEachCall()
    {
        var first = WindowedGamesOptimizationValues.DetectCurrentState(true, "SwapEffectUpgradeEnable=1;");
        Equal(WindowedGamesOptimizationValues.DetectionKind.On, first.Kind, "first on");
        var second = WindowedGamesOptimizationValues.DetectCurrentState(true, "SwapEffectUpgradeEnable=0;");
        Equal(WindowedGamesOptimizationValues.DetectionKind.Off, second.Kind, "second off");
    }

    private static void AssertKind(bool ok, object? raw, WindowedGamesOptimizationValues.DetectionKind expected)
    {
        var result = WindowedGamesOptimizationValues.DetectCurrentState(ok, raw);
        Equal(expected, result.Kind, $"Expected {expected}");
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}, Actual: {actual}");
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
