using System;
using System.IO;
using System.Security;
using SynToolkit.Utils;

namespace SynToolkit.Services;

/// <summary>
/// State of the SynToolkit context menu registration.
/// </summary>
public enum ContextMenuState
{
    /// <summary>All shell entries are registered and point to the current executable.</summary>
    On,

    /// <summary>No shell entry is registered.</summary>
    Off,

    /// <summary>
    /// Entries exist but are out of date (stale path, partial set, legacy v1 key, etc.).
    /// Enabling rewrites them cleanly, disabling removes them.
    /// </summary>
    Modified,

    /// <summary>
    /// Detection failed due to access denied, security exception, or other unexpected error.
    /// No write should be attempted in this state.
    /// </summary>
    Error
}

/// <summary>
/// Result of detecting the current context menu registration state.
/// </summary>
/// <param name="State">The overall state of the context menu entry.</param>
/// <param name="LegacyPresent">Whether any key exists (even if incomplete or stale).</param>
public readonly record struct ContextMenuDetectionResult(ContextMenuState State, bool LegacyPresent);

/// <summary>
/// Result of an enable or disable operation.
/// </summary>
/// <param name="Success">Whether the operation completed successfully.</param>
/// <param name="FailedStep">Description of the failed step if <see cref="Success"/> is false.</param>
/// <param name="DetectedState">The state detected after the operation (used to revert UI on failure).</param>
public readonly record struct ContextMenuOperationResult(bool Success, string FailedStep, ContextMenuDetectionResult DetectedState);

/// <summary>
/// Manages the SynToolkit Explorer context menu registration (opt-in toggle only).
/// Covers the real desktop, folder icons, and folder backgrounds.
/// </summary>
public static class ContextMenuService
{
    /// <summary>
    /// Gets the current executable path, quoted for use in registry commands.
    /// </summary>
    public static string GetCurrentExePath() => Environment.ProcessPath ?? string.Empty;

    /// <summary>
    /// Returns the quoted command string for the registry.
    /// </summary>
    public static string GetQuotedCommand(string exePath) => $"\"{exePath}\"";

    /// <summary>
    /// Returns the icon value (exe path with resource index).
    /// </summary>
    public static string GetIconValue(string exePath) => $"\"{exePath}\",0";

    /// <summary>
    /// Detects the current state of the context menu registration.
    /// Performs a fresh read every call with no caching.
    /// </summary>
    public static ContextMenuDetectionResult DetectCurrentState()
    {
        try
        {
            string currentExePath = GetCurrentExePath();
            if (string.IsNullOrWhiteSpace(currentExePath))
            {
                return new ContextMenuDetectionResult(ContextMenuState.Error, false);
            }

            int present = 0;
            int current = 0;
            foreach (string shellKey in ContextMenuConstants.ShellKeyPaths)
            {
                if (!RegistryHelper.KeyExists(shellKey))
                {
                    continue;
                }

                present++;
                if (IsKeyCurrent(shellKey, currentExePath))
                {
                    current++;
                }
            }

            bool legacyPresent = RegistryHelper.KeyExists(ContextMenuConstants.LegacyShellKeyPath);
            bool anyPresent = present > 0 || legacyPresent;

            if (!anyPresent)
            {
                return new ContextMenuDetectionResult(ContextMenuState.Off, false);
            }

            // shortcut: fully registered only when every HKLM verb is current and no legacy key remains.
            if (current == ContextMenuConstants.ShellKeyPaths.Length && !legacyPresent)
            {
                return new ContextMenuDetectionResult(ContextMenuState.On, true);
            }

            return new ContextMenuDetectionResult(ContextMenuState.Modified, true);
        }
        catch (UnauthorizedAccessException)
        {
            App.logger.Warn("[ContextMenu] Access denied while detecting context menu state.");
            return new ContextMenuDetectionResult(ContextMenuState.Error, false);
        }
        catch (SecurityException)
        {
            App.logger.Warn("[ContextMenu] Security exception while detecting context menu state.");
            return new ContextMenuDetectionResult(ContextMenuState.Error, false);
        }
        catch (Exception exception)
        {
            App.logger.Warn(exception, "[ContextMenu] Unexpected error while detecting context menu state.");
            return new ContextMenuDetectionResult(ContextMenuState.Error, false);
        }
    }

