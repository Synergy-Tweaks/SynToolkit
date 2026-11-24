using Roomy.Core;

namespace Roomy.Tests;

public sealed class PreferencesTests : IDisposable
{
    readonly string _folder = Path.Combine(Path.GetTempPath(), "RoomyTests", Guid.NewGuid().ToString("N"));
    string SettingsPath => Path.Combine(_folder, "settings.json");

    [Fact]
    public void MissingSettingsStayOffWithoutCreatingFiles()
    {
        var p = new PreferencesStore(SettingsPath).Load();
        Assert.False(p.RunInBackground);
        Assert.False(p.RecycleDownloads);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void RecycleChoiceSurvivesRestart()
    {
        new PreferencesStore(SettingsPath).Save(new() { RecycleDownloads = true });
        Assert.True(new PreferencesStore(SettingsPath).Load().RecycleDownloads);
    }

    [Fact]
    public void OlderSettingsFilesKeepDeletingPermanently()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, "{\"RunInBackground\":true}");
        Assert.False(new PreferencesStore(SettingsPath).Load().RecycleDownloads);
    }

    [Fact]
    public void CooldownAndSnoozeSurviveRestart()
    {
        var p = new Preferences
        {
            RunInBackground = true, LowSpaceAlerts = true, WeeklyChecks = true,
            LowSpaceGB = 25, CheckEveryMinutes = 60,
            LastNotification = DateTimeOffset.UtcNow, SnoozedUntil = DateTimeOffset.UtcNow.AddDays(1),
            LastWeeklyCheck = DateTimeOffset.UtcNow.AddDays(-3),
        };
        new PreferencesStore(SettingsPath).Save(p);
        var loaded = new PreferencesStore(SettingsPath).Load();
        Assert.Equal(p, loaded);
        Assert.False(ReminderPolicy.CanNotify(loaded, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void InvalidJsonFailsClosedAndKeepsOriginalForRecovery()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, "{broken");
        var store = new PreferencesStore(SettingsPath);
        Assert.False(store.Load().RunInBackground);
        Assert.NotNull(store.LoadError);
        Assert.Equal("{broken", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void SavingReplacesSettingsAndLeavesNoTemporaryFiles()
    {
        var store = new PreferencesStore(SettingsPath);
        store.Save(new() { RunInBackground = true });
        store.Save(new() { RunInBackground = false });
        Assert.False(store.Load().RunInBackground);
        Assert.Single(Directory.GetFiles(_folder));
    }

    [Fact]
    public void OutOfRangeSettingsAreNormalizedOnLoad()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, "{\"LowSpaceGB\":2147483647,\"CheckEveryMinutes\":0}");
        var p = new PreferencesStore(SettingsPath).Load();
        Assert.Equal(500, p.LowSpaceGB);
        Assert.Equal(30, p.CheckEveryMinutes);
    }

    public void Dispose()
    {
        // Only remove files this test created. Never recurse through links in a temp folder.
        if (!Directory.Exists(_folder)) return;
        foreach (var file in Directory.GetFiles(_folder)) File.Delete(file);
        Directory.Delete(_folder);
    }
}
