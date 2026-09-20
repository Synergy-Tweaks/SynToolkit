#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SynToolkit.Services
{
    /// <summary>
    /// Tracks which navigation tabs flagged as "NEW" the user has opened at least once.
    /// Persists to the same per-user LocalAppData folder as other SynToolkit preferences
    /// (e.g. previous-wallpaper.txt, custom wallpapers, logs).
    /// </summary>
    internal static class NavigationNewBadgeService
    {
        private static readonly string SeenTabsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SynToolkit",
            "seen-new-tabs.json");

        /// <summary>
        /// Stable tab IDs mapped to the navigation tags used by MainWindow routing.
        /// Add future NEW tabs here; existing users will not have the new ID in their
        /// persisted seen-set, so the badge appears without migration code.
        /// </summary>
        private static readonly Dictionary<string, string> NewBadgeTabNavigationTags =
            new(StringComparer.Ordinal)
            {
                ["Installer"] = "SynToolkit.Views.AppFetchPage",
                ["Customizations"] = "SynToolkit.Views.AdjustmentsPage",
                ["PowerPlans"] = "SynToolkit.Views.PowerPlansPage",
                ["Games"] = "SynToolkit.Views.GamesPage",
                ["AdvancedConfigurations"] = "Advanced",
            };

        private static readonly Dictionary<string, string> NavigationTagToTabId =
            NewBadgeTabNavigationTags.ToDictionary(
                pair => pair.Value,
                pair => pair.Key,
                StringComparer.Ordinal);

        private static HashSet<string> _seenTabIds = LoadSeenTabIds();

        public static string? GetTabIdForNavigationTag(string? navigationTag)
        {
            if (string.IsNullOrWhiteSpace(navigationTag))
            {
                return null;
            }

            return NavigationTagToTabId.TryGetValue(navigationTag, out string? tabId)
                ? tabId
                : null;
        }

        public static bool ShouldShowNewBadge(string tabId) =>
            NewBadgeTabNavigationTags.ContainsKey(tabId) &&
            !_seenTabIds.Contains(tabId);

        /// <summary>
        /// Records the tab as seen and saves immediately. Returns true when the tab was newly marked.
        /// </summary>
        public static bool MarkTabSeen(string tabId)
        {
            if (!NewBadgeTabNavigationTags.ContainsKey(tabId) || !_seenTabIds.Add(tabId))
            {
                return false;
            }

            SaveSeenTabIds(_seenTabIds);
            return true;
        }

        public static bool MarkTabSeenForNavigationTag(string? navigationTag)
        {
            string? tabId = GetTabIdForNavigationTag(navigationTag);
            return tabId is not null && MarkTabSeen(tabId);
        }

        private static HashSet<string> LoadSeenTabIds()
        {
            try
            {
                if (!File.Exists(SeenTabsPath))
                {
                    return new HashSet<string>(StringComparer.Ordinal);
                }

                string json = File.ReadAllText(SeenTabsPath);
                List<string>? ids = JsonSerializer.Deserialize<List<string>>(json);
                if (ids is null || ids.Count == 0)
                {
                    return new HashSet<string>(StringComparer.Ordinal);
                }

                return new HashSet<string>(
                    ids.Where(id => NewBadgeTabNavigationTags.ContainsKey(id)),
                    StringComparer.Ordinal);
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "Unable to load seen NEW-tab state.");
                return new HashSet<string>(StringComparer.Ordinal);
            }
        }

        private static void SaveSeenTabIds(HashSet<string> seenTabIds)
        {
            try
            {
                string? directory = Path.GetDirectoryName(SeenTabsPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                List<string> payload = seenTabIds
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToList();
                string json = JsonSerializer.Serialize(payload);
                File.WriteAllText(SeenTabsPath, json);
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "Unable to save seen NEW-tab state.");
            }
        }
    }
}