    /// <summary>
    /// Enables the context menu entry by writing the registry keys.
    /// </summary>
    public static ContextMenuOperationResult Enable()
    {
        try
        {
            string exePath = GetCurrentExePath();
            if (string.IsNullOrWhiteSpace(exePath))
            {
                return new ContextMenuOperationResult(false, "Could not determine executable path.", DetectCurrentState());
            }

            ContextMenuDetectionResult currentState = DetectCurrentState();
            if (currentState.State == ContextMenuState.On)
            {
                return new ContextMenuOperationResult(true, null, currentState);
            }

            if (currentState.State == ContextMenuState.Error)
            {
                return new ContextMenuOperationResult(false, "Cannot write: detection failed.", currentState);
            }

            foreach (string shellKey in ContextMenuConstants.ShellKeyPaths)
            {
                if (IsForeignKey(shellKey))
                {
                    return new ContextMenuOperationResult(false, "A context-menu key is owned by another application. SynToolkit left it unchanged.", DetectCurrentState());
                }
            }

            try
            {
                foreach (string shellKey in ContextMenuConstants.ShellKeyPaths)
                {
                    WriteShellKey(shellKey, exePath);
                }

                // Best-effort removal of the v1 HKCU key (folder-background only).
                RegistryHelper.DeleteKey(ContextMenuConstants.LegacyShellKeyPath);
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[ContextMenu] Failed to write shell keys.");
                return new ContextMenuOperationResult(false, "Failed to write shell keys.", DetectCurrentState());
            }

            ShellNotifyHelper.NotifyAssocChanged();

            ContextMenuDetectionResult verifyState = DetectCurrentState();
            if (verifyState.State != ContextMenuState.On)
            {
                App.logger.Warn("[ContextMenu] Write completed but verification failed.");
                return new ContextMenuOperationResult(false, "Write completed but verification failed.", verifyState);
            }

            App.logger.Info("[ContextMenu] Context menu entry enabled successfully.");
            return new ContextMenuOperationResult(true, null, verifyState);
        }
        catch (UnauthorizedAccessException exception)
        {
            App.logger.Warn(exception, "[ContextMenu] Access denied while enabling context menu.");
            return new ContextMenuOperationResult(false, "Access denied.", DetectCurrentState());
        }
        catch (SecurityException exception)
        {
            App.logger.Warn(exception, "[ContextMenu] Security exception while enabling context menu.");
            return new ContextMenuOperationResult(false, "Security exception.", DetectCurrentState());
        }
        catch (IOException exception)
        {
            App.logger.Warn(exception, "[ContextMenu] I/O error while enabling context menu.");
            return new ContextMenuOperationResult(false, "I/O error.", DetectCurrentState());
        }
        catch (Exception exception)
        {
            App.logger.Warn(exception, "[ContextMenu] Unexpected error while enabling context menu.");
            return new ContextMenuOperationResult(false, "Unexpected error.", DetectCurrentState());
        }
    }

