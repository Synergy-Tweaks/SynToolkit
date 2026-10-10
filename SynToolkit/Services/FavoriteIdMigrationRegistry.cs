#nullable enable

using SynToolkit.Utils;
using System;

namespace SynToolkit.Services
{
    /// <summary>
    /// Registry-backed favourite ID migration (requires App + RegistryHelper).
    /// </summary>
    public static class FavoriteIdMigrationRegistry
    {
        /// <summary>
        /// Rewrites HKLM\SOFTWARE\SynToolkit\Favorites so legacy FsoAndGameBar becomes
        /// the two split tweak IDs. Safe to call repeatedly.
        /// </summary>
        public static void MigrateFavoritesRegistry()
        {
            const string keyPath = @"HKLM\SOFTWARE\SynToolkit\Favorites";
            try
            {
                if (!RegistryHelper.KeyExists(keyPath))
                {
                    return;
                }

                object? legacy = RegistryHelper.GetValue(keyPath, FavoriteIdMigration.LegacyFsoAndGameBar);
                if (legacy is null)
                {
                    return;
                }

                foreach (string id in FavoriteIdMigration.Expand(
                    FavoriteIdMigration.LegacyFsoAndGameBar,
                    passthroughUnknown: false))
                {
                    RegistryHelper.SetValue(keyPath, id, true);
                }

                RegistryHelper.DeleteValue(keyPath, FavoriteIdMigration.LegacyFsoAndGameBar);
                App.logger.Info(
                    "[Favorites] Migrated {0} → {1}, {2}.",
                    FavoriteIdMigration.LegacyFsoAndGameBar,
                    FavoriteIdMigration.FullScreenOptimizations,
                    FavoriteIdMigration.XboxGameBar);
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[Favorites] Legacy FsoAndGameBar migration skipped.");
            }
        }
    }
}
