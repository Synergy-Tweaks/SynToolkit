#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace SynToolkit.Services
{
    /// <summary>
    /// Maps retired tweak IDs (favourites / profiles) onto their replacements.
    /// Unknown old IDs are ignored without throwing when passthrough is false.
    /// </summary>
    public static class FavoriteIdMigration
    {
        public const string LegacyFsoAndGameBar = "FsoAndGameBar";
        public const string FullScreenOptimizations = "FullScreenOptimizations";
        public const string XboxGameBar = "XboxGameBar";

        private static readonly IReadOnlyDictionary<string, string[]> Map =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [LegacyFsoAndGameBar] = new[] { FullScreenOptimizations, XboxGameBar },
            };

        /// <summary>
        /// Expands a stored ID to zero or more current IDs.
        /// </summary>
        public static IEnumerable<string> Expand(
            string? storedId,
            bool passthroughUnknown = true)
        {
            if (string.IsNullOrWhiteSpace(storedId))
            {
                yield break;
            }

            if (Map.TryGetValue(storedId, out string[]? replacements))
            {
                foreach (string id in replacements)
                {
                    yield return id;
                }

                yield break;
            }

            if (passthroughUnknown)
            {
                yield return storedId;
            }
        }

        /// <summary>
        /// Expands a profile's configuration-service key list for import.
        /// </summary>
        public static List<string> ExpandProfileKeys(IEnumerable<string> keys)
        {
            HashSet<string> result = new(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                foreach (string expanded in Expand(key, passthroughUnknown: true))
                {
                    result.Add(expanded);
                }
            }

            return result.ToList();
        }
    }
}
