#nullable enable

using System.Collections.Generic;
using System.Diagnostics;

namespace SynToolkit.Services.AudioMixer
{
    /// <summary>
    /// Finds apps that are open with a visible window but have no audio session yet. Windows only
    /// creates a session once an app starts producing audio, so enumerating windowed processes is
    /// what lets the mixer list the apps a user actually has open (and store a level that is
    /// applied the next time that app plays audio).
    /// </summary>
    internal static class OpenAppEnumerator
    {
        // Pure UI hosts that never produce audio on their own.
        private static readonly HashSet<string> IgnoredProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "ApplicationFrameHost",
            "TextInputHost",
            "ShellExperienceHost",
            "StartMenuExperienceHost",
            "SearchHost",
            "LockApp",
            "WidgetService",
            "Widgets"
        };

        public static IReadOnlyList<AudioAppProcessInfo> GetOpenApps()
        {
            var apps = new Dictionary<string, AudioAppProcessInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    if (process.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(process.MainWindowTitle))
                    {
                        continue;
                    }

                    string processName = process.ProcessName;
                    if (string.IsNullOrWhiteSpace(processName) || IgnoredProcessNames.Contains(processName))
                    {
                        continue;
                    }

                    string name = processName + ".exe";
                    if (!apps.ContainsKey(name))
                    {
                        apps[name] = new AudioAppProcessInfo(name, process.Id, TryGetPath(process));
                    }
                }
                catch
                {
                    // A process can exit or deny access while it is being inspected.
                }
                finally
                {
                    process.Dispose();
                }
            }

            return [.. apps.Values];
        }

        private static string? TryGetPath(Process process)
        {
            try
            {
                return process.MainModule?.FileName;
            }
            catch
            {
                return null;
            }
        }
    }
}