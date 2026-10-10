using SynToolkit.Services.Win32Priority;

namespace SynToolkit.SystemInformationTests;

internal static class Win32PrioritySeparationTests
{
    public static void PresetValuesAreUnique()
    {
        Win32PrioritySeparationValues.EnsurePresetValuesUnique();
    }

    public static void PresetMappingIncludesDefaultAndNoBoost()
    {
        Equal(2, Win32PrioritySeparationValues.DefaultWin32PrioritySeparation, "Default must be 2.");
        Equal(0, Win32PrioritySeparationValues.NoBoostValue, "No Boost must be 0.");
        Equal(2, Win32PrioritySeparationValues.TryGetPresetValue("Default"), "Default maps to 2.");
        Equal(0, Win32PrioritySeparationValues.TryGetPresetValue("No Boost"), "No Boost maps to 0.");
        Equal(21, Win32PrioritySeparationValues.TryGetPresetValue("Double Boost"), "Double Boost maps to 21.");
        Equal(22, Win32PrioritySeparationValues.TryGetPresetValue("Triple Boost"), "Triple Boost maps to 22.");
        Equal(24, Win32PrioritySeparationValues.TryGetPresetValue("Long Quantum"), "Long Quantum maps to 24.");
        Equal(40, Win32PrioritySeparationValues.TryGetPresetValue("Short Quantum"), "Short Quantum maps to 40.");
        Equal(36, Win32PrioritySeparationValues.TryGetPresetValue("Variable Quantum"), "Variable Quantum maps to 36.");
        True(Win32PrioritySeparationValues.TryGetPresetValue("Custom Value") is null, "Custom Value has no fixed decimal.");
    }

    public static void DropdownOrderMatchesSpecification()
    {
        string[] expected =
        {
            "Default",
            "No Boost",
            "Double Boost",
            "Triple Boost",
            "Long Quantum",
            "Short Quantum",
            "Variable Quantum",
            "Custom Value",
        };
        Equal(expected.Length, Win32PrioritySeparationValues.DropdownLabels.Count, "Dropdown length mismatch.");
        for (int i = 0; i < expected.Length; i++)
        {
            Equal(expected[i], Win32PrioritySeparationValues.DropdownLabels[i], $"Dropdown label {i} mismatch.");
        }
    }

    public static void DetectionRules()
    {
        AssertDetection(null, "Default", Win32PrioritySeparationValues.DetectionKind.Default, expectWarning: false);
        AssertDetection(2, "Default", Win32PrioritySeparationValues.DetectionKind.Default, expectWarning: false);
        AssertDetection(0, "No Boost", Win32PrioritySeparationValues.DetectionKind.Preset, expectWarning: false);
        AssertDetection(21, "Double Boost", Win32PrioritySeparationValues.DetectionKind.Preset, expectWarning: false);
        AssertDetection(22, "Triple Boost", Win32PrioritySeparationValues.DetectionKind.Preset, expectWarning: false);
        AssertDetection(24, "Long Quantum", Win32PrioritySeparationValues.DetectionKind.Preset, expectWarning: false);
        AssertDetection(40, "Short Quantum", Win32PrioritySeparationValues.DetectionKind.Preset, expectWarning: false);
        AssertDetection(36, "Variable Quantum", Win32PrioritySeparationValues.DetectionKind.Preset, expectWarning: false);

        // Preset beats custom for overlapping allowed values.
        AssertDetection(36, "Variable Quantum", Win32PrioritySeparationValues.DetectionKind.Preset, expectWarning: false);
        AssertDetection(40, "Short Quantum", Win32PrioritySeparationValues.DetectionKind.Preset, expectWarning: false);

        foreach (int custom in new[] { 20, 25, 26, 37, 38, 41, 42 })
        {
            var result = Win32PrioritySeparationValues.DetectCurrentState(custom);
            Equal(
                Win32PrioritySeparationValues.FormatCustomLabel(custom),
                result.PresetLabel,
                $"Custom {custom} label.");
            Equal(Win32PrioritySeparationValues.DetectionKind.Custom, result.Kind, $"Custom {custom} kind.");
            True(result.Warning is null, $"Custom {custom} must not warn.");
        }

        foreach (int unsupported in new[] { 3, 99, -1 })
        {
            var result = Win32PrioritySeparationValues.DetectCurrentState(unsupported);
            Equal(Win32PrioritySeparationValues.DetectionKind.Unsupported, result.Kind, $"Unsupported {unsupported} kind.");
            True(!string.IsNullOrWhiteSpace(result.Warning), $"Unsupported {unsupported} must warn.");
            True(result.PresetLabel.Contains(unsupported.ToString(), StringComparison.Ordinal), $"Unsupported label shows {unsupported}.");
        }

        var wrongType = Win32PrioritySeparationValues.DetectCurrentState("not-a-dword");
        Equal(Win32PrioritySeparationValues.DetectionKind.Unsupported, wrongType.Kind, "Wrong type is unsupported.");
        True(!string.IsNullOrWhiteSpace(wrongType.Warning), "Wrong type must warn without throwing.");
    }

