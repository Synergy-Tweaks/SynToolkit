namespace SynToolkit.SystemInformationTests;

/// <summary>
/// Documents the shared-value ownership split after FsoAndGameBar was divided.
/// </summary>
internal static class FsoGameBarOwnershipTests
{
    // FullScreenOptimizations owns:
    private const string FsoOwned = "GameDVR_FSEBehaviorMode";

    // XboxGameBar owns (including former shared GameDVR_Enabled):
    private static readonly string[] GameBarOwned =
    [
        "GameDVR_Enabled",
        "AppCaptureEnabled",
        "AllowGameDVR",
    ];

    public static void OwnedValuesDoNotOverlap()
    {
        True(!GameBarOwned.Contains(FsoOwned), "FSO value must not be in Game Bar set");
        foreach (string value in GameBarOwned)
        {
            True(value != FsoOwned, $"{value} is owned by Game Bar, not FSO");
        }

        // Shared-value decision: GameDVR_Enabled → Xbox Game Bar only.
        True(GameBarOwned.Contains("GameDVR_Enabled"), "GameDVR_Enabled owned by Game Bar");
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
