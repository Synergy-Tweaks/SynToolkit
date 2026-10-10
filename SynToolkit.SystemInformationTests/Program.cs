using SynToolkit.Models;
using SynToolkit.Commands;
using SynToolkit.Services;
using SynToolkit.Services.Bcd;
using SynToolkit.Services.Games;
using SynToolkit.Services.NvidiaProfileInspector;
using SynToolkit.Services.RadeonSlimmer;
using SynToolkit.Utils;
using System.ComponentModel;
using System.Text;

namespace SynToolkit.SystemInformationTests;

internal static class Program
{
    private static int _failures;

    private static int Main()
    {
        Run("Official AME registry path", OfficialRegistryPath);
        Run("No AME metadata", NoMetadata);
        Run("Official AME record", OfficialRecord);
        Run("Official AME binary timestamp", OfficialBinaryTimestamp);
        Run("Invalid AME timestamps are rejected", InvalidBinaryTimestamps);
        Run("Newest official AME record", NewestRecordWins);
        Run("OS overhaul precedes newer utility playbook", OverhaulPlaybookWins);
        Run("Fatal AME records do not identify the OS", FatalRecordIsIgnored);
        Run("Mixed timestamp metadata stays conflict-aware", MixedTimestampConflict);
        Run("Tied timestamp metadata stays deterministic", TiedTimestampConflict);
        Run("Official metadata precedes legacy", RegistryPrecedesLegacy);
        Run("Untimestamped conflicts stay explicit", UntimestampedConflict);
        Run("Malformed official entry is isolated", MalformedEntryIsolated);
        Run("Valid legacy playbook.conf", ValidLegacyConfiguration);
        Run("Latest numeric legacy folder wins", LatestLegacyDirectoryWins);
        Run("Invalid latest legacy folder falls back safely", InvalidLegacyDirectoryFallsBack);
        Run("XML external entities are prohibited", ExternalEntityIsProhibited);
        Run("Oversized legacy metadata is rejected", OversizedConfigurationIsRejected);
        Run("Empty normalized versions are omitted", EmptyNormalizedVersionIsOmitted);
        Run("Invalid AME versions are omitted", InvalidVersionsAreOmitted);
        Run("Same Playbook with conflicting versions stays explicit", SameNameVersionConflict);
        Run("BIOS version is not a custom OS", BiosVersionIsNotCustomWindows);
        Run("SynergyOS version is a custom OS", SynergyOsVersionIsCustomWindows);
        Run("Bare AME generations are not Playbooks", BareAmeGenerationIsNotPlaybook);
        Run("Named versioned Playbook is accepted", NamedVersionedPlaybookIsAccepted);
        Run("Current project metadata precedes legacy history", CurrentProjectMetadataPrecedesLegacy);
        Run("BCD identifiers are strict and canonical", BcdIdentifiersAreCanonical);
        Run("BCD element formats are derived from their numeric type", BcdElementFormatsAreTyped);
        Run("BCD WMI path keys are escaped", BcdWmiPathKeysAreEscaped);
        Run("BCD writes are read back and verified", BcdWritesAreVerified);
        Run("Deleting an absent BCD element is idempotent", BcdDeleteIsIdempotent);
        Run("Async commands prevent concurrent execution", AsyncCommandPreventsConcurrentExecution);
        Run("Async command failures are contained and logged", AsyncCommandFailureIsContained);
        Run("Batch file paths with spaces execute correctly", BatchFilePathsWithSpacesExecuteCorrectly);
        Run("Display-name command safely carries user input", DisplayNameCommandSafelyCarriesUserInput);
        Run("Display-name input is validated", DisplayNameInputIsValidated);
        Run("Radeon bulk selections notify the UI", RadeonBulkSelectionsNotifyTheUi);
        Run("Legacy profile registration match is exact", LegacyRegistrationMatchIsExact);
        Run("Valid legacy profile JSON is accepted", ValidLegacyProfileIsAccepted);
        Run("Malformed legacy profiles are rejected", MalformedLegacyProfilesAreRejected);
        Run("Oversized legacy profiles are rejected", OversizedLegacyProfileIsRejected);
        Run("NVIDIA profile export preserves imported settings", NvidiaProfileExportRoundTrips);
        Run("Identified desktop DIMMs stay separate", IdentifiedDesktopDimmsStaySeparate);
        Run("CPU-Z memory timings are parsed", CpuZMemoryTimingsAreParsed);
        Run("CPU-Z processor details are parsed", CpuZProcessorDetailsAreParsed);
        Run("CPU-Z motherboard details are parsed", CpuZMainboardDetailsAreParsed);
        Run("AMD 3D V-Cache is detected", Amd3dVCacheIsDetected);
        Run("GPU vendor classification matches PCI IDs and names", GpuVendorClassificationMatchesPciIdsAndNames);
        Run("GPU tab icon uses NVIDIA/AMD brands and falls back for Intel/unknown", GpuTabIconUsesVendorBrandsAndSafeFallback);
        Run("Primary GPU vendor prefers discrete NVIDIA then AMD then Intel", PrimaryGpuVendorPrefersDiscreteGpu);
        Run("HAGS classification distinguishes enabled, disabled, and unsupported", HagsClassificationDistinguishesStates);
        Run("Metadata cache retains recent entries and stays bounded", MetadataCacheStaysBounded);
        Run("Metadata cache handles concurrent readers and null values", MetadataCacheHandlesConcurrency);
        Run("Epic store artwork slugs are derived from display names", EpicArtworkSlugsAreDerivedFromNames);
        Run("Ignore durations resolve to future expiries", IgnoreDurationsResolveToFutureExpiries);
        Run("Win32PrioritySeparation preset values are unique", Win32PrioritySeparationTests.PresetValuesAreUnique);
        Run("Win32PrioritySeparation preset mapping", Win32PrioritySeparationTests.PresetMappingIncludesDefaultAndNoBoost);
        Run("Win32PrioritySeparation dropdown order", Win32PrioritySeparationTests.DropdownOrderMatchesSpecification);
        Run("Win32PrioritySeparation detection rules", Win32PrioritySeparationTests.DetectionRules);
        Run("Win32PrioritySeparation custom validation", Win32PrioritySeparationTests.CustomValidation);
        Run("Win32PrioritySeparation helper text uses allowed values", Win32PrioritySeparationTests.HelperTextUsesAllowedValues);
        Run("Win32PrioritySeparation custom dialog prefill", Win32PrioritySeparationTests.PrefillUsesAllowedRawOrFallsBackTo36);
        Run("SystemResponsiveness preset values are unique", SystemResponsivenessTests.PresetValuesAreUnique);
        Run("SystemResponsiveness preset mapping and hex", SystemResponsivenessTests.PresetMappingAndHex);
        Run("SystemResponsiveness dropdown order", SystemResponsivenessTests.DropdownHasExactlyThreeItemsInOrder);
        Run("SystemResponsiveness detection missing and presets", SystemResponsivenessTests.DetectionMissingAndPresets);
        Run("SystemResponsiveness detection unsupported numbers", SystemResponsivenessTests.DetectionUnsupportedNumbers);
        Run("SystemResponsiveness signed -1 normalization", SystemResponsivenessTests.DetectionSignedMinusOneNormalizes);
        Run("SystemResponsiveness wrong registry types", SystemResponsivenessTests.DetectionWrongTypesDoNotThrow);
        Run("SystemResponsiveness access denied is error", SystemResponsivenessTests.DetectionAccessDeniedIsErrorNotDefault);
        Run("SystemResponsiveness detection is fresh each call", SystemResponsivenessTests.DetectionAlwaysUsesFreshInput);
        Run("SystemResponsiveness uint conversion", SystemResponsivenessTests.UIntConversionHandlesSignedNegativeOne);

        Console.WriteLine(_failures == 0
            ? "All SynToolkit service tests passed."
            : $"{_failures} SynToolkit service test(s) failed.");
        return _failures == 0 ? 0 : 1;
    }

