using SynToolkit.Services;

namespace SynToolkit.SystemInformationTests;

internal static class FavoriteIdMigrationTests
{
    public static void LegacyFsoMapsToSplitIds()
    {
        string[] mapped = FavoriteIdMigration.Expand("FsoAndGameBar").ToArray();
        Equal(2, mapped.Length, "Two replacements");
        Equal("FullScreenOptimizations", mapped[0], "FSO id");
        Equal("XboxGameBar", mapped[1], "Game Bar id");
    }

    public static void UnknownIdsPassthroughOrIgnored()
    {
        string[] passthrough = FavoriteIdMigration.Expand("Hags").ToArray();
        Equal(1, passthrough.Length, "passthrough length");
        Equal("Hags", passthrough[0], "passthrough Hags");

        string[] ignored = FavoriteIdMigration.Expand("TotallyUnknownLegacyId", passthroughUnknown: false).ToArray();
        Equal(0, ignored.Length, "unknown ignored without crash");

        string[] empty = FavoriteIdMigration.Expand(null).ToArray();
        Equal(0, empty.Length, "null ignored");
    }

    public static void ProfileKeyExpansion()
    {
        List<string> keys = FavoriteIdMigration.ExpandProfileKeys(
            new[] { "FsoAndGameBar", "Hags", "TotallyUnknown" });
        True(keys.Contains("FullScreenOptimizations"), "has FSO");
        True(keys.Contains("XboxGameBar"), "has Game Bar");
        True(keys.Contains("Hags"), "has Hags");
        True(keys.Contains("TotallyUnknown"), "unknown kept for passthrough profiles");
        True(!keys.Contains("FsoAndGameBar"), "legacy id removed");
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
