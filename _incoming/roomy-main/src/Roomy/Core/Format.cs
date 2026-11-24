using Roomy.Localization;
namespace Roomy.Core;

public static class Format
{
    static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Bytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < Units.Length - 1) { v /= 1024; i++; }
        string n = i == 0 || v >= 100 ? v.ToString("0") : v >= 10 ? v.ToString("0.#") : v.ToString("0.##");
        return $"{n} {Units[i]}";
    }

    public static string Ago(DateTime when)
    {
        var d = DateTime.Now - when;
        if (d.TotalDays < 1) return S.Today;
        if (d.TotalDays < 2) return S.Yesterday;
        if (d.TotalDays < 14)
        {
            var n = (int)d.TotalDays;
            return string.Format(Loc.Plural(n, "AgoDaysOne", "AgoDaysFew", "AgoDays"), n);
        }
        if (d.TotalDays < 60) return Loc.Format("AgoWeeks", (int)(d.TotalDays / 7));
        if (d.TotalDays < 365) return Loc.Format("AgoMonths", (int)(d.TotalDays / 30));
        var y = (int)(d.TotalDays / 365);
        return y == 1 ? S.YearAgo : string.Format(Loc.Plural(y, "AgoYearsOne", "AgoYearsFew", "AgoYears"), y);
    }
}