    private static void MetadataCacheStaysBounded()
    {
        BoundedCache<int, string> cache = new(2);
        cache.GetOrAdd(1, () => "one");
        cache.GetOrAdd(2, () => "two");
        Equal("one", cache.GetOrAdd(1, () => throw new Exception("Unexpected reload")), "Cached metadata must be reused.");
        cache.GetOrAdd(3, () => "three");
        Equal(2, cache.Count, "Capacity must remain bounded.");
        Equal("reloaded", cache.GetOrAdd(2, () => "reloaded"), "The least recently used entry must be evicted.");
        Throws<ArgumentOutOfRangeException>(() => new BoundedCache<int, int>(0), "Zero capacity must be rejected.");
    }

    private static void MetadataCacheHandlesConcurrency()
    {
        BoundedCache<int, string?> cache = new(8);
        Parallel.For(0, 500, i => Equal($"value-{i % 20}", cache.GetOrAdd(i % 20, () => $"value-{i % 20}"), "Concurrent reads must return correct metadata."));
        True(cache.Count <= 8, "Concurrent insertion must respect capacity.");
        cache.GetOrAdd(-1, () => null);
        True(cache.GetOrAdd(-1, () => throw new Exception("Unexpected null reload")) is null, "Unavailable native metadata should be cached too.");
    }

    private static void IgnoreDurationsResolveToFutureExpiries()
    {
        DateTimeOffset now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

        Equal(now.AddDays(1), NeedsAttentionIgnorePolicy.ResolveExpiry(NeedsAttentionIgnoreDuration.OneDay, now), "One day should be now + 1 day.");
        Equal(now.AddDays(4), NeedsAttentionIgnorePolicy.ResolveExpiry(NeedsAttentionIgnoreDuration.FourDays, now), "Four days should be now + 4 days.");
        Equal(now.AddDays(21), NeedsAttentionIgnorePolicy.ResolveExpiry(NeedsAttentionIgnoreDuration.ThreeWeeks, now), "Three weeks should be now + 21 days.");
        Equal(now.AddMonths(3), NeedsAttentionIgnorePolicy.ResolveExpiry(NeedsAttentionIgnoreDuration.ThreeMonths, now), "Three months should be now + 3 months.");
        Equal(now.AddYears(2), NeedsAttentionIgnorePolicy.ResolveExpiry(NeedsAttentionIgnoreDuration.TwoYears, now), "Two years should be now + 2 years.");
        Equal(DateTimeOffset.MaxValue, NeedsAttentionIgnorePolicy.ResolveExpiry(NeedsAttentionIgnoreDuration.Forever, now), "Forever should never expire.");

        DateTimeOffset tomorrow = NeedsAttentionIgnorePolicy.ResolveExpiry(NeedsAttentionIgnoreDuration.UntilTomorrow, now);
        True(tomorrow > now, "Until tomorrow must still hide the warning.");
        True(tomorrow - now <= TimeSpan.FromDays(1), "Until tomorrow must fall within the next day.");

        True(NeedsAttentionIgnorePolicy.All.Count == 15, "The ignore dropdown exposes the expected number of options.");

        foreach (NeedsAttentionIgnoreDuration duration in NeedsAttentionIgnorePolicy.All)
        {
            True(
                NeedsAttentionIgnorePolicy.ResolveExpiry(duration, now) > now,
                $"Every ignore duration must expire in the future ({duration}).");
            True(
                !string.IsNullOrWhiteSpace(NeedsAttentionIgnorePolicy.GetLocalizationKey(duration)),
                $"Every ignore duration must have a localization key ({duration}).");
        }
    }

    private static void EpicArtworkSlugsAreDerivedFromNames()
    {
        Equal(
            "grand-theft-auto-v",
            EpicArtworkSlug.BuildCandidates("Grand Theft Auto V")[0],
            "A plain title should lower-case and dash-separate.");
        Equal(
            "thems-fightin-herds",
            EpicArtworkSlug.BuildCandidates("Them's Fightin' Herds")[0],
            "Apostrophes must join the surrounding letters rather than split the slug.");
        Equal(
            "warhammer-vermintide-2",
            EpicArtworkSlug.BuildCandidates("Warhammer: Vermintide 2")[0],
            "Punctuation runs must collapse to a single separator.");

        IReadOnlyList<string> definitive =
            EpicArtworkSlug.BuildCandidates("Ori and the Blind Forest: Definitive Edition");
        True(
            definitive.Contains("ori-and-the-blind-forest"),
            "A trailing edition suffix should produce a base-product candidate.");

        IReadOnlyList<string> withParenthetical = EpicArtworkSlug.BuildCandidates("Fortnite (Battle Royale)");
        True(
            withParenthetical.Contains("fortnite"),
            "A parenthetical qualifier should produce a stripped candidate.");

        IReadOnlyList<string> subtitled =
            EpicArtworkSlug.BuildCandidates("Fallout 2: A Post Nuclear Role Playing Game");
        Equal(
            "fallout-2-a-post-nuclear-role-playing-game",
            subtitled[0],
            "The full subtitle form should still be tried first.");
        True(
            subtitled.Contains("fallout-2"),
            "Epic drops the marketing subtitle from the slug, so the short title should be a candidate.");

        True(
            EpicArtworkSlug.BuildCandidates("   ").Count == 0,
            "Blank names must not produce candidates.");
    }

    private static void FragmentedOnboardMemoryChipsAreLabeled()
    {
        MemoryModuleSpec[] firmwareEntries = Enumerable.Range(0, 8)
            .Select(_ => new MemoryModuleSpec(null, 2UL * 1024 * 1024 * 1024, 4800))
            .ToArray();

        IReadOnlyList<MemoryModuleSpec> normalized = MemoryModuleInventoryNormalizer.Normalize(
            firmwareEntries,
            16UL * 1024 * 1024 * 1024 - 320UL * 1024 * 1024);

        Equal(8, normalized.Count, "Individual onboard chips should remain separate rows.");
        True(
            normalized.All(module => module.Manufacturer == "Onboard Memory Chip"),
            "Each unidentified onboard chip should receive the onboard-memory label.");
        True(
            normalized.All(module => module.CapacityBytes == 2UL * 1024 * 1024 * 1024 && module.SpeedMHz == 4800),
            "Each onboard chip must retain its own capacity and reported speed.");
    }

    private static void IdentifiedDesktopDimmsStaySeparate()
    {
        MemoryModuleSpec[] desktopDimms =
        {
            new("Gold Key Technology Co Ltd", 8UL * 1024 * 1024 * 1024, 3200, IsMemoryStick: true),
            new("Gold Key Technology Co Ltd", 8UL * 1024 * 1024 * 1024, 3200, IsMemoryStick: true)
        };

        IReadOnlyList<MemoryModuleSpec> normalized = MemoryModuleInventoryNormalizer.Normalize(
            desktopDimms,
            16UL * 1024 * 1024 * 1024);

        Equal(2, normalized.Count, "Normal identified DIMMs must remain separate rows.");
        Equal("Slot 1", normalized[0].SlotLabel, "The first stick must receive Slot 1.");
        Equal("Slot 2", normalized[1].SlotLabel, "The second stick must receive Slot 2.");
    }

