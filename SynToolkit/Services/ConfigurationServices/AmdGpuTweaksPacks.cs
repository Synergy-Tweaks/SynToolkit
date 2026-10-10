#nullable enable

using System.Collections.Generic;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// Grouped AMD DWORD packs from https://github.com/imribiy/amd-gpu-tweaks (active values only).
    /// MPO OverlayTestMode is omitted — SynToolkit already exposes Multi-Plane Overlay separately.
    /// </summary>
    internal static class AmdGpuTweaksPacks
    {
        public static readonly IReadOnlyList<AmdRegistryValue> DriverTelemetry = new[]
        {
            AmdRegistryValue.Dword("ReportAnalytics", 0),
            AmdRegistryValue.Dword("NotifySubscription", 0),
            AmdRegistryValue.Dword("AllowSubscription", 0),
            AmdRegistryValue.Dword("ShowReleaseNotes", 0),
        };

        public static readonly IReadOnlyList<AmdRegistryValue> FeatureLatency = new[]
        {
            AmdRegistryValue.Dword("StutterMode", 0),
            AmdRegistryValue.Dword("KMD_EnableAmdFendrOptions", 0),
            AmdRegistryValue.Dword("KMD_ChillEnabled", 0),
            AmdRegistryValue.Dword("KMD_DeLagEnabled", 1),
            AmdRegistryValue.Dword("KMD_FramePacingSupport", 0),
            AmdRegistryValue.Dword("KMD_RadeonBoostEnabled", 0),
            AmdRegistryValue.Dword("DalDisableStutter", 1),
            AmdRegistryValue.Dword("DisableBlockWrite", 1),
            AmdRegistryValue.Dword("DisableFBCSupport", 1),
            AmdRegistryValue.Dword("DisableFBCForFullScreenApp", 1),
        };

        public static readonly IReadOnlyList<AmdRegistryValue> PowerPlay = new[]
        {
            AmdRegistryValue.Dword("PP_Force3DPerformanceMode", 1),
            AmdRegistryValue.Dword("PP_ForceHighDPMLevel", 1),
            AmdRegistryValue.Dword("PP_SclkDeepSleepDisable", 1),
            AmdRegistryValue.Dword("PP_GfxOffControl", 0),
            AmdRegistryValue.Dword("PP_ThermalAutoThrottlingEnable", 0),
            AmdRegistryValue.Dword("PP_EnableRaceToIdle", 0),
        };

        public static readonly IReadOnlyList<AmdRegistryValue> Ulps = new[]
        {
            AmdRegistryValue.Dword("EnableUlps", 0),
            AmdRegistryValue.String("EnableUlps_NA", "0"),
            AmdRegistryValue.Dword("PP_DisableULPS", 1),
            AmdRegistryValue.Dword("KMD_EnableULPS", 0),
            AmdRegistryValue.Dword("KMD_ForceD3ColdSupport", 0),
        };

        public static readonly IReadOnlyList<AmdRegistryValue> Aspm = new[]
        {
            AmdRegistryValue.Dword("EnableAspmL0s", 0),
            AmdRegistryValue.Dword("EnableAspmL1", 0),
            AmdRegistryValue.Dword("EnableAspmL1SS", 0),
            AmdRegistryValue.Dword("DisableAspmL0s", 1),
            AmdRegistryValue.Dword("DisableAspmL1", 1),
        };

        public static readonly IReadOnlyList<AmdRegistryValue> ClockGating = new[]
        {
            AmdRegistryValue.Dword("DisableGfxClockGating", 1),
            AmdRegistryValue.Dword("DisableVceClockGating", 1),
            AmdRegistryValue.Dword("DisableSamuClockGating", 1),
            AmdRegistryValue.Dword("DisableRomMGCGClockGating", 1),
            AmdRegistryValue.Dword("DisableGfxCoarseGrainClockGating", 1),
            AmdRegistryValue.Dword("DisableGfxMediumGrainClockGating", 1),
            AmdRegistryValue.Dword("DisableGfxFineGrainClockGating", 1),
            AmdRegistryValue.Dword("DisableHdpMGClockGating", 1),
            AmdRegistryValue.Dword("EnableVceSwClockGating", 0),
            AmdRegistryValue.Dword("EnableUvdClockGating", 0),
            AmdRegistryValue.Dword("EnableGfxClockGatingThruSmu", 0),
            AmdRegistryValue.Dword("EnableSysClockGatingThruSmu", 0),
            AmdRegistryValue.Dword("DisableXdmaSclkGating", 1),
            AmdRegistryValue.Dword("DalFineGrainClockGating", 0),
            AmdRegistryValue.Dword("DisableRomMediumGrainClockGating", 1),
            AmdRegistryValue.Dword("DisableNbioMediumGrainClockGating", 1),
            AmdRegistryValue.Dword("DisableMcMediumGrainClockGating", 1),
            AmdRegistryValue.Dword("IRQMgrDisableIHClockGating", 1),
        };

        public static readonly IReadOnlyList<AmdRegistryValue> PowerGating = new[]
        {
            AmdRegistryValue.Dword("DisableGfxMGLS", 1),
            AmdRegistryValue.Dword("DisableHdpClockPowerGating", 1),
            AmdRegistryValue.Dword("DisableUVDPowerGating", 1),
            AmdRegistryValue.Dword("DisableVCEPowerGating", 1),
            AmdRegistryValue.Dword("DisableAcpPowerGating", 1),
            AmdRegistryValue.Dword("DisableDrmdmaPowerGating", 1),
            AmdRegistryValue.Dword("DisableGfxCGPowerGating", 1),
            AmdRegistryValue.Dword("DisableStaticGfxMGPowerGating", 1),
            AmdRegistryValue.Dword("DisableDynamicGfxMGPowerGating", 1),
            AmdRegistryValue.Dword("DisableCpPowerGating", 1),
            AmdRegistryValue.Dword("DisableGDSPowerGating", 1),
            AmdRegistryValue.Dword("DisableXdmaPowerGating", 1),
            AmdRegistryValue.Dword("DisableGFXPipelinePowerGating", 1),
            AmdRegistryValue.Dword("DisableQuickGfxMGPowerGating", 1),
            AmdRegistryValue.Dword("DisablePowerGating", 1),
            AmdRegistryValue.Dword("SMU_DisableMmhubPowerGating", 1),
            AmdRegistryValue.Dword("SMU_DisableAthubPowerGating", 1),
        };

        public static readonly IReadOnlyList<AmdRegistryValue> DisplayPower = new[]
        {
            AmdRegistryValue.Dword("DalForceMaxDisplayClock", 1),
            AmdRegistryValue.Dword("DalDisableClockGating", 1),
            AmdRegistryValue.Dword("DalDisableDeepSleep", 1),
            AmdRegistryValue.Dword("DalDisableDiv2", 1),
        };

        public static readonly IReadOnlyList<AmdRegistryValue> SpreadSpectrum = new[]
        {
            AmdRegistryValue.Dword("EnableSpreadSpectrum", 0),
            AmdRegistryValue.Dword("EnableVcePllSpreadSpectrum", 0),
        };
    }
}
