using Microsoft.Win32;
using SynToolkit.Services.Taskbar;

namespace SynToolkit.SystemInformationTests;

internal static class TaskbarAlignmentTests
{
    public static void ConstantsAndDropdownOrder()
    {
        TaskbarAlignmentValues.EnsurePresetValuesUnique();
        Equal(1u, TaskbarAlignmentValues.TASKBAR_ALIGN_CENTERED, "Centered = 1");
        Equal(0u, TaskbarAlignmentValues.TASKBAR_ALIGN_LEFT, "Left = 0");
        Equal(2, TaskbarAlignmentValues.DropdownLabels.Count, "Exactly two items");
        Equal("Centered", TaskbarAlignmentValues.DropdownLabels[0], "Centered first");
        Equal("Left", TaskbarAlignmentValues.DropdownLabels[1], "Left second");
        True(!TaskbarAlignmentValues.DropdownLabels.Contains("Other (5)"), "Other not selectable");
        True(!TaskbarAlignmentValues.DropdownLabels.Contains("Unknown"), "Unknown not selectable");
    }

    public static void DetectionRules()
    {
        Assert(true, 1, RegistryValueKind.DWord, "Centered", TaskbarAlignmentValues.DetectionKind.Preset);
        Assert(true, 0, RegistryValueKind.DWord, "Left", TaskbarAlignmentValues.DetectionKind.Preset);
        Assert(true, null, null, "Centered", TaskbarAlignmentValues.DetectionKind.Default);

        foreach (object boxed in new object[] { 2, 255, unchecked((int)4294967295), -1 })
        {
            var result = TaskbarAlignmentValues.DetectCurrentState(true, boxed, RegistryValueKind.DWord);
            Equal(TaskbarAlignmentValues.DetectionKind.Unsupported, result.Kind, $"Unsupported {boxed}");
            True(result.DisplayLabel.StartsWith("Other", StringComparison.Ordinal), "Other label");
        }

        var sz = TaskbarAlignmentValues.DetectCurrentState(true, "1", RegistryValueKind.String);
        Equal(TaskbarAlignmentValues.DetectionKind.Unsupported, sz.Kind, "REG_SZ unsupported");

        var error = TaskbarAlignmentValues.DetectCurrentState(false, null, null);
        Equal(TaskbarAlignmentValues.DetectionKind.Error, error.Kind, "access denied → error");
        Equal("Unknown", error.DisplayLabel, "error display Unknown");
    }

    public static void WindowsVersionGate()
    {
        True(TaskbarAlignmentValues.IsWindows11OrLater(22000), "22000 is Win11");
        True(TaskbarAlignmentValues.IsWindows11OrLater(26100), "26100 is Win11");
        True(!TaskbarAlignmentValues.IsWindows11OrLater(19041), "19041 is Win10");
    }

    public static void DetectionIsFreshEachCall()
    {
        var first = TaskbarAlignmentValues.DetectCurrentState(true, 0, RegistryValueKind.DWord);
        Equal("Left", first.DisplayLabel, "first Left");
        var second = TaskbarAlignmentValues.DetectCurrentState(true, 1, RegistryValueKind.DWord);
        Equal("Centered", second.DisplayLabel, "second Centered");
    }

    private static void Assert(
        bool ok,
        object? raw,
        RegistryValueKind? kind,
        string label,
        TaskbarAlignmentValues.DetectionKind expectedKind)
    {
        var result = TaskbarAlignmentValues.DetectCurrentState(ok, raw, kind);
        Equal(expectedKind, result.Kind, $"Kind {label}");
        Equal(label, result.DisplayLabel, $"Label {label}");
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
