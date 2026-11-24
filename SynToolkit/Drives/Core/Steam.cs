// Portions adapted from Roomy (MIT) © 2026 krezio — https://github.com/krezio/roomy
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

#nullable enable

namespace SynToolkit.Drives.Core;

public sealed record SteamGame(string AppId, string Name, long Size, DateTime? LastPlayed, string InstallPath);

public static class Steam
{
    private const string RedistributablesAppId = "228980";

    public static string? Root()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string steamPath && Directory.Exists(steamPath))
            {
                return Path.GetFullPath(steamPath.Replace('/', '\\'));
            }
        }
        catch
        {
        }

        string fallback = Path.Combine(Env.ProgramFilesX86, "Steam");
        return Directory.Exists(fallback) ? fallback : null;
    }

    public static List<string> Libraries()
    {
        string? root = Root();
        if (root is null)
        {
            return [];
        }

        List<string> libraries = [root];
        try
        {
            string vdfPath = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdfPath))
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(vdfPath), "\"path\"\\s+\"([^\"]+)\""))
                {
                    libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
                }
            }
        }
        catch
        {
        }

        return libraries
            .Where(Directory.Exists)
            .Select(path => Path.GetFullPath(path).TrimEnd('\\'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<SteamGame> Games()
    {
        List<SteamGame> list = [];
        foreach (string library in Libraries())
        {
            string appsPath = Path.Combine(library, "steamapps");
            List<string> manifests;
            try
            {
                manifests = Directory.EnumerateFiles(appsPath, "appmanifest_*.acf").ToList();
            }
            catch
            {
                continue;
            }

            foreach (string acf in manifests)
            {
                try
                {
                    string text = File.ReadAllText(acf);

                    string? ReadValue(string key)
                    {
                        Match match = Regex.Match(text, "\"" + key + "\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
                        return match.Success ? match.Groups[1].Value : null;
                    }

                    string? appId = ReadValue("appid");
                    string? name = ReadValue("name");
                    string? installDir = ReadValue("installdir");
                    string? lastPlayed = ReadValue("LastPlayed");
                    if (appId is null || name is null || installDir is null || lastPlayed is null || appId == RedistributablesAppId)
                    {
                        continue;
                    }

                    long.TryParse(ReadValue("SizeOnDisk"), out long size);
                    DateTime? playedAt = long.TryParse(lastPlayed, out long unixSeconds) && unixSeconds > 0
                        ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime
                        : null;

                    list.Add(new SteamGame(appId, name, size, playedAt, Path.Combine(appsPath, "common", installDir)));
                }
                catch
                {
                }
            }
        }

        return list;
    }
}
