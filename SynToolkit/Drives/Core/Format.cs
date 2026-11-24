// Portions adapted from Roomy (MIT) © 2026 krezio — https://github.com/krezio/roomy
namespace SynToolkit.Drives.Core;

public static class Format
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Bytes(long bytes)
    {
        if (bytes <= 0) return "0 B";

        double value = bytes;
        int index = 0;
        while (value >= 1024 && index < Units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        string number = index == 0 || value >= 100
            ? value.ToString("0")
            : value >= 10
                ? value.ToString("0.#")
                : value.ToString("0.##");

        return $"{number} {Units[index]}";
    }

    public static string Ago(DateTime when)
    {
        TimeSpan diff = DateTime.Now - when;
        if (diff.TotalDays < 1) return "today";
        if (diff.TotalDays < 2) return "yesterday";
        if (diff.TotalDays < 14) return $"{(int)diff.TotalDays} days ago";
        if (diff.TotalDays < 60) return $"{(int)(diff.TotalDays / 7)} weeks ago";
        if (diff.TotalDays < 365) return $"{(int)(diff.TotalDays / 30)} months ago";
        int years = (int)(diff.TotalDays / 365);
        return years == 1 ? "a year ago" : $"{years} years ago";
    }
}
