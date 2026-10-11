namespace SynToolkit.SystemInformationTests;

/// <summary>
/// Documents the shared-value ownership split after FsoAndGameBar was divided.
/// FSO owns all GameConfigStore FSE/DSE values; Xbox Game Bar owns capture values.
/// </summary>
internal static class FsoGameBarOwnershipTests
{
    // FullScreenOptimizations owns these six HKCU\System\GameConfigStore values:
    private static readonly string[] FsoOwned =
    [
        "GameDVR_EFSEFeatureFlags",
        "GameDVR_FSEBehaviorMode",
        "GameDVR_FSEBehavior",
        "GameDVR_DSEBehavior",
        "GameDVR_DXGIHonorFSEWindowsCompatible",
        "GameDVR_HonorUserFSEBehaviorMode"
    ];

    // FullScreenOptimizations also owns this HKLM Environment value:
    private const string FsoOwnedHklm = "__COMPAT_LAYER";

    // XboxGameBar owns (including former shared GameDVR_Enabled):
    private static readonly string[] GameBarOwned =
    [
        "GameDVR_Enabled",
        "AppCaptureEnabled",
        "AllowGameDVR",
    ];

    // Values that MUST NEVER be touched by FSO:
    private static readonly string[] FsoMustNotTouch =
    [
        "GameDVR_Enabled",
        "AppCaptureEnabled",
        "UseNexusForGameBarEnabled",
        "AllowGameDVR"
    ];

    public static void OwnedValuesDoNotOverlap()
    {
        foreach (string fso in FsoOwned)
        {
            True(!GameBarOwned.Contains(fso), $"FSO value {fso} must not be in Game Bar set");
        }

        foreach (string gb in GameBarOwned)
        {
            True(!FsoOwned.Contains(gb), $"Game Bar value {gb} must not be in FSO set");
        }

        // Shared-value decision: GameDVR_Enabled → Xbox Game Bar only.
        True(GameBarOwned.Contains("GameDVR_Enabled"), "GameDVR_Enabled owned by Game Bar");
        True(!FsoOwned.Contains("GameDVR_Enabled"), "FSO must not own GameDVR_Enabled");
    }

    public static void FsoNeverTouchesGameBarValues()
    {
        foreach (string forbidden in FsoMustNotTouch)
        {
            True(!FsoOwned.Contains(forbidden), $"FSO must never touch {forbidden}");
        }
    }

    public static void FsoOwnsExactlySixHkcuValues()
    {
        Equal(6, FsoOwned.Length, "FSO owns exactly 6 HKCU values");
        True(FsoOwned.Contains("GameDVR_EFSEFeatureFlags"), "FSO owns GameDVR_EFSEFeatureFlags");
        True(FsoOwned.Contains("GameDVR_FSEBehaviorMode"), "FSO owns GameDVR_FSEBehaviorMode");
        True(FsoOwned.Contains("GameDVR_FSEBehavior"), "FSO owns GameDVR_FSEBehavior");
        True(FsoOwned.Contains("GameDVR_DSEBehavior"), "FSO owns GameDVR_DSEBehavior");
        True(FsoOwned.Contains("GameDVR_DXGIHonorFSEWindowsCompatible"), "FSO owns GameDVR_DXGIHonorFSEWindowsCompatible");
        True(FsoOwned.Contains("GameDVR_HonorUserFSEBehaviorMode"), "FSO owns GameDVR_HonorUserFSEBehaviorMode");
    }

    public static void FsoOwnsCompatLayer()
    {
        Equal("__COMPAT_LAYER", FsoOwnedHklm, "FSO owns __COMPAT_LAYER in HKLM Environment");
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}, Actual: {actual}");
        }
    }
}