    private static void CpuZMemoryTimingsAreParsed()
    {
        const string report = "Memory Type          DDR5\r\n" +
                              "CAS# latency (CL)   40.0\r\n" +
                              "RAS# to CAS# delay (tRCD) 40\r\n" +
                              "RAS# Precharge (tRP) 40\r\n" +
                              "Cycle Time (tRAS)   77\r\n";

        CpuZMemoryTimings? timings = CpuZMemoryTimingParser.TryParse(report);

        Equal("DDR5", timings?.MemoryType, "CPU-Z's memory type should be retained.");
        Equal("CL40 40-40-40-77", timings?.TimingText, "CPU-Z's live primary timings should be formatted consistently.");
    }

    private static void CpuZProcessorDetailsAreParsed()
    {
        const string report = "Processors Information\r\n" +
                              "-------------------------------------------------------------------------\r\n" +
                              "Socket 1\t\tID = 0\r\n" +
                              "\tNumber of cores\t\t14 (max 14)\r\n" +
                              "\tNumber of threads\t20 (max 20)\r\n" +
                              "\tHybrid\t\tyes, 2 coresets\r\n" +
                              "\tCore Set 0\t\tP-Cores, 6 cores, 12 threads\r\n" +
                              "\tCore Set 1\t\tE-Cores, 8 cores, 8 threads\r\n" +
                              "\tManufacturer\t\tGenuineIntel\r\n" +
                              "\tName\t\tIntel Core i5 13500\r\n" +
                              "\tCodename\t\tRaptor Lake\r\n" +
                              "\tPackage (platform ID)\tSocket 1700 LGA\r\n" +
                              "\tTechnology\t\t10 nm\r\n" +
                              "\tCPUID\t\t6.F.2\r\n" +
                              "\tCore Stepping\t\tC0\r\n" +
                              "\tInstructions sets\tMMX, SSE, AVX2\r\n" +
                              "\tTDP Limit\t\t65.0 Watts\r\n" +
                              "\tTjmax\t\t100.0 °C\r\n" +
                              "\tBase frequency (cores)\t99.8 MHz\r\n" +
                              "\tL1 Data cache\t\t6 x 48 KB (12-way) + 8 x 32 KB (8-way)\r\n" +
                              "\tL1 Instruction cache\t6 x 32 KB (8-way) + 8 x 64 KB (8-way)\r\n" +
                              "\tL2 cache\t\t6 x 1.25 MB (10-way) + 2 x 2 MB (16-way)\r\n" +
                              "\tL3 cache\t\t24 MB (12-way)\r\n" +
                              "\tMax turbo ratio\t\t48x\r\n" +
                              "\tMin operating ratio\t4x\r\n" +
                              "\tRatio 1 P-Core\t\t48x\r\n" +
                              "\tRatio 1 E-Core\t\t35x\r\n" +
                              "\r\nThread dumps\r\n";

        CpuZProcessorDetails? details = CpuZProcessorDetailsParser.TryParse(report);

        Equal("Raptor Lake", details?.Codename, "CPU-Z's codename should be retained.");
        Equal(6, details?.PerformanceCores?.Cores, "P-core count should be parsed.");
        Equal(8, details?.EfficientCores?.Cores, "E-core count should be parsed.");
        Equal(4790.4m, details?.PerformanceCores?.MaximumFrequencyMHz, "P-core maximum should use the P-core ratio.");
        Equal(3493m, details?.EfficientCores?.MaximumFrequencyMHz, "E-core maximum should use the E-core ratio.");
        Equal(399.2m, details?.MinimumFrequencyMHz, "Minimum operating frequency should use its ratio.");
        Equal("6 × 48 KB + 8 × 32 KB", details?.L1DataCache, "Cache associativity should be omitted while preserving the hybrid cache layout.");
        Equal("100 \u00B0C", details?.TemperatureLimit, "Temperature output must use a valid degree symbol.");
    }
    private static void CpuZMainboardDetailsAreParsed()
    {
        const string report = "Chipset\r\n" +
                              "Northbridge\t\tIntel Alder Lake rev. 02\r\n" +
                              "Southbridge\t\tIntel B660 rev. 11\r\n" +
                              "Bus Specification\t\tPCI-Express 4.0 (16.0 GT/s)\r\n" +
                              "Graphic Interface\t\tPCI-Express 5.0\r\n" +
                              "Mainboard Model\t\tB660M DS3H AX DDR4 (0x00000444 - 0x8461EA80)\r\n" +
                              "LPCIO Vendor\t\tITE\r\n" +
                              "LPCIO Model\t\tIT8689\r\n";

        CpuZMainboardDetails? details = CpuZMainboardDetailsParser.TryParse(report);

        Equal("B660M DS3H AX DDR4", details?.Model, "CPU-Z's board model should omit the internal hardware ID.");
        Equal("Intel Alder Lake rev. 02", details?.Northbridge, "CPU-Z's northbridge should be retained.");
        Equal("Intel B660 rev. 11", details?.Southbridge, "CPU-Z's southbridge should be retained.");
        Equal("PCI-Express 4.0 (16.0 GT/s)", details?.BusSpecification, "CPU-Z's mainboard bus should be retained.");
        Equal("ITE", details?.LpcioVendor, "CPU-Z's LPCIO vendor should be retained.");
        Equal("IT8689", details?.LpcioModel, "CPU-Z's LPCIO model should be retained.");
    }
    private static void Amd3dVCacheIsDetected()
    {
        const string report = "Processors Information\r\n" +
                              "\tManufacturer\t\tAuthenticAMD\r\n" +
                              "\tName\t\tAMD Ryzen 7 7800X3D\r\n" +
                              "\tL3 cache\t\t96 MB (16-way)\r\n" +
                              "\r\nThread dumps\r\n";

        CpuZProcessorDetails? details = CpuZProcessorDetailsParser.TryParse(report);

        True(details?.HasAmd3dVCache == true, "An AMD X3D processor must expose its 3D V-Cache designation.");
        Equal("96 MB", details?.L3Cache, "The AMD L3 cache capacity should remain visible.");
    }
    private static void OfficialRegistryPath() =>
        Equal(
            @"SOFTWARE\AME\Playbooks\Applied",
            WindowsAmePlaybookMetadataSource.AppliedRegistryPath,
            "The detector must follow AME Wizard's official applied-Playbook hierarchy.");

    private static void DisplayNameCommandSafelyCarriesUserInput()
    {
        const string sid = "S-1-5-21-1-2-3-1001";
        const string input = "  O'Brien; $(throw 'boom') — テスト  ";
        const string normalized = "O'Brien; $(throw 'boom') — テスト";

        string encodedCommand = LocalUserDisplayNameCommand.CreateEncodedPowerShellCommand(sid, input);
        string script = Encoding.Unicode.GetString(Convert.FromBase64String(encodedCommand));
        string encodedDisplayName = Convert.ToBase64String(Encoding.Unicode.GetBytes(normalized));

        True(script.Contains("Set-LocalUser", StringComparison.Ordinal), "The command must use Set-LocalUser.");
        True(script.Contains($"-SID '{sid}'", StringComparison.Ordinal), "The command must target the current SID.");
        True(script.Contains(encodedDisplayName, StringComparison.Ordinal), "The command must preserve the Unicode display name as data.");
        True(!script.Contains(normalized, StringComparison.Ordinal), "The display name must never be interpolated as PowerShell syntax.");
    }

