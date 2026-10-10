using Microsoft.Win32;
using SynToolkit.Services.Mpo;

namespace SynToolkit.SystemInformationTests;

internal static class MultiPlaneOverlayTests
{
    public static void DetectionRules()
    {
        var disabled = MultiPlaneOverlayValues.DetectCurrentState(true, 5, RegistryValueKind.DWord);
        Equal(MultiPlaneOverlayValues.DetectionKind.Disabled, disabled.Kind, "5 → disabled");

        var enabled = MultiPlaneOverlayValues.DetectCurrentState(true, null, null);
        Equal(MultiPlaneOverlayValues.DetectionKind.Enabled, enabled.Kind, "missing → enabled");

        var other = MultiPlaneOverlayValues.DetectCurrentState(true, 1, RegistryValueKind.DWord);
        Equal(MultiPlaneOverlayValues.DetectionKind.Unsupported, other.Kind, "other → unsupported");

        var error = MultiPlaneOverlayValues.DetectCurrentState(false, null, null);
        Equal(MultiPlaneOverlayValues.DetectionKind.Error, error.Kind, "read fail → error");

        Equal(5u, MultiPlaneOverlayValues.DisableMpoValue, "disable constant is 5");
    }

    public static void DetectionIsFreshEachCall()
    {
        var first = MultiPlaneOverlayValues.DetectCurrentState(true, 5, RegistryValueKind.DWord);
        Equal(MultiPlaneOverlayValues.DetectionKind.Disabled, first.Kind, "first disabled");
        var second = MultiPlaneOverlayValues.DetectCurrentState(true, null, null);
        Equal(MultiPlaneOverlayValues.DetectionKind.Enabled, second.Kind, "second enabled");
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}, Actual: {actual}");
        }
    }
}
