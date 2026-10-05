#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

namespace SynToolkit.Services.Games
{
    internal static class EpicArtworkSlug
    {
        private static readonly string[] EditionSuffixes =
        {
            "-enhanced-edition",
            "-enhanced",
            "-digital-deluxe-edition",
            "-deluxe-edition",
            "-ultimate-edition",
            "-standard-edition",
            "-definitive-edition",
            "-complete-edition",
            "-collectors-edition",
            "-game-of-the-year-edition",
            "-goty-edition",
            "-gold-edition",
            "-premium-edition",
            "-special-edition",
            "-legendary-edition",
            "-deluxe",
            "-ultimate"
        };

        public static IReadOnlyList<string> BuildCandidates(string? name)
        {
            var candidates = new List<string>();
            if (string.IsNullOrWhiteSpace(name))
            {
                return candidates;
            }

            AddWithVariants(candidates, name);

            string withoutPunctuation = StripParentheticals(name);
            if (!string.Equals(withoutPunctuation, name, StringComparison.Ordinal))
            {
                AddWithVariants(candidates, withoutPunctuation);
            }

            string mainTitle = ExtractMainTitle(name);
            if (mainTitle.Length > 0 &&
                !string.Equals(mainTitle, name, StringComparison.Ordinal) &&
                !string.Equals(mainTitle, withoutPunctuation, StringComparison.Ordinal))
            {
                AddWithVariants(candidates, mainTitle);
            }

            return candidates;
        }
        private static string ExtractMainTitle(string name)
        {
            int index = name.IndexOfAny(new[] { ':', '\u2013', '\u2014' });
            return index <= 0 ? string.Empty : name[..index].Trim();
        }

        private static void AddWithVariants(List<string> candidates, string name)
        {
            string slug = Normalize(name);
            if (slug.Length == 0)
            {
                return;
            }

            AddDistinct(candidates, slug);

            string baseSlug = slug;
            while (true)
            {
                string? trimmed = TrimEditionSuffix(baseSlug);
                if (string.IsNullOrEmpty(trimmed))
                {
                    break;
                }

                baseSlug = trimmed;
                AddDistinct(candidates, baseSlug);
            }
        }

        private static string Normalize(string name)
        {
            var builder = new StringBuilder(name.Length);
            bool pendingSeparator = false;

            foreach (char c in name)
            {
                if (c is '\'' or '\u2019' or '\u00B4' or '`')
                {
                    continue;
                }

                if (char.IsAsciiLetterOrDigit(c))
                {
                    if (pendingSeparator && builder.Length > 0)
                    {
                        builder.Append('-');
                    }

                    builder.Append(char.ToLowerInvariant(c));
                    pendingSeparator = false;
                }
                else
                {
                    pendingSeparator = true;
                }
            }

            return builder.ToString();
        }

        private static string StripParentheticals(string name)
        {
            var builder = new StringBuilder(name.Length);
            int depth = 0;

            foreach (char c in name)
            {
                if (c is '(' or '[')
                {
                    depth++;
                    continue;
                }

                if (c is ')' or ']')
                {
                    if (depth > 0)
                    {
                        depth--;
                    }

                    continue;
                }

                if (depth == 0)
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        private static string? TrimEditionSuffix(string slug)
        {
            foreach (string suffix in EditionSuffixes)
            {
                if (slug.Length > suffix.Length && slug.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return slug[..^suffix.Length].TrimEnd('-');
                }
            }

            return null;
        }

        private static void AddDistinct(List<string> candidates, string value)
        {
            if (value.Length > 0 && !candidates.Contains(value, StringComparer.Ordinal))
            {
                candidates.Add(value);
            }
        }
    }
}