    private static void DisplayNameInputIsValidated()
    {
        Equal("Jane Doe", LocalUserDisplayNameCommand.Normalize("  Jane Doe  "), "Outer whitespace should be trimmed.");
        Throws<ArgumentException>(
            () => LocalUserDisplayNameCommand.Normalize("   "),
            "A blank display name must be rejected before Windows is called.");
        Throws<ArgumentException>(
            () => LocalUserDisplayNameCommand.Normalize("Jane\nDoe"),
            "Control characters must not enter the PowerShell command.");
        Throws<ArgumentException>(
            () => LocalUserDisplayNameCommand.Normalize(new string('x', LocalUserDisplayNameCommand.MaximumDisplayNameLength + 1)),
            "An overlong display name must be rejected before Windows is called.");
    }

    private static void BiosVersionIsNotCustomWindows()
    {
        CustomWindowsInformation? result = SystemInformationService.TryParseCustomWindowsMarker(
            "BIOS 1.2",
            "Windows OEM model");
        True(result is null, "A BIOS version must not be presented as a custom Windows build.");
    }

    private static void SynergyOsVersionIsCustomWindows()
    {
        CustomWindowsInformation? result = SystemInformationService.TryParseCustomWindowsMarker(
            "SynergyOS 1.5.1",
            "Windows OEM model");
        Equal("SynergyOS 1.5.1", result?.DisplayName, "A complete SynergyOS marker should be detected.");
        Equal("Windows OEM model", result?.Source, "The custom-Windows source should be preserved.");
    }

    private static void BareAmeGenerationIsNotPlaybook()
    {
        foreach (string candidate in new[] { "AME 10", "AME 11" })
        {
            PlaybookInformation? result = SystemInformationService.TryParseFallbackPlaybookMarker(
                candidate,
                "Windows OEM manufacturer");
            True(result is null, $"Bare OEM marker '{candidate}' must not be guessed as a Playbook.");
        }
    }

    private static void NamedVersionedPlaybookIsAccepted()
    {
        PlaybookInformation? result = SystemInformationService.TryParseFallbackPlaybookMarker(
            "SynergyOS Playbook 1.5.1",
            "Windows OEM model");
        Equal(PlaybookDetectionStatus.Detected, result?.Status, "A literal named and versioned Playbook should be detected.");
        Equal("SynergyOS Playbook", result?.Name, "The Playbook name should exclude trailing version text.");
        Equal("1.5.1", result?.Version, "The Playbook version should be preserved.");
    }

    private static void CurrentProjectMetadataPrecedesLegacy()
    {
        PlaybookInformation current = new(
            PlaybookDetectionStatus.Detected,
            "SynergyOS Playbook",
            "1.5.1",
            @"HKLM\SOFTWARE\SynergyOS\Playbook");
        PlaybookInformation legacy = new(
            PlaybookDetectionStatus.Detected,
            "Legacy AME",
            "0.7",
            @"C:\ProgramData\AME\AppliedPlaybooks\1\playbook.conf");

        PlaybookInformation selected = SystemInformationService.PreferCurrentPlaybook(current, legacy);
        Equal(current, selected, "Current project-specific metadata must take priority over legacy history.");

        PlaybookInformation notDetected = new(PlaybookDetectionStatus.NotDetected, null, null, null);
        PlaybookInformation legacyFallback = SystemInformationService.PreferCurrentPlaybook(notDetected, legacy);
        Equal(legacy, legacyFallback, "Legacy history should remain available when no current marker exists.");
    }

    private static void BcdIdentifiersAreCanonical()
    {
        Equal(
            WellKnownObjectIdentifiers.Current,
            BcdContract.NormalizeObjectIdentifier("{fa926493-6f1c-4193-a414-58f0b2456d1e}"),
            "A valid BCD GUID should be normalized without changing its identity.");
        Throws<ArgumentException>(
            () => BcdContract.NormalizeObjectIdentifier("{current}"),
            "Aliases and path fragments must not reach a WMI instance path.");
    }

    private static void BcdElementFormatsAreTyped()
    {
        Equal(
            BcdElementValueKind.Boolean,
            BcdContract.GetValueKind(WellKnownElementTypes.AdvancedOptions),
            "AdvancedOptions should be a Boolean BCD element.");
        Equal(
            BcdElementValueKind.Integer,
            BcdContract.GetValueKind(WellKnownElementTypes.BootStatusPolicy),
            "BootStatusPolicy should be an integer BCD element.");
        Throws<NotSupportedException>(
            () => BcdContract.GetValueKind(0x12000001),
            "Unsupported BCD value formats should fail closed.");
    }

    private static void BcdWmiPathKeysAreEscaped()
    {
        Equal(
            @"C:\\Boot",
            BcdContract.EscapeManagementPathKey(@"C:\Boot"),
            "Backslashes must be escaped before building a WMI object path.");
        Equal(
            "say \\\"hello\\\"",
            BcdContract.EscapeManagementPathKey("say \"hello\""),
            "Quotes must be escaped before building a WMI object path.");
        Throws<ArgumentException>(
            () => BcdContract.EscapeManagementPathKey("bad\0key"),
            "Null characters must not enter a WMI object path.");
    }

    private static void BcdWritesAreVerified()
    {
        FakeBcdProvider provider = new();
        BcdService service = new(provider);
        service.SetBooleanElement(
            WellKnownObjectIdentifiers.GlobalSettings,
            WellKnownElementTypes.AdvancedOptions,
            true);
        Equal(
            true,
            service.GetElementValue(
                WellKnownObjectIdentifiers.GlobalSettings,
                WellKnownElementTypes.AdvancedOptions),
            "A retained Boolean value should be returned.");

        provider.RetainWrites = false;
        Throws<InvalidOperationException>(
            () => service.SetIntegerElement(
                WellKnownObjectIdentifiers.Current,
                WellKnownElementTypes.BootStatusPolicy,
                1UL),
            "A WMI provider that does not retain the requested value must be rejected.");
    }

    private static void BcdDeleteIsIdempotent()
    {
        FakeBcdProvider provider = new();
        BcdService service = new(provider);
        service.DeleteElement(
            WellKnownObjectIdentifiers.GlobalSettings,
            WellKnownElementTypes.HighestMode);
        Equal(0, provider.DeleteCalls, "An already-absent element should not be deleted again.");
    }

    private static void AsyncCommandPreventsConcurrentExecution()
    {
        SynToolkit.App.logger.ResetErrors();
        GatedAsyncCommand command = new();
        int canExecuteNotifications = 0;
        command.CanExecuteChanged += (_, _) => Interlocked.Increment(ref canExecuteNotifications);

        command.Execute(null);
        True(command.WaitUntilStarted(), "The first asynchronous command invocation should start.");
        True(command.IsExecuting, "A running command should expose its busy state.");
        True(!command.CanExecute(null), "A running command must report that it cannot execute again.");

        command.Execute(null);
        Equal(1, command.ExecutionCount, "A second invocation must be ignored while the first is running.");

        command.Release();
        True(
            SpinWait.SpinUntil(() => !command.IsExecuting, TimeSpan.FromSeconds(2)),
            "The command should leave its busy state after completion.");
        Equal(1, command.ExecutionCount, "Only one asynchronous operation should have run.");
        True(command.CanExecute(null), "A completed command should become executable again.");
        True(canExecuteNotifications >= 2, "The command should notify bindings when execution starts and ends.");
        Equal(0, SynToolkit.App.logger.ErrorCount, "A successful command should not log an error.");
    }

    private static void AsyncCommandFailureIsContained()
    {
        SynToolkit.App.logger.ResetErrors();
        FailingAsyncCommand command = new();

        command.Execute(null);

        True(
            SpinWait.SpinUntil(
                () => !command.IsExecuting && SynToolkit.App.logger.ErrorCount == 1,
                TimeSpan.FromSeconds(2)),
            "A failed command should complete, reset its busy state, and log exactly once.");
        True(command.CanExecute(null), "A failed command should not remain permanently disabled.");
    }

