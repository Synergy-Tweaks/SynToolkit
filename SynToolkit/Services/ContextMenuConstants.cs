namespace SynToolkit.Services;

/// <summary>
/// Constants for the Explorer context menu registration feature.
/// Used by <see cref="ContextMenuService"/> and unit tests.
/// </summary>
internal static class ContextMenuConstants
{
    /// <summary>
    /// Registry path for the SynToolkit shell verb under Directory\Background.
    /// HKCU is used so no admin elevation is required.
    /// </summary>
    public const string ShellKeyPath = @"HKCU\Software\Classes\Directory\Background\shell\SynToolkit";

    /// <summary>
    /// Registry path for the command subkey that holds the executable invocation.
    /// </summary>
    public const string CommandKeyPath = ShellKeyPath + @"\command";

    /// <summary>
    /// The label displayed in the context menu.
    /// </summary>
    public const string LabelValue = "Open SynToolkit";

    /// <summary>
    /// Registry value name for the icon path on the shell key.
    /// </summary>
    public const string IconValueName = "Icon";
}
