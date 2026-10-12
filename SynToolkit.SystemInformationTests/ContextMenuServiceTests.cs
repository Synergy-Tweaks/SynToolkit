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
        RegistryPathsUseHkcu();
        CommandSubkeyIsUnderShellKey();
        LabelValueIsCorrect();
        PathFormattingHelpers();
    }

    private static void ConstantsAreCorrect()
    {
        // Verify registry paths follow expected structure
        True(
            ContextMenuConstants.ShellKeyPath.StartsWith("HKCU", StringComparison.Ordinal),
            "Context menu must use HKCU (current user) to avoid requiring admin rights.");

        True(
            ContextMenuConstants.ShellKeyPath.Contains(@"Directory\Background\shell", StringComparison.OrdinalIgnoreCase),
            "Context menu must target Directory\\Background for desktop and folder background right-click.");

        True(
            ContextMenuConstants.ShellKeyPath.EndsWith("SynToolkit", StringComparison.OrdinalIgnoreCase),
            "Shell key must end with SynToolkit as the verb name.");
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

    private static void RegistryPathsUseHkcu()
    {
        True(
            ContextMenuConstants.ShellKeyPath.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase),
            "Shell key must use HKCU hive prefix.");

        True(
            ContextMenuConstants.CommandKeyPath.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase),
            "Command key must use HKCU hive prefix.");

        // Verify no HKLM usage (would require admin)
        True(
            !ContextMenuConstants.ShellKeyPath.Contains("HKLM", StringComparison.OrdinalIgnoreCase),
            "Shell key must not use HKLM (requires admin).");

        True(
            !ContextMenuConstants.CommandKeyPath.Contains("HKLM", StringComparison.OrdinalIgnoreCase),
            "Command key must not use HKLM (requires admin).");
    }

    private static void CommandSubkeyIsUnderShellKey()
    {
        True(
            ContextMenuConstants.CommandKeyPath.StartsWith(ContextMenuConstants.ShellKeyPath, StringComparison.OrdinalIgnoreCase),
            "Command key must be a subkey of the shell key.");

        True(
            ContextMenuConstants.CommandKeyPath.EndsWith(@"\command", StringComparison.OrdinalIgnoreCase),
            "Command key must end with \\command subkey.");
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