    private static void BatchFilePathsWithSpacesExecuteCorrectly()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SynToolkit Batch Tests " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string batchFilePath = Path.Combine(directory, "Safe Mode Test.cmd");
        try
        {
            File.WriteAllText(
                batchFilePath,
                "@echo off\r\nif not \"%~1\"==\"first argument\" exit /b 7\r\necho batch-ok\r\nexit /b 0\r\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            CommandResult result = CommandPromptHelper.RunBatchFileResult(
                batchFilePath,
                ["first argument"],
                timeoutMilliseconds: 10_000);

            True(result.Succeeded, $"A quoted batch path should execute successfully: {result.CombinedOutput}");
            True(result.StandardOutput.Contains("batch-ok", StringComparison.Ordinal), "The batch payload must actually run.");
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static void RadeonBulkSelectionsNotifyTheUi()
    {
        RadeonPackage package = new()
        {
            SourceFile = "manifest.json",
            ProductName = "Package",
            Url = "package.zip",
            Type = "driver",
            Description = "Package description",
        };
        RadeonScheduledTask task = new()
        {
            SourceFile = "task.xml",
            Description = "Scheduled task",
            Command = "task.exe",
        };
        RadeonDisplayComponent component = new()
        {
            DirectoryPath = "DisplayComponent",
            Name = "Display component",
        };

        PropertyChangeIsRaised(package, nameof(RadeonPackage.Keep), () => package.Keep = false);
        PropertyChangeIsRaised(task, nameof(RadeonScheduledTask.Enabled), () => task.Enabled = true);
        PropertyChangeIsRaised(component, nameof(RadeonDisplayComponent.Keep), () => component.Keep = false);
    }

    private static void PropertyChangeIsRaised(
        INotifyPropertyChanged item,
        string expectedPropertyName,
        Action change)
    {
        int notifications = 0;
        item.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == expectedPropertyName)
            {
                notifications++;
            }
        };

