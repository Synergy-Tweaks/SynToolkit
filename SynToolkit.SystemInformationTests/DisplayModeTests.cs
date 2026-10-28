using SynToolkit.Services.Display;

namespace SynToolkit.SystemInformationTests;

internal static class DisplayModeTests
{
    public static void RotationSwapsOnlyPortrait()
    {
        Equal((1920u, 1080u), DisplayModeCatalog.EffectiveModeSize(1920, 1080, 1), "Landscape identity");
        Equal((1920u, 1080u), DisplayModeCatalog.EffectiveModeSize(1920, 1080, 3), "Landscape flipped");
        Equal((1080u, 1920u), DisplayModeCatalog.EffectiveModeSize(1920, 1080, 2), "Portrait");
        Equal((1080u, 1920u), DisplayModeCatalog.EffectiveModeSize(1920, 1080, 4), "Portrait flipped");

        // Desktop size -> pre-rotation signal size -> desktop size.
        (uint width, uint height) = DisplayModeCatalog.EffectiveModeSize(2560, 1600, 2);
        Equal((2560u, 1600u), DisplayModeCatalog.EffectiveModeSize(width, height, 2), "Round trip");
    }

    public static void SwapsDimensionsMatchesCcdRotations()
    {
        True(!DisplayModeCatalog.SwapsDimensions(0), "0 is not a rotation");
        True(!DisplayModeCatalog.SwapsDimensions(1), "identity");
        True(DisplayModeCatalog.SwapsDimensions(2), "90 degrees");
        True(!DisplayModeCatalog.SwapsDimensions(3), "180 degrees");
        True(DisplayModeCatalog.SwapsDimensions(4), "270 degrees");
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
