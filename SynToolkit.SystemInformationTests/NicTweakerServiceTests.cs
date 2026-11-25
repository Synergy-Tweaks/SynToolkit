using SynToolkit.Services.NicTweaker;
using System.Text.Json;

namespace SynToolkit.SystemInformationTests;

internal static class NicTweakerServiceTests
{
    public static void RunAll()
    {
        PresetParsingReadsValues();
        ConfigParsingRejectsWrongFormat();
        SnapshotRoundTripKeepsDeleteSemantics();
    }

    private static void PresetParsingReadsValues()
    {
        const string json = """
            {
              "name": "Intel",
              "description": "Preset",
              "values": {
                "pow.eee": "Disabled",
                "adv.intmod": "0 - Disabled"
              }
            }
            """;

        NicPreset preset = NicTweakerService.ParsePresetJson(json);
        if (!string.Equals(preset.Name, "Intel", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Preset name parsing failed.");
        }

        if (!preset.Values.TryGetValue("pow.eee", out string? value) || value != "Disabled")
        {
            throw new InvalidOperationException("Preset values parsing failed.");
        }
    }

    private static void ConfigParsingRejectsWrongFormat()
    {
        const string bad = """{"format":"evil","schemaVersion":1,"tweaks":[]}""";
        bool threw = false;
        try
        {
            _ = NicTweakerService.ParseConfigJson(bad);
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        if (!threw)
        {
            throw new InvalidOperationException("Wrong config format should throw.");
        }
    }

    private static void SnapshotRoundTripKeepsDeleteSemantics()
    {
        NicSnapshot snapshot = new()
        {
            Id = "test",
            CreatedUtc = DateTimeOffset.UtcNow,
            Label = "snapshot",
            Entries =
            [
                new NicSnapshotEntry
                {
                    TweakId = "pow.eee",
                    Path = @"HKLM\Software\Test",
                    ValueName = "Value",
                    DidNotExist = true,
                    PreviousValue = null,
                    IsDword = true
                }
            ]
        };

        string json = JsonSerializer.Serialize(snapshot);
        NicSnapshot? parsed = JsonSerializer.Deserialize<NicSnapshot>(json);
        if (parsed is null || parsed.Entries.Count != 1 || !parsed.Entries[0].DidNotExist)
        {
            throw new InvalidOperationException("Snapshot round-trip failed.");
        }
    }
}
