#nullable enable

using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace SynToolkit.Services.GpuDrivers;

public static partial class GpuDriverService
{
    private static readonly Guid WinTrustActionGenericVerifyV2 =
        new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    private static void EnsureTrustedAmdDownloadUri(Uri? uri)
    {
        if (uri is null ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !(uri.Host.Equals("amd.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".amd.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "The AMD driver link did not resolve to an official HTTPS AMD address.");
        }
    }

    private static void EnsureTrustedAmdInstaller(string installerPath)
    {
        if (!File.Exists(installerPath))
            throw new FileNotFoundException("The AMD installer could not be verified.", installerPath);

        if (!HasValidEmbeddedSignature(installerPath))
            throw new InvalidOperationException("The AMD driver installer has an invalid or untrusted digital signature.");

        string publisher;
        try
        {
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(installerPath));
            publisher = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("The AMD driver installer publisher could not be verified.", ex);
        }

        if (!publisher.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase) &&
            !publisher.Contains("ATI Technologies", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The downloaded driver is signed by an unexpected publisher: {publisher}.");
        }
    }

    private static bool HasValidEmbeddedSignature(string filePath)
    {
        var fileInfo = new WinTrustFileInfo(filePath);
        var fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());

        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, fDeleteOld: false);
            var trustData = new WinTrustData(fileInfoPointer);
            return WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, ref trustData) == 0;
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(fileInfoPointer);
            Marshal.FreeHGlobal(fileInfoPointer);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
    private static extern int WinVerifyTrust(
        IntPtr hwnd,
        [MarshalAs(UnmanagedType.LPStruct)] Guid actionId,
        ref WinTrustData trustData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint StructureSize;
        [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;

        public WinTrustFileInfo(string filePath)
        {
            StructureSize = (uint)Marshal.SizeOf<WinTrustFileInfo>();
            FilePath = filePath;
            FileHandle = IntPtr.Zero;
            KnownSubject = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint StructureSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UrlReference;
        public uint ProviderFlags;
        public uint UiContext;

        public WinTrustData(IntPtr fileInfo)
        {
            StructureSize = (uint)Marshal.SizeOf<WinTrustData>();
            PolicyCallbackData = IntPtr.Zero;
            SipClientData = IntPtr.Zero;
            UiChoice = 2; // WTD_UI_NONE
            RevocationChecks = 0; // WTD_REVOKE_NONE
            UnionChoice = 1; // WTD_CHOICE_FILE
            FileInfo = fileInfo;
            StateAction = 0; // WTD_STATEACTION_IGNORE
            StateData = IntPtr.Zero;
            UrlReference = null;
            ProviderFlags = 0x00000100; // WTD_SAFER_FLAG
            UiContext = 0;
        }
    }
}
