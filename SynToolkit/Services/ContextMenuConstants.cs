namespace SynToolkit.Services;

/// <summary>
/// Constants for the Explorer context menu registration feature.
/// Used by <see cref="ContextMenuService"/> and unit tests.
/// </summary>
internal static class ContextMenuConstants
{
    /// <summary>
    /// Verb name shared by all three shell locations.
    /// </summary>
    public const string VerbName = "SynToolkit";

    /// <summary>
    /// Machine-wide shell locations: real desktop, folder icon, folder background.
    /// HKLM matches every other context-menu toggle in this codebase and the
    /// app already runs elevated, so there is no benefit to HKCU here.
    /// </summary>
    public static readonly string[] ShellKeyPaths =
    [
        @"HKLM\SOFTWARE\Classes\DesktopBackground\shell\SynToolkit",
        @"HKLM\SOFTWARE\Classes\Directory\shell\SynToolkit",
        @"HKLM\SOFTWARE\Classes\Directory\Background\shell\SynToolkit",
    ];

    /// <summary>
    /// Leftover v1 key (HKCU, folder-background only). Removed on enable/disable.
    /// </summary>
    public const string LegacyShellKeyPath = @"HKCU\Software\Classes\Directory\Background\shell\SynToolkit";

    /// <summary>
    /// The label displayed in the context menu.
    /// </summary>
    public const string LabelValue = "Open SynToolkit";

    /// <summary>
    /// Registry value name for the icon path on the shell key.
    /// </summary>
    public const string IconValueName = "Icon";

    /// <summary>
    /// Ownership tag so Disable never removes a foreign application's verb.
    /// </summary>
    public const string OwnerValueName = "SynToolkitOwner";

    /// <summary>
    /// Expected ownership tag value.
    /// </summary>
    public const string OwnerValue = "SynToolkit";
}
