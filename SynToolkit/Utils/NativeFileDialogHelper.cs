#nullable enable

using System;
using System.Windows.Forms;

namespace SynToolkit.Utils
{
    /// <summary>
    /// Shows the classic Win32 file-open dialog (via the WindowsForms FrameworkReference)
    /// rather than Windows.Storage.Pickers.FileOpenPicker. The WinRT picker's broker
    /// activation is unreliable for this app's elevated (requireAdministrator) launch path
    /// and can silently return without ever showing a dialog; the classic
    /// IFileOpenDialog-backed picker used here does not depend on that broker.
    /// </summary>
    public static class NativeFileDialogHelper
    {
        public static string? ShowOpenFileDialog(IntPtr ownerWindowHandle, string filter)
        {
            using OpenFileDialog dialog = new()
            {
                Filter = filter,
                CheckFileExists = true,
                Multiselect = false,
                RestoreDirectory = true,
                AutoUpgradeEnabled = true
            };

            DialogResult result = ownerWindowHandle == IntPtr.Zero
                ? dialog.ShowDialog()
                : dialog.ShowDialog(new Win32Window(ownerWindowHandle));

            if (result != DialogResult.OK)
                return null;

            if (string.IsNullOrWhiteSpace(dialog.FileName))
            {
                throw new InvalidOperationException("The file picker closed without returning a file path.");
            }

            return dialog.FileName;
        }

        public static string? ShowSaveFileDialog(IntPtr ownerWindowHandle, string filter, string? defaultFileName = null)
        {
            using SaveFileDialog dialog = new()
            {
                Filter = filter,
                FileName = defaultFileName ?? string.Empty,
                OverwritePrompt = true
            };

            return dialog.ShowDialog(new Win32Window(ownerWindowHandle)) == DialogResult.OK
                ? dialog.FileName
                : null;
        }

        public static string? ShowFolderBrowserDialog(IntPtr ownerWindowHandle, string description)
        {
            using FolderBrowserDialog dialog = new()
            {
                Description = description,
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };

            DialogResult result = ownerWindowHandle == IntPtr.Zero
                ? dialog.ShowDialog()
                : dialog.ShowDialog(new Win32Window(ownerWindowHandle));

            if (result != DialogResult.OK)
                return null;

            return string.IsNullOrWhiteSpace(dialog.SelectedPath) ? null : dialog.SelectedPath;
        }

        private sealed class Win32Window : IWin32Window
        {
            public Win32Window(IntPtr handle) => Handle = handle;
            public IntPtr Handle { get; }
        }
    }
}
