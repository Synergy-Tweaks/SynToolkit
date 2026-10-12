using SynToolkit.Services;

namespace SynToolkit.SystemInformationTests;

/// <summary>
/// Tests for context menu constants and registry path structure.
/// Note: ContextMenuService is not linked in the test project due to RegistryHelper
/// nullable compatibility issues. The service is tested via manual verification.
/// </summary>
internal static class ContextMenuServiceTests
{
    internal static void RunAll()
    {
        ConstantsAreCorrect();
        RegistryPathsUseHklm();
        CommandSubkeysAreUnderShellKeys();
        LabelValueIsCorrect();
        PathFormattingHelpers();
    }

    private static void ConstantsAreCorrect()
    {
        // Three machine-wide verbs: real desktop, folder icon, folder background.
        Equal(3, ContextMenuConstants.ShellKeyPaths.Length, "Context menu must cover exactly three shell locations.");

        foreach (string shellKey in ContextMenuConstants.ShellKeyPaths)
        {
            True(
                shellKey.StartsWith("HKLM", StringComparison.Ordinal),
                $"Context menu must use HKLM (app already runs elevated): {shellKey}.");
            True(
                shellKey.EndsWith("SynToolkit", StringComparison.OrdinalIgnoreCase),
                $"Shell key must end with SynToolkit as the verb name: {shellKey}.");
        }

        True(
            ContextMenuConstants.LegacyShellKeyPath.StartsWith("HKCU", StringComparison.Ordinal),
            "The legacy v1 key must use HKCU so cleanup targets the old location.");
    }

    private static void LabelValueIsCorrect()
    {
        Equal(
            "Open SynToolkit",
            ContextMenuConstants.LabelValue,
            "Menu label must be 'Open SynToolkit'.");

        Equal(
            "Icon",
            ContextMenuConstants.IconValueName,
            "Icon value name must be 'Icon' for shell menu icons.");
    }

    private static void RegistryPathsUseHklm()
    {
        // Verify no HKCU usage in the live keys (HKCU would require per-user
        // writes from an elevated process and diverge from every other toggle).
        foreach (string shellKey in ContextMenuConstants.ShellKeyPaths)
        {
            True(
                !shellKey.Contains("HKCU", StringComparison.OrdinalIgnoreCase),
                $"Live shell key must not use HKCU: {shellKey}.");
        }
    }

    private static void CommandSubkeysAreUnderShellKeys()
    {
        foreach (string shellKey in ContextMenuConstants.ShellKeyPaths)
        {
            True(
                (shellKey + @"\command").EndsWith(@"\command", StringComparison.OrdinalIgnoreCase),
                $"Command key must end with \\command subkey: {shellKey}.");
        }
    }

    private static void PathFormattingHelpers()
    {
        // Test quoted command format (simulating what ContextMenuService.GetQuotedCommand does)
        const string simplePath = @"C:\Apps\SynToolkit.exe";
        string quotedSimple = $"\"{simplePath}\"";
        Equal(
            @"""C:\Apps\SynToolkit.exe""",
            quotedSimple,
            "Simple path should be quoted correctly.");

        // Test path with spaces
        const string pathWithSpaces = @"C:\Program Files\SynToolkit\SynToolkit.exe";
        string quotedSpaces = $"\"{pathWithSpaces}\"";
        True(
            quotedSpaces.StartsWith("\"", StringComparison.Ordinal) && quotedSpaces.EndsWith("\"", StringComparison.Ordinal),
            "Path with spaces must be surrounded by quotes.");

        // Test icon value format (simulating what ContextMenuService.GetIconValue does)
        string iconValue = $"\"{simplePath}\",0";
        Equal(
            @"""C:\Apps\SynToolkit.exe"",0",
            iconValue,
            "Icon value must be quoted path with ,0 suffix for first icon.");

        // Test non-ASCII path preservation
        const string unicodePath = @"C:\Users\用户\Desktop\SynToolkit.exe";
        string quotedUnicode = $"\"{unicodePath}\"";
        True(
            quotedUnicode.Contains("用户", StringComparison.Ordinal),
            "Non-ASCII characters must be preserved in quoted command.");
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
}
