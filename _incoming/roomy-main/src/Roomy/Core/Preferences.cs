using System.Text.Json;
using Roomy.Localization;

namespace Roomy.Core;

public sealed record Preferences
{
    public bool RunInBackground { get; init; }
    public bool LowSpaceAlerts { get; init; }
    public bool WeeklyChecks { get; init; }
    public int LowSpaceGB { get; init; } = 10;
    public int CheckEveryMinutes { get; init; } = 30;
    /// <summary>Send cleaned files from Downloads to the Recycle Bin instead of deleting them.</summary>
    public bool RecycleDownloads { get; init; }
    public DateTimeOffset? SnoozedUntil { get; init; }
    public DateTimeOffset? LastNotification { get; init; }
    public DateTimeOffset? LastWeeklyCheck { get; init; }

    public Preferences Normalize() => this with
    {
        LowSpaceGB = Math.Clamp(LowSpaceGB, 1, 500),
        CheckEveryMinutes = CheckEveryMinutes == 60 ? 60 : 30,
    };
}

public sealed class PreferencesStore(string path)
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roomy", "settings.json");
    public string? LoadError { get; private set; }

    public Preferences Load()
    {
        try
        {
            return File.Exists(path)
                ? (JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path)) ?? new()).Normalize()
                : new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LoadError = S.CantReadSettings;
            return new();
        }
    }

    public void Save(Preferences preferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(preferences.Normalize(), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        LoadError = null;
    }
}

public static class ReminderPolicy
{
    public static bool CanNotify(Preferences p, DateTimeOffset now) => p.RunInBackground &&
        (p.SnoozedUntil is null || now >= p.SnoozedUntil) &&
        (p.LastNotification is null || now - p.LastNotification >= TimeSpan.FromDays(1));

    public static bool IsLowSpace(Preferences p, long freeBytes) => p.LowSpaceAlerts &&
        freeBytes >= 0 && freeBytes < p.LowSpaceGB * (1L << 30);

    public static bool WeeklyCheckDue(Preferences p, DateTimeOffset now) => p.WeeklyChecks &&
        CanNotify(p, now) && (p.LastWeeklyCheck is null || now - p.LastWeeklyCheck >= TimeSpan.FromDays(7));
}