    /// <summary>
    /// Disables the context menu entry by removing the registry keys.
    /// </summary>
    public static ContextMenuOperationResult Disable()
    {
        try
        {
            ContextMenuDetectionResult currentState = DetectCurrentState();
            if (currentState.State == ContextMenuState.Off)
            {
                return new ContextMenuOperationResult(true, null, currentState);
            }

            if (currentState.State == ContextMenuState.Error)
            {
                return new ContextMenuOperationResult(false, "Cannot write: detection failed.", currentState);
            }

            foreach (string shellKey in ContextMenuConstants.ShellKeyPaths)
            {
                if (IsForeignKey(shellKey))
                {
                    return new ContextMenuOperationResult(false, "A context-menu key is owned by another application. SynToolkit left it unchanged.", DetectCurrentState());
                }
            }

            try
            {
                foreach (string shellKey in ContextMenuConstants.ShellKeyPaths)
                {
                    RegistryHelper.DeleteKey(shellKey);
                }

                RegistryHelper.DeleteKey(ContextMenuConstants.LegacyShellKeyPath);
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[ContextMenu] Failed to delete shell keys.");
                return new ContextMenuOperationResult(false, "Failed to delete shell keys.", DetectCurrentState());
            }

            ShellNotifyHelper.NotifyAssocChanged();

            ContextMenuDetectionResult verifyState = DetectCurrentState();
            if (verifyState.State != ContextMenuState.Off)
            {
                App.logger.Warn("[ContextMenu] Delete completed but verification failed.");
                return new ContextMenuOperationResult(false, "Delete completed but verification failed.", verifyState);
            }

            App.logger.Info("[ContextMenu] Context menu entry disabled successfully.");
            return new ContextMenuOperationResult(true, null, verifyState);
        }
        catch (UnauthorizedAccessException exception)
        {
            App.logger.Warn(exception, "[ContextMenu] Access denied while disabling context menu.");
            return new ContextMenuOperationResult(false, "Access denied.", DetectCurrentState());
        }
        catch (SecurityException exception)
        {
            App.logger.Warn(exception, "[ContextMenu] Security exception while disabling context menu.");
            return new ContextMenuOperationResult(false, "Security exception.", DetectCurrentState());
        }
        catch (IOException exception)
        {
            App.logger.Warn(exception, "[ContextMenu] I/O error while disabling context menu.");
            return new ContextMenuOperationResult(false, "I/O error.", DetectCurrentState());
        }
        catch (Exception exception)
        {
            App.logger.Warn(exception, "[ContextMenu] Unexpected error while disabling context menu.");
            return new ContextMenuOperationResult(false, "Unexpected error.", DetectCurrentState());
        }
    }

    /// <summary>
    /// Repairs a stale context menu entry by re-enabling with the current executable path.
    /// Only call this when the state is <see cref="ContextMenuState.Modified"/>.
    /// </summary>
    public static ContextMenuOperationResult Repair() => Enable();

    private static void WriteShellKey(string shellKey, string exePath)
    {
        // Ownership first: a later interruption still leaves a tree Disable can identify as ours.
        RegistryHelper.SetValue(shellKey, ContextMenuConstants.OwnerValueName, ContextMenuConstants.OwnerValue);
        RegistryHelper.SetValue(shellKey, "MUIVerb", ContextMenuConstants.LabelValue);
        RegistryHelper.SetValue(shellKey, ContextMenuConstants.IconValueName, GetIconValue(exePath));
        RegistryHelper.SetValue(shellKey, "Position", "Bottom");
        RegistryHelper.SetValue(shellKey, "HasLUAShield", string.Empty); // app manifest requires elevation
        RegistryHelper.SetValue(shellKey + @"\command", string.Empty, GetQuotedCommand(exePath));
    }

    private static bool IsKeyCurrent(string shellKey, string exePath)
    {
        return RegistryHelper.IsMatch(shellKey, "MUIVerb", ContextMenuConstants.LabelValue)
            && RegistryHelper.IsMatch(shellKey + @"\command", string.Empty, GetQuotedCommand(exePath));
    }

    private static bool IsForeignKey(string shellKey)
    {
        if (!RegistryHelper.KeyExists(shellKey))
        {
            return false;
        }

        // Ours, or at least shaped like ours (older build without the owner tag).
        return !RegistryHelper.IsMatch(shellKey, ContextMenuConstants.OwnerValueName, ContextMenuConstants.OwnerValue)
            && !RegistryHelper.IsMatch(shellKey, "MUIVerb", ContextMenuConstants.LabelValue);
    }

    /// <summary>
    /// Normalizes a path for comparison by removing quotes and trimming whitespace.
    /// </summary>
    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.Trim().Trim('"').Trim();
    }
}
