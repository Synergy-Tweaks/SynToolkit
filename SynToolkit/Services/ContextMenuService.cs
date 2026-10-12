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
    /// <summary>The context menu entry is registered and points to the current executable.</summary>
    On,

    /// <summary>The context menu entry is not registered.</summary>
    Off,

    /// <summary>
    /// The context menu entry exists but is out of date (stale path, incomplete structure, etc.).
    /// A toggle action will rewrite or remove it cleanly.
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
/// <param name="LegacyPresent">Whether any legacy registry key exists (even if incomplete or stale).</param>
public readonly record struct ContextMenuDetectionResult(ContextMenuState State, bool LegacyPresent);

/// <summary>
/// Result of an enable or disable operation.
/// </summary>
/// <param name="Success">Whether the operation completed successfully.</param>
/// <param name="FailedStep">Description of the failed step if <see cref="Success"/> is false.</param>
/// <param name="DetectedState">The state detected after the operation (used to revert UI on failure).</param>
public readonly record struct ContextMenuOperationResult(bool Success, string FailedStep, ContextMenuDetectionResult DetectedState);

/// <summary>
/// Manages the SynToolkit Explorer context menu registration.
/// Supports Desktop and folder background right-click via Directory\Background.
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
                // Cannot determine current exe path - treat as error
                return new ContextMenuDetectionResult(ContextMenuState.Error, false);
            }

            // Check if the shell key exists at all
            bool shellKeyExists = RegistryHelper.KeyExists(ContextMenuConstants.ShellKeyPath);
            if (!shellKeyExists)
            {
                return new ContextMenuDetectionResult(ContextMenuState.Off, false);
            }

            // Shell key exists - check the command subkey
            bool commandKeyExists = RegistryHelper.KeyExists(ContextMenuConstants.CommandKeyPath);
            if (!commandKeyExists)
            {
                // Parent exists but command is missing - incomplete/modified
                return new ContextMenuDetectionResult(ContextMenuState.Modified, true);
            }

            // Read the command value
            if (!RegistryHelper.TryReadValue(ContextMenuConstants.CommandKeyPath, string.Empty, out object commandValue))
            {
                // Read failed (access denied, etc.)
                return new ContextMenuDetectionResult(ContextMenuState.Error, true);
            }

            if (commandValue is not string commandString || string.IsNullOrWhiteSpace(commandString))
            {
                // Command value is missing or wrong type
                return new ContextMenuDetectionResult(ContextMenuState.Modified, true);
            }

            // Compare paths (case-insensitive, handle quotes)
            string expectedCommand = GetQuotedCommand(currentExePath);
            string normalizedCommand = NormalizePath(commandString);
            string normalizedExpected = NormalizePath(expectedCommand);

            if (string.Equals(normalizedCommand, normalizedExpected, StringComparison.OrdinalIgnoreCase))
            {
                return new ContextMenuDetectionResult(ContextMenuState.On, true);
            }

            // Path doesn't match - stale/moved
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
                ContextMenuDetectionResult errorState = DetectCurrentState();
                return new ContextMenuOperationResult(false, "Could not determine executable path.", errorState);
            }

            // Check if already enabled with correct path
            ContextMenuDetectionResult currentState = DetectCurrentState();
            if (currentState.State == ContextMenuState.On)
            {
                // Already enabled - no write needed
                return new ContextMenuOperationResult(true, null, currentState);
            }

            if (currentState.State == ContextMenuState.Error)
            {
                return new ContextMenuOperationResult(false, "Cannot write: detection failed.", currentState);
            }

            // Write the shell key with label and icon
            try
            {
                RegistryHelper.SetValue(ContextMenuConstants.ShellKeyPath, string.Empty, ContextMenuConstants.LabelValue);
                RegistryHelper.SetValue(ContextMenuConstants.ShellKeyPath, ContextMenuConstants.IconValueName, GetIconValue(exePath));
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[ContextMenu] Failed to write shell key.");
                ContextMenuDetectionResult failState = DetectCurrentState();
                return new ContextMenuOperationResult(false, "Failed to write shell key.", failState);
            }

            // Write the command subkey
            try
            {
                RegistryHelper.SetValue(ContextMenuConstants.CommandKeyPath, string.Empty, GetQuotedCommand(exePath));
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[ContextMenu] Failed to write command key.");
                ContextMenuDetectionResult failState = DetectCurrentState();
                return new ContextMenuOperationResult(false, "Failed to write command key.", failState);
            }

            // Notify shell of changes
            ShellNotifyHelper.NotifyAssocChanged();

            // Read back and verify
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
            ContextMenuDetectionResult errorState = DetectCurrentState();
            return new ContextMenuOperationResult(false, "Access denied.", errorState);
        }
        catch (SecurityException exception)
        {
            App.logger.Warn(exception, "[ContextMenu] Security exception while enabling context menu.");
            ContextMenuDetectionResult errorState = DetectCurrentState();
            return new ContextMenuOperationResult(false, "Security exception.", errorState);
        }
        catch (IOException exception)
        {
            App.logger.Warn(exception, "[ContextMenu] I/O error while enabling context menu.");
            ContextMenuDetectionResult errorState = DetectCurrentState();
            return new ContextMenuOperationResult(false, "I/O error.", errorState);
        }
        catch (Exception exception)
        {
            App.logger.Warn(exception, "[ContextMenu] Unexpected error while enabling context menu.");
            ContextMenuDetectionResult errorState = DetectCurrentState();
            return new ContextMenuOperationResult(false, "Unexpected error.", errorState);
        }
    }

    /// <summary>
    /// Disables the context menu entry by removing the registry keys.
    /// </summary>
    public static ContextMenuOperationResult Disable()
    {
        try
        {
            // Check current state
            ContextMenuDetectionResult currentState = DetectCurrentState();
            if (currentState.State == ContextMenuState.Off)
            {
                // Already disabled - no write needed
                return new ContextMenuOperationResult(true, null, currentState);
            }

            if (currentState.State == ContextMenuState.Error)
            {
                return new ContextMenuOperationResult(false, "Cannot write: detection failed.", currentState);
            }

            // Delete the entire SynToolkit subtree
            try
            {
                RegistryHelper.DeleteKey(ContextMenuConstants.ShellKeyPath);
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[ContextMenu] Failed to delete shell key.");
                ContextMenuDetectionResult failState = DetectCurrentState();
                return new ContextMenuOperationResult(false, "Failed to delete shell key.", failState);
            }

            // Notify shell of changes
            ShellNotifyHelper.NotifyAssocChanged();

            // Read back and verify
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
            ContextMenuDetectionResult errorState = DetectCurrentState();
            return new ContextMenuOperationResult(false, "Access denied.", errorState);
        }
        catch (SecurityException exception)
        {
            App.logger.Warn(exception, "[ContextMenu] Security exception while disabling context menu.");
            ContextMenuDetectionResult errorState = DetectCurrentState();
            return new ContextMenuOperationResult(false, "Security exception.", errorState);
        }
        catch (IOException exception)
        {
            App.logger.Warn(exception, "[ContextMenu] I/O error while disabling context menu.");
            ContextMenuDetectionResult errorState = DetectCurrentState();
            return new ContextMenuOperationResult(false, "I/O error.", errorState);
        }
        catch (Exception exception)
        {
            App.logger.Warn(exception, "[ContextMenu] Unexpected error while disabling context menu.");
            ContextMenuDetectionResult errorState = DetectCurrentState();
            return new ContextMenuOperationResult(false, "Unexpected error.", errorState);
        }
    }

    /// <summary>
    /// Repairs a stale context menu entry by re-enabling with the current executable path.
    /// Only call this when the state is <see cref="ContextMenuState.Modified"/>.
    /// </summary>
    public static ContextMenuOperationResult Repair() => Enable();

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
