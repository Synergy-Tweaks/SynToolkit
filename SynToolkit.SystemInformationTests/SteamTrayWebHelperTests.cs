using SynToolkit.Services.SteamTrayWebHelper;

namespace SynToolkit.SystemInformationTests;

internal static class SteamTrayWebHelperTests
{
    public static void PathResolutionPrefersHkcuThenHklmThenFallback()
    {
        var hkcu = SteamTrayWebHelperPaths.ResolveSteamDirectory(
            () => @"C:/Games/Steam",
            () => @"D:\Steam",
            dir => dir is @"C:\Games\Steam" or @"D:\Steam",
            path => path.Equals(@"C:\Games\Steam\steam.exe", StringComparison.OrdinalIgnoreCase));
        Equal(@"C:\Games\Steam", hkcu.Directory, "HKCU SteamPath wins");
        True(hkcu.SteamExeExists, "HKCU path has steam.exe");

        var hklm = SteamTrayWebHelperPaths.ResolveSteamDirectory(
            () => null,
            () => @"D:\Steam\",
            dir => dir is @"D:\Steam",
            path => path.Equals(@"D:\Steam\steam.exe", StringComparison.OrdinalIgnoreCase));
        Equal(@"D:\Steam", hklm.Directory, "HKLM InstallPath used when HKCU missing");
        True(hklm.SteamExeExists, "HKLM path has steam.exe");

        var fallback = SteamTrayWebHelperPaths.ResolveSteamDirectory(
            () => "   ",
            () => null,
            dir => string.Equals(dir, SteamTrayWebHelperConstants.FallbackSteamDirectory, StringComparison.OrdinalIgnoreCase),
            path => path.EndsWith(@"\steam.exe", StringComparison.OrdinalIgnoreCase));
        Equal(SteamTrayWebHelperConstants.FallbackSteamDirectory, fallback.Directory, "fallback Program Files path");
        True(fallback.SteamExeExists, "fallback has steam.exe when present");
    }

    public static void MissingSteamExeIsReported()
    {
        var result = SteamTrayWebHelperPaths.ResolveSteamDirectory(
            () => @"C:\EmptySteam",
            () => null,
            dir => dir is @"C:\EmptySteam",
            _ => false);
        Equal(@"C:\EmptySteam", result.Directory, "directory still returned");
        True(!result.SteamExeExists, "steam.exe missing → not valid");
    }

    public static void DllPresenceUsesSteamFolder()
    {
        True(
            SteamTrayWebHelperPaths.IsDllInstalled(@"C:\Steam", path => path.EndsWith(@"\umpdc.dll", StringComparison.OrdinalIgnoreCase)),
            "DLL present");
        True(
            !SteamTrayWebHelperPaths.IsDllInstalled(@"C:\Steam", _ => false),
            "DLL absent");
        Equal(
            @"C:\Steam\umpdc.dll",
            SteamTrayWebHelperPaths.GetDllPath(@"C:\Steam"),
            "DLL path beside steam.exe");
    }

    public static void Sha256ParsingAndHashCompare()
    {
        Equal(
            SteamTrayWebHelperConstants.ExpectedSha256,
            SteamTrayWebHelperPaths.TryParseSha256FileContents(
                $"{SteamTrayWebHelperConstants.ExpectedSha256.ToLowerInvariant()}  umpdc.dll"),
            "hash filename form");
        Equal(
            SteamTrayWebHelperConstants.ExpectedSha256,
            SteamTrayWebHelperPaths.TryParseSha256FileContents(
                $"SHA256 (umpdc.dll) = {SteamTrayWebHelperConstants.ExpectedSha256}"),
            "openssl-style form");
        True(
            SteamTrayWebHelperPaths.TryParseSha256FileContents("not-a-hash") is null,
            "rejects non-hash text");
        True(
            SteamTrayWebHelperPaths.HashesMatch(
                SteamTrayWebHelperConstants.ExpectedSha256,
                SteamTrayWebHelperConstants.ExpectedSha256.ToLowerInvariant()),
            "hash compare is case-insensitive");
        True(
            !SteamTrayWebHelperPaths.HashesMatch(SteamTrayWebHelperConstants.ExpectedSha256, "00"),
            "mismatched hashes fail");
    }

    public static void PinnedReleaseConstantsAreCoherent()
    {
        Equal("1.0", SteamTrayWebHelperConstants.ReleaseTag, "pinned tag");
        Equal("umpdc.dll", SteamTrayWebHelperConstants.DllFileName, "DLL name");
        True(
            SteamTrayWebHelperConstants.DllDownloadUrl.Contains(
                "/releases/download/1.0/umpdc.dll",
                StringComparison.Ordinal),
            "download URL pins tag + file");
        True(
            SteamTrayWebHelperConstants.ExpectedSha256.Length == 64,
            "expected SHA-256 is 64 hex chars");
        True(
            SteamTrayWebHelperConstants.SteamProcessNames.Contains("steam")
            && SteamTrayWebHelperConstants.SteamProcessNames.Contains("steamwebhelper")
            && SteamTrayWebHelperConstants.SteamProcessNames.Contains("steamservice"),
            "closes steam + webhelper + service");
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}, Actual: {actual}");
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