        change();
        Equal(1, notifications, $"Changing {expectedPropertyName} must notify its checkbox binding.");
        change();
        Equal(1, notifications, $"Reapplying {expectedPropertyName} must not raise a duplicate notification.");
    }

    private static void LegacyRegistrationMatchIsExact()
    {
        True(
            LegacyProfileMigrationPolicy.IsExactLegacyRegistration(
                " Syntoolkit ",
                "Kwanteks",
                "1.5.0"),
            "The exact legacy Kwanteks registration should be eligible.");
        True(
            !LegacyProfileMigrationPolicy.IsExactLegacyRegistration(
                "Atlas Toolbox",
                "AtlasOS",
                "0.1.13"),
            "The official Atlas registration that shares the old AppId must not be eligible.");
        True(
            !LegacyProfileMigrationPolicy.IsExactLegacyRegistration(
                "Syntoolkit",
                "Kwanteks",
                "1.5.1"),
            "A different SynToolkit version must not be treated as the legacy product.");
    }

    private static void ValidLegacyProfileIsAccepted()
    {
        const string json =
            "{\"Name\":\"Gaming\",\"Config\":[\"Animations\",\"Bluetooth\"]," +
            "\"MultiConfig\":[{\"Key\":\"Mitigations\",\"Value\":\"Default\"}]}";
        byte[] withBom = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(json))
            .ToArray();

        True(
            LegacyProfileMigrationPolicy.TryValidateProfile(
                withBom,
                "Gaming.json",
                out string rejectionReason),
            $"A normal legacy profile should be accepted: {rejectionReason}");
    }

    private static void MalformedLegacyProfilesAreRejected()
    {
        string[] invalidProfiles =
        [
            "not json",
            "{\"Name\":\"Gaming\",\"Config\":[],\"MultiConfig\":[] /* comment */}",
            "{\"Name\":\"DifferentName\",\"Config\":[],\"MultiConfig\":[]}",
            "{\"Name\":\"Gaming\",\"Config\":[],\"MultiConfig\":[],\"Unknown\":true}",
            "{\"Name\":\"Gaming\",\"Config\":[\"Animations\",\"Animations\"],\"MultiConfig\":[]}",
            "{\"Name\":\"Gaming\",\"Config\":[],\"MultiConfig\":[{\"Key\":\"Mitigations\"}]}"
        ];

        foreach (string json in invalidProfiles)
        {
            True(
                !LegacyProfileMigrationPolicy.TryValidateProfile(
                    Encoding.UTF8.GetBytes(json),
                    "Gaming.json",
                    out _),
                $"Invalid legacy profile JSON should be rejected: {json}");
        }

        True(
            !LegacyProfileMigrationPolicy.TryValidateProfile(
                Encoding.UTF8.GetBytes("{\"Name\":\"Gaming\",\"Config\":[],\"MultiConfig\":[]}"),
                "Gaming.txt",
                out _),
            "A profile with a non-JSON filename must be rejected.");
    }

    private static void OversizedLegacyProfileIsRejected()
    {
        byte[] oversized = new byte[LegacyProfileMigrationPolicy.MaximumProfileBytes + 1];
        True(
            !LegacyProfileMigrationPolicy.TryValidateProfile(
                oversized,
                "Gaming.json",
                out _),
            "A profile larger than the migration limit must be rejected before parsing.");
    }

    private static void NvidiaProfileExportRoundTrips()
    {
        List<NvidiaProfile> expected =
        [
            new NvidiaProfile
            {
                ProfileName = "Base Profile",
                Settings =
                [
                    new NvidiaProfileSetting
                    {
                        SettingNameInfo = "Power management mode",
                        SettingId = 274197361,
                        SettingValue = "1",
                        ValueType = NvidiaSettingValueType.Dword,
                    },
                ],
            },
            new NvidiaProfile
            {
                ProfileName = "Game & Launcher",
                Executeables = ["game.exe", "launcher.exe"],
                Settings =
                [
                    new NvidiaProfileSetting
                    {
                        SettingNameInfo = "Application note",
                        SettingId = 550564838,
                        SettingValue = "GPU <preferred>",
                        ValueType = NvidiaSettingValueType.String,
                    },
                ],
            },
        ];

        string exportPath = Path.Combine(Path.GetTempPath(), "SynToolkit-NipTests-" + Guid.NewGuid().ToString("N") + ".nip");
        try
        {
            NvidiaProfilePreviewService.SaveProfiles(expected, exportPath);
            List<NvidiaProfile> actual = NvidiaProfilePreviewService.LoadProfiles(exportPath);

            Equal(2, actual.Count, "Every loaded profile must be exported.");
            Equal("Base Profile", actual[0].ProfileName, "The base profile name must be preserved.");
            Equal(1, actual[0].Settings.Count, "Base-profile settings must be preserved.");
            Equal(274197361U, actual[0].Settings[0].SettingId, "Setting IDs must be preserved.");
            Equal("1", actual[0].Settings[0].SettingValue, "Setting values must be preserved.");
            Equal(2, actual[1].Executeables.Count, "Executable associations must be preserved.");
            Equal("GPU <preferred>", actual[1].Settings[0].SettingValue, "Escaped string values must round-trip.");
        }
        finally
        {
            if (File.Exists(exportPath))
            {
                File.Delete(exportPath);
            }
        }
    }

    private static void GpuVendorClassificationMatchesPciIdsAndNames()
    {
        Equal(
            GpuVendor.Nvidia,
            GpuVendorClassification.GetVendor("NVIDIA GeForce RTX 4070", @"PCI\VEN_10DE&DEV_2786"),
            "NVIDIA PCI vendor IDs must classify as NVIDIA.");
        Equal(
            GpuVendor.Nvidia,
            GpuVendorClassification.GetVendor("NVIDIA GeForce RTX 4070", string.Empty),
            "NVIDIA adapter names must classify as NVIDIA when the PCI ID is missing.");
        Equal(
            GpuVendor.Amd,
            GpuVendorClassification.GetVendor("AMD Radeon RX 7800 XT", @"PCI\VEN_1002&DEV_7480"),
            "AMD PCI vendor IDs must classify as AMD.");
        Equal(
            GpuVendor.Amd,
            GpuVendorClassification.GetVendor("Radeon RX 7800 XT", string.Empty),
            "Radeon adapter names must classify as AMD when the PCI ID is missing.");
        Equal(
            GpuVendor.Intel,
            GpuVendorClassification.GetVendor("Intel(R) UHD Graphics", @"PCI\VEN_8086&DEV_46A6"),
            "Intel PCI vendor IDs must classify as Intel.");
        Equal(
            GpuVendor.Unknown,
            GpuVendorClassification.GetVendor("Microsoft Basic Display Adapter", @"PCI\VEN_1414"),
            "The Microsoft Basic Display Adapter must not be treated as a real GPU vendor.");
        Equal(
            GpuVendor.Unknown,
            GpuVendorClassification.GetVendor("Unknown GPU", string.Empty),
            "Unrecognized adapters must stay Unknown.");
    }

    private static void GpuTabIconUsesVendorBrandsAndSafeFallback()
    {
        Equal(
            GpuVendorClassification.NvidiaGpuIconPath,
            GpuVendorClassification.GetGpuTabIconPath(GpuVendor.Nvidia),
            "NVIDIA systems must use the existing NVIDIA brand icon.");
        Equal(
            GpuVendorClassification.AmdGpuIconPath,
            GpuVendorClassification.GetGpuTabIconPath(GpuVendor.Amd),
            "AMD systems must use the existing AMD brand icon.");
        Equal(
            GpuVendorClassification.DefaultGpuIconPath,
            GpuVendorClassification.GetGpuTabIconPath(GpuVendor.Intel),
            "Intel systems must keep the generic GPU tab icon.");
        Equal(
            GpuVendorClassification.DefaultGpuIconPath,
            GpuVendorClassification.GetGpuTabIconPath(GpuVendor.Unknown),
            "Failed or unknown detection must keep the generic GPU tab icon.");
        Equal(
            GpuVendorClassification.IntelGpuIconPath,
            GpuVendorClassification.GetIconPath(GpuVendor.Intel),
            "The Specs list can still show the Intel logo per GPU; only the tab icon stays generic.");
    }

    private static void PrimaryGpuVendorPrefersDiscreteGpu()
    {
        Equal(
            GpuVendor.Nvidia,
            GpuVendorClassification.GetPrimaryGpuVendor(
            [
                ("Intel(R) UHD Graphics", GpuVendor.Intel),
                ("NVIDIA GeForce RTX 4070", GpuVendor.Nvidia),
            ]),
            "A laptop with Intel iGPU + NVIDIA dGPU must prefer NVIDIA, matching the Specs header.");
        Equal(
            GpuVendor.Amd,
            GpuVendorClassification.GetPrimaryGpuVendor(
            [
                ("Intel(R) Iris Xe Graphics", GpuVendor.Intel),
                ("AMD Radeon RX 7600M", GpuVendor.Amd),
            ]),
            "A laptop with Intel iGPU + AMD dGPU must prefer AMD, matching the Specs header.");
        Equal(
            GpuVendor.Nvidia,
            GpuVendorClassification.GetPrimaryGpuVendor(
            [
                ("AMD Radeon Graphics", GpuVendor.Amd),
                ("NVIDIA GeForce RTX 4070", GpuVendor.Nvidia),
            ]),
            "NVIDIA must win over AMD when both are present, matching the Specs header.");
        Equal(
            GpuVendor.Intel,
            GpuVendorClassification.GetPrimaryGpuVendor(
            [
                ("Microsoft Basic Display Adapter", GpuVendor.Unknown),
                ("Intel(R) UHD Graphics", GpuVendor.Intel),
            ]),
            "Intel-only systems stay Intel after Basic Display adapters are ignored.");
        Equal(
            GpuVendor.Unknown,
            GpuVendorClassification.GetPrimaryGpuVendor([]),
            "An empty adapter list must stay Unknown so the tab icon can fail safe.");
    }

    private static void HagsClassificationDistinguishesStates()
    {
        const int windows2004 = HagsDetection.MinimumWindowsBuild;
        const int windows11 = 26100;
        const int windows1909 = 18363;

        Equal(
            HagsSupportState.NotSupportedByWindowsVersion,
            HagsDetection.Classify(windows1909, null),
            "A missing HwSchMode on Windows 10 before 2004 is an OS limitation.");
        Equal(
            HagsSupportState.NotSupportedByWindowsVersion,
            HagsDetection.Classify(windows1909, 2),
            "HAGS is still an OS limitation below build 19041 even if a DWORD exists.");
        Equal(
            HagsSupportState.NotSupportedByGpuOrDriver,
            HagsDetection.Classify(windows2004, null),
            "A missing HwSchMode on Windows 10 2004+ means the GPU/driver did not register support.");
        Equal(
            HagsSupportState.NotSupportedByGpuOrDriver,
            HagsDetection.Classify(windows11, null),
            "A missing HwSchMode on Windows 11 means the GPU/driver did not register support.");
        Equal(
            HagsSupportState.SupportedDisabled,
            HagsDetection.Classify(windows11, 1),
            "HwSchMode=1 is supported and currently disabled, not unavailable.");
        Equal(
            HagsSupportState.SupportedEnabled,
            HagsDetection.Classify(windows11, 2),
            "HwSchMode=2 is supported and currently enabled.");
        Equal(
            HagsSupportState.Unknown,
            HagsDetection.Classify(windows11, 0),
            "HwSchMode=0 is unexpected and must not be treated as enabled or disabled.");
        Equal(
            HagsSupportState.Unknown,
            HagsDetection.Classify(windows11, 3),
            "Any other HwSchMode value is unknown rather than silently available.");
        Equal(
            HagsSupportState.Unknown,
            HagsDetection.Classify(windows11, null, registryReadFailed: true),
            "A registry-view/read failure is unknown, not a missing-key unsupported state.");

        Equal(
            "Supported — currently disabled.",
            HagsDetection.GetStatusText(HagsSupportState.SupportedDisabled),
            "Disabled-but-supported must keep a distinct status string.");
        Equal(
            "Supported — currently enabled.",
            HagsDetection.GetStatusText(HagsSupportState.SupportedEnabled),
            "Enabled must keep a distinct status string.");
        Equal(
            "Not supported by your Windows version.",
            HagsDetection.GetStatusText(HagsSupportState.NotSupportedByWindowsVersion),
            "Pre-2004 Windows must use the OS-version message.");
        Equal(
            "Not supported by your GPU/driver.",
            HagsDetection.GetStatusText(HagsSupportState.NotSupportedByGpuOrDriver),
            "A missing key on a supported OS must use the GPU/driver message.");
        Equal(
            "Unknown (HwSchMode=7).",
            HagsDetection.GetStatusText(HagsSupportState.Unknown, 7),
            "Unknown states must surface the raw DWORD for diagnostics.");
        True(
            HagsDetection.CanToggle(HagsSupportState.SupportedDisabled),
            "A present-but-off system must remain toggleable.");
        True(
            !HagsDetection.CanToggle(HagsSupportState.NotSupportedByGpuOrDriver),
            "An unsupported GPU/driver must not be treated as a toggleable off state.");
    }

    private static void NoMetadata()
    {
        PlaybookInformation result = AmePlaybookDetector.Detect(new FakeSource());
        Equal(PlaybookDetectionStatus.NotDetected, result.Status, "Empty metadata should not be guessed.");
    }

    private static void OfficialRecord()
    {
        FakeSource source = new(
            registryMarkers:
            [
                new AmePlaybookMarker(
                    "AME 11",
                    "v0.8.4",
                    @"HKLM\SOFTWARE\AME\Playbooks\Applied\{9010E718-4B54-443F-8354-D893CD50FDDE}",
                    new DateTime(2026, 2, 11, 12, 0, 0, DateTimeKind.Utc))
            ]);

        PlaybookInformation result = AmePlaybookDetector.Detect(source);
        Equal(PlaybookDetectionStatus.Detected, result.Status, "Official metadata should be detected.");
        Equal("AME 11", result.Name, "Stored Playbook name should be preserved.");
        Equal("0.8.4", result.Version, "A leading v should be normalized.");
    }

    private static void NewestRecordWins()
    {
        FakeSource source = new(
            registryMarkers:
            [
                new AmePlaybookMarker(
                    "Older Playbook",
                    "1.0.0",
                    "older",
                    new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                new AmePlaybookMarker(
                    "AME 10",
                    "2.5",
                    "newer",
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            ]);

        PlaybookInformation result = AmePlaybookDetector.Detect(source);
        Equal(PlaybookDetectionStatus.Detected, result.Status, "A timestamped record should resolve confidently.");
        Equal("AME 10", result.Name, "The latest AppliedTimeUTC record should win.");
        Equal("2.5", result.Version, "The selected record's version should be returned.");
    }

    private static void OfficialBinaryTimestamp()
    {
        DateTime expected = new(2026, 2, 11, 12, 34, 56, DateTimeKind.Utc);
        DateTime? actual = WindowsAmePlaybookMetadataSource.TryDecodeAppliedTimeUtc(expected.ToBinary());
        Equal(expected, actual, "AME stores AppliedTimeUTC using DateTime.UtcNow.ToBinary().");
    }

    private static void InvalidBinaryTimestamps()
    {
        True(
            WindowsAmePlaybookMetadataSource.TryDecodeAppliedTimeUtc("not-a-qword") is null,
            "A non-QWORD registry value must be ignored.");
        True(
            WindowsAmePlaybookMetadataSource.TryDecodeAppliedTimeUtc(long.MaxValue) is null,
            "An out-of-range DateTime binary value must be ignored.");
        True(
            WindowsAmePlaybookMetadataSource.TryDecodeAppliedTimeUtc(
                new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToBinary()) is null,
            "Implausibly old applied timestamps must be ignored.");
        DateTime now = new(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc);
        True(
            WindowsAmePlaybookMetadataSource.TryDecodeAppliedTimeUtc(
                now.AddDays(2).ToBinary(),
                now) is null,
            "Implausibly future applied timestamps must not win forever.");
    }

    private static void OverhaulPlaybookWins()
    {
        FakeSource source = new(
            registryMarkers:
            [
                new AmePlaybookMarker(
                    "SynergyOS Playbook",
                    "1.5.1",
                    "overhaul",
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    Overhaul: true,
                    ErrorLevel: 0),
                new AmePlaybookMarker(
                    "Utility Playbook",
                    "2.0",
                    "utility",
                    new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                    Overhaul: false,
                    ErrorLevel: 0)
            ]);

        PlaybookInformation result = AmePlaybookDetector.DetectRegistry(source);
        Equal("SynergyOS Playbook", result.Name, "An add-on Playbook must not replace the OS-overhaul identity.");
    }

    private static void FatalRecordIsIgnored()
    {
        FakeSource source = new(
            registryMarkers:
            [
                new AmePlaybookMarker(
                    "Working Playbook",
                    "1.0",
                    "success",
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    Overhaul: true,
                    ErrorLevel: 0),
                new AmePlaybookMarker(
                    "Failed Playbook",
                    "2.0",
                    "fatal",
                    new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                    Overhaul: true,
                    ErrorLevel: 2)
            ]);

        PlaybookInformation result = AmePlaybookDetector.DetectRegistry(source);
        Equal("Working Playbook", result.Name, "A fatal AME attempt must not replace a successfully applied Playbook.");

        PlaybookInformation fatalOnly = AmePlaybookDetector.DetectRegistry(
            new FakeSource(registryMarkers:
            [
                new AmePlaybookMarker("Failed Playbook", "2.0", "fatal", ErrorLevel: 2)
            ]));
        Equal(PlaybookDetectionStatus.NotDetected, fatalOnly.Status, "A fatal-only history must not be presented as current.");
    }

    private static void MixedTimestampConflict()
    {
        FakeSource source = new(
            registryMarkers:
            [
                new AmePlaybookMarker(
                    "AME 11",
                    "0.8.4",
                    "timestamped",
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                new AmePlaybookMarker("Privacy+", "1.0", "untimestamped")
            ]);

        PlaybookInformation result = AmePlaybookDetector.Detect(source);
        Equal(
            PlaybookDetectionStatus.Conflicting,
            result.Status,
            "A missing timestamp must not let registry order decide between different Playbooks.");
    }

    private static void TiedTimestampConflict()
    {
        DateTime tie = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        FakeSource source = new(
            registryMarkers:
            [
                new AmePlaybookMarker("AME 11", "0.8.4", "first", tie),
                new AmePlaybookMarker("Privacy+", "1.0", "second", tie)
            ]);

        PlaybookInformation result = AmePlaybookDetector.Detect(source);
        Equal(
            PlaybookDetectionStatus.Conflicting,
            result.Status,
            "Equal timestamps for different Playbooks must stay explicit.");
    }

    private static void RegistryPrecedesLegacy()
    {
        FakeSource source = new(
            registryMarkers:
            [
                new AmePlaybookMarker("Privacy+", "0.8", "registry")
            ],
            legacyMarker: new AmePlaybookMarker("Legacy AME", "0.7", "legacy"));

        PlaybookInformation result = AmePlaybookDetector.Detect(source);
        Equal("Privacy+", result.Name, "Current registry metadata must precede legacy history.");
    }

    private static void UntimestampedConflict()
    {
        FakeSource source = new(
            registryMarkers:
            [
                new AmePlaybookMarker("AME 10", "2.5", "first"),
                new AmePlaybookMarker("Privacy+", "0.8", "second")
            ]);

        PlaybookInformation result = AmePlaybookDetector.Detect(source);
        Equal(PlaybookDetectionStatus.Conflicting, result.Status, "Registry order must not be guessed without timestamps.");
    }

    private static void MalformedEntryIsolated()
    {
        FakeSource source = new(
            registryMarkers:
            [
                new AmePlaybookMarker("https://invalid.example/playbook", "1.0", "invalid"),
                new AmePlaybookMarker("AME 11", "0.8.4", "valid")
            ]);

        PlaybookInformation result = AmePlaybookDetector.Detect(source);
        Equal(PlaybookDetectionStatus.Detected, result.Status, "One corrupt record must not hide valid metadata.");
        Equal("AME 11", result.Name, "The valid record should survive isolation.");
    }

    private static void ValidLegacyConfiguration()
    {
        WithTemporaryConfiguration(
            "<Playbook><Name>AME 10</Name><Version>v2.5</Version></Playbook>",
            path =>
            {
                bool parsed = WindowsAmePlaybookMetadataSource.TryReadPlaybookConfiguration(path, out AmePlaybookMarker? marker);
                True(parsed, "A normal AME playbook.conf should parse.");
                Equal("AME 10", marker?.Name, "Legacy name should be read from XML.");
                Equal("v2.5", marker?.Version, "Legacy version should be read from XML before normalization.");
            });
    }

    private static void ExternalEntityIsProhibited()
    {
        const string xml = "<!DOCTYPE Playbook [<!ENTITY xxe SYSTEM 'file:///C:/Windows/win.ini'>]>"
            + "<Playbook><Name>&xxe;</Name><Version>1.0</Version></Playbook>";
        WithTemporaryConfiguration(
            xml,
            path => True(
                !WindowsAmePlaybookMetadataSource.TryReadPlaybookConfiguration(path, out _),
                "DTD/external-entity metadata must be rejected."));
    }

    private static void LatestLegacyDirectoryWins()
    {
        WithTemporaryLegacyRoot(
            root =>
            {
                WriteLegacyConfiguration(root, "1", "Older AME", "1.0");
                WriteLegacyConfiguration(root, "2", "Newer AME", "2.0");

                WindowsAmePlaybookMetadataSource source = new(root);
                AmePlaybookMarker? marker = source.ReadLatestLegacyMarker();
                Equal("Newer AME", marker?.Name, "The largest numeric AME history folder should win.");
            });
    }

    private static void InvalidLegacyDirectoryFallsBack()
    {
        WithTemporaryLegacyRoot(
            root =>
            {
                WriteLegacyConfiguration(root, "2", "Usable AME", "2.0");
                string invalidDirectory = Path.Combine(root, "3");
                Directory.CreateDirectory(invalidDirectory);
                File.WriteAllText(
                    Path.Combine(invalidDirectory, "playbook.conf"),
                    "<Playbook><Name>https://invalid.example/playbook</Name></Playbook>",
                    new UTF8Encoding(false));

                WindowsAmePlaybookMetadataSource source = new(root);
                AmePlaybookMarker? marker = source.ReadLatestLegacyMarker();
                Equal("Usable AME", marker?.Name, "One malformed history entry must not hide an older valid entry.");
            });
    }

    private static void EmptyNormalizedVersionIsOmitted()
    {
        PlaybookInformation? result = AmePlaybookDetector.TryNormalize(
            new AmePlaybookMarker("AME 11", "v", "test"));
        Equal<string?>(null, result?.Version, "A bare version prefix must not be displayed as an empty version.");
    }

    private static void InvalidVersionsAreOmitted()
    {
        foreach (string version in new[] { "vvvvjunk", "1.2.3.4", "https://invalid.example/version" })
        {
            PlaybookInformation? result = AmePlaybookDetector.TryNormalize(
                new AmePlaybookMarker("AME 11", version, "test"));
            Equal<string?>(null, result?.Version, $"Invalid AME version '{version}' must not be displayed.");
        }
    }

    private static void SameNameVersionConflict()
    {
        PlaybookInformation result = AmePlaybookDetector.ResolveMarkers(
        [
            new PlaybookInformation(PlaybookDetectionStatus.Detected, "AME 11", "0.8.3", "first"),
            new PlaybookInformation(PlaybookDetectionStatus.Detected, "AME 11", "0.8.4", "second")
        ]);
        Equal(
            PlaybookDetectionStatus.Conflicting,
            result.Status,
            "The same Playbook name with different versions must not be selected by insertion order.");
    }

    private static void OversizedConfigurationIsRejected()
    {
        string oversized = "<Playbook><Name>" + new string('A', checked((int)WindowsAmePlaybookMetadataSource.MaximumConfigurationBytes))
            + "</Name><Version>1.0</Version></Playbook>";
        WithTemporaryConfiguration(
            oversized,
            path => True(
                !WindowsAmePlaybookMetadataSource.TryReadPlaybookConfiguration(path, out _),
                "Oversized metadata must be rejected before XML parsing."));
    }

    private static void WithTemporaryConfiguration(string contents, Action<string> assertion)
    {
        string directory = Path.Combine(Path.GetTempPath(), "SynToolkit-AmeTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "playbook.conf");
        try
        {
            File.WriteAllText(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            assertion(path);
        }
        finally
        {
            string resolvedDirectory = Path.GetFullPath(directory);
            string temporaryRoot = Path.GetFullPath(Path.GetTempPath());
            if (resolvedDirectory.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(resolvedDirectory))
            {
                Directory.Delete(resolvedDirectory, recursive: true);
            }
        }
    }

    private static void WithTemporaryLegacyRoot(Action<string> assertion)
    {
        string directory = Path.Combine(Path.GetTempPath(), "SynToolkit-AmeLegacyTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            assertion(directory);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static void WriteLegacyConfiguration(string root, string folder, string name, string version)
    {
        string directory = Path.Combine(root, folder);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "playbook.conf"),
            $"<Playbook><Name>{name}</Name><Version>{version}</Version></Playbook>",
            new UTF8Encoding(false));
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        string resolvedDirectory = Path.GetFullPath(directory);
        string temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        if (resolvedDirectory.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(resolvedDirectory))
        {
            Directory.Delete(resolvedDirectory, recursive: true);
        }
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"PASS: {name}");
        }
        catch (Exception exception)
        {
            _failures++;
            Console.Error.WriteLine($"FAIL: {name}: {exception.Message}");
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected '{expected}', got '{actual}'.");
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Throws<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private sealed class FakeBcdProvider : IBcdWmiProvider
    {
        private readonly Dictionary<(string ObjectId, uint ElementType), object> _values = new();

        internal bool RetainWrites { get; set; } = true;
        internal int DeleteCalls { get; private set; }

        public object GetElementValue(string objectId, uint elementType) =>
            _values.TryGetValue((objectId, elementType), out object? value) ? value : null!;

        public void DeleteElement(string objectId, uint elementType)
        {
            DeleteCalls++;
            if (RetainWrites)
            {
                _values.Remove((objectId, elementType));
            }
        }

        public void SetBooleanElement(string objectId, uint elementType, bool value)
        {
            if (RetainWrites)
            {
                _values[(objectId, elementType)] = value;
            }
        }

        public void SetIntegerElement(string objectId, uint elementType, ulong value)
        {
            if (RetainWrites)
            {
                _values[(objectId, elementType)] = value;
            }
        }
    }

    private sealed class GatedAsyncCommand : AsyncCommandBase
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _executionCount;

        internal int ExecutionCount => Volatile.Read(ref _executionCount);

        internal bool WaitUntilStarted() => _started.Task.Wait(TimeSpan.FromSeconds(2));

        internal void Release() => _release.TrySetResult();

        protected override async Task ExecuteAsync(object? parameter)
        {
            Interlocked.Increment(ref _executionCount);
            _started.TrySetResult();
            await _release.Task.ConfigureAwait(false);
        }
    }

    private sealed class FailingAsyncCommand : AsyncCommandBase
    {
        protected override Task ExecuteAsync(object? parameter) =>
            Task.FromException(new InvalidOperationException("Expected test failure."));
    }

    private sealed class FakeSource : IAmePlaybookMetadataSource
    {
        private readonly IReadOnlyCollection<AmePlaybookMarker> _registryMarkers;
        private readonly AmePlaybookMarker? _legacyMarker;

        internal FakeSource(
            IReadOnlyCollection<AmePlaybookMarker>? registryMarkers = null,
            AmePlaybookMarker? legacyMarker = null)
        {
            _registryMarkers = registryMarkers ?? Array.Empty<AmePlaybookMarker>();
            _legacyMarker = legacyMarker;
        }

        public IReadOnlyCollection<AmePlaybookMarker> ReadRegistryMarkers() => _registryMarkers;

        public AmePlaybookMarker? ReadLatestLegacyMarker() => _legacyMarker;
    }
}
