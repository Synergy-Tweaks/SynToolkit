#nullable enable

using System;
using System.Runtime.InteropServices;

namespace SynToolkit.Services.AudioMixer
{
    internal enum AudioRole
    {
        Console = 0,
        Multimedia = 1,
        Communications = 2
    }

    /// <summary>
    /// Thin wrapper over the undocumented (but long-stable) Windows audio policy COM interface
    /// used to change the system default playback/recording endpoint. Windows exposes no public
    /// API for this, so every device-switcher on Windows uses the same private interface.
    /// </summary>
    internal static class PolicyConfigClient
    {
        private static readonly Guid ClassId = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");

        public static void SetDefaultEndpoint(string deviceId)
        {
            object? instance = null;
            try
            {
                Type? type = Type.GetTypeFromCLSID(ClassId);
                if (type is null)
                {
                    throw new InvalidOperationException("The audio policy service is unavailable on this system.");
                }

                instance = Activator.CreateInstance(type);
                if (instance is not IPolicyConfig policyConfig)
                {
                    throw new InvalidOperationException("The audio policy service does not expose IPolicyConfig.");
                }

                foreach (AudioRole role in new[] { AudioRole.Console, AudioRole.Multimedia, AudioRole.Communications })
                {
                    int hr = policyConfig.SetDefaultEndpoint(deviceId, role);
                    if (hr != 0)
                    {
                        Marshal.ThrowExceptionForHR(hr);
                    }
                }
            }
            finally
            {
                if (instance is not null && Marshal.IsComObject(instance))
                {
                    Marshal.ReleaseComObject(instance);
                }
            }
        }

        [ComImport]
        [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPolicyConfig
        {
            [PreserveSig]
            int GetMixFormat(string pszDeviceName, IntPtr ppFormat);

            [PreserveSig]
            int GetDeviceFormat(string pszDeviceName, bool bDefault, IntPtr ppFormat);

            [PreserveSig]
            int ResetDeviceFormat(string pszDeviceName);

            [PreserveSig]
            int SetDeviceFormat(string pszDeviceName, IntPtr pEndpointFormat, IntPtr mixFormat);

            [PreserveSig]
            int GetProcessingPeriod(string pszDeviceName, bool bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);

            [PreserveSig]
            int SetProcessingPeriod(string pszDeviceName, IntPtr pmftPeriod);

            [PreserveSig]
            int GetShareMode(string pszDeviceName, IntPtr pMode);

            [PreserveSig]
            int SetShareMode(string pszDeviceName, IntPtr mode);

            [PreserveSig]
            int GetPropertyValue(string pszDeviceName, bool bFxStore, IntPtr key, IntPtr pv);

            [PreserveSig]
            int SetPropertyValue(string pszDeviceName, bool bFxStore, IntPtr key, IntPtr pv);

            [PreserveSig]
            int SetDefaultEndpoint(string pszDeviceName, AudioRole role);

            [PreserveSig]
            int SetEndpointVisibility(string pszDeviceName, bool bVisible);
        }
    }
}