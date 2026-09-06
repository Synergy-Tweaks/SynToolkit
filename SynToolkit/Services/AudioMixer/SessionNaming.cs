#nullable enable

using System;
using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace SynToolkit.Services.AudioMixer
{
    public static class SessionNaming
    {
        public const string SystemSoundsName = "_System Sounds";
        private const string SystemSoundsResource = @"@%SystemRoot%\System32\AudioSrv.Dll,-202";

        public static string Resolve(AudioSessionControl session)
        {
            try
            {
                if (session.IsSystemSoundsSession)
                {
                    return SystemSoundsName;
                }
            }
            catch
            {
            }

            string? displayName = TryGet(() => session.DisplayName);
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                return string.Equals(displayName, SystemSoundsResource, StringComparison.Ordinal)
                    ? SystemSoundsName
                    : displayName;
            }

            uint processId = TryGet(() => session.GetProcessID);
            if (processId != 0)
            {
                try
                {
                    using Process process = Process.GetProcessById((int)processId);
                    return process.ProcessName + ".exe";
                }
                catch
                {
                }
            }

            string? identifier = TryGet(() => session.GetSessionIdentifier);
            if (!string.IsNullOrEmpty(identifier))
            {
                int exeEnd = identifier.LastIndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (exeEnd > 0)
                {
                    int start = identifier.LastIndexOfAny(['\\', '/', '|'], exeEnd) + 1;
                    return identifier[start..(exeEnd + 4)];
                }
            }

            return "Unknown Session";
        }

        public static string GetDisplayName(string name) =>
            string.Equals(name, SystemSoundsName, StringComparison.Ordinal) ? "System Sounds" : name;

        private static T? TryGet<T>(Func<T> getter)
        {
            try
            {
                return getter();
            }
            catch
            {
                return default;
            }
        }
    }
}