    public static void CustomValidation()
    {
        True(Win32PrioritySeparationValues.TryValidateCustomInput("36", out int ok, out _), "36 must pass.");
        Equal(36, ok, "Parsed 36.");
        True(Win32PrioritySeparationValues.TryValidateCustomInput("20", out _, out _), "20 must pass.");

        True(!Win32PrioritySeparationValues.TryValidateCustomInput("2", out _, out string? defaultError), "2 must be rejected.");
        True(!string.IsNullOrWhiteSpace(defaultError), "2 rejection explains Default option.");

        foreach (string invalid in new[] { "", "   ", "abc", "-5", "36.5", "36,5", "99" })
        {
            True(
                !Win32PrioritySeparationValues.TryValidateCustomInput(invalid, out _, out string? error),
                $"Invalid input '{invalid}' must fail.");
            True(!string.IsNullOrWhiteSpace(error), $"Invalid input '{invalid}' must explain failure.");
        }
    }

    public static void HelperTextUsesAllowedValues()
    {
        string text = Win32PrioritySeparationValues.BuildCustomAllowedValuesHelperText();
        foreach (int value in Win32PrioritySeparationValues.CustomAllowedValues)
        {
            True(text.Contains(value.ToString(), StringComparison.Ordinal), $"Helper text must include {value}.");
        }
        True(text.StartsWith("Type a whole number for Win32PrioritySeparation.", StringComparison.Ordinal), "Helper text prefix.");
    }

    public static void PrefillUsesAllowedRawOrFallsBackTo36()
    {
        var allowed = Win32PrioritySeparationValues.DetectCurrentState(25);
        Equal(25, Win32PrioritySeparationValues.PrefillCustomDialogValue(allowed), "Allowed raw prefills.");

        var unsupported = Win32PrioritySeparationValues.DetectCurrentState(99);
        Equal(36, Win32PrioritySeparationValues.PrefillCustomDialogValue(unsupported), "Unsupported falls back to 36.");

        var missing = Win32PrioritySeparationValues.DetectCurrentState(null);
        Equal(36, Win32PrioritySeparationValues.PrefillCustomDialogValue(missing), "Missing falls back to 36.");
    }

    private static void AssertDetection(
        object? raw,
        string expectedLabel,
        Win32PrioritySeparationValues.DetectionKind expectedKind,
        bool expectWarning)
    {
        var result = Win32PrioritySeparationValues.DetectCurrentState(raw);
        Equal(expectedLabel, result.PresetLabel, $"Label for {raw ?? "null"}.");
        Equal(expectedKind, result.Kind, $"Kind for {raw ?? "null"}.");
        if (expectWarning)
        {
            True(!string.IsNullOrWhiteSpace(result.Warning), $"Warning expected for {raw ?? "null"}.");
        }
        else
        {
            True(result.Warning is null, $"No warning expected for {raw ?? "null"}.");
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception($"{message} Expected <{expected}>, actual <{actual}>.");
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
