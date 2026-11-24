using System.Globalization;
using System.Resources;

namespace Roomy.Localization;

/// <summary>
/// Every user-facing string goes through here. The English text is the source
/// of truth; translations live in Strings.resx / Strings.&lt;culture&gt;.resx.
/// Unknown keys and missing translations fall back to the English source text.
/// </summary>
public static class Loc
{
    static readonly ResourceManager Rm = new("Roomy.Localization.Strings", typeof(Loc).Assembly);

    /// <summary>The language the app speaks: ROOMY_LANG if set, otherwise Windows'.</summary>
    public static string CurrentLanguage =>
        Environment.GetEnvironmentVariable("ROOMY_LANG") is { Length: > 0 } forced
            ? forced
            : SystemLanguage();

    /// <summary>
    /// Windows collapses every Chinese UI culture to the ISO code "zh". Keep the full
    /// name instead (zh-CN, zh-TW, …) so the Han variant survives: Simplified variants
    /// resolve to the zh-Hans catalogue, Traditional ones fall back to English until a
    /// zh-Hant catalogue exists.
    /// </summary>
    static string SystemLanguage()
    {
        var ui = CultureInfo.CurrentUICulture;
        return ui.TwoLetterISOLanguageName == "zh" ? ui.Name : ui.TwoLetterISOLanguageName;
    }

    /// <summary>True when a translation catalogue for <paramref name="language"/> is available.</summary>
    public static bool HasLanguage(string language) =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, $"{language}", "Roomy.resources.dll"));

    /// <summary>
    /// Resolves <see cref="CurrentLanguage"/> to a catalogue culture. Simplified Chinese
    /// arrives as "zh", "zh-CN", "zh-SG" or "zh-Hans" and the catalogue is named
    /// "zh-Hans", so those all load Simplified Chinese. Traditional variants (zh-TW,
    /// zh-HK, zh-MO, zh-Hant) have no catalogue yet and fall back to English; add cases
    /// here when Strings.zh-Hant.resx lands.
    /// </summary>
    static CultureInfo ResolveCulture()
    {
        var language = CurrentLanguage;
        return language.ToLowerInvariant() switch
        {
            "zh" or "zh-cn" or "zh-sg" or "zh-hans" => CultureInfo.GetCultureInfo("zh-Hans"),
            _ => new CultureInfo(language),
        };
    }

    /// <summary>The translation for <paramref name="key"/>, or the English text itself.</summary>
    public static string Get(string key)
    {
        try
        {
            return Rm.GetString(key, ResolveCulture()) ?? key;
        }
        catch (MissingManifestResourceException)
        {
            return key;
        }
        catch (CultureNotFoundException)
        {
            return key;
        }
    }

    /// <summary>Formats a translated template with <paramref name="args"/>.</summary>
    public static string Format(string key, params object?[] args)
    {
        var template = Get(key);
        try { return string.Format(template, args); }
        catch (FormatException) { return template; }
    }

    /// <summary>
    /// Russian needs three plural forms (1 элемент, 2 элемента, 5 элементов),
    /// English two, Chinese one. The catalogue holds "One", "Few" and "Many";
    /// English uses One/Many, Chinese always uses One.
    /// </summary>
    public static string Plural(long count, string oneKey, string fewKey, string manyKey)
    {
        var language = CurrentLanguage;
        if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return Get(oneKey); // Chinese has a single plural form
        if (language == "ru")
        {
            var n = Math.Abs(count) % 100;
            if (n is >= 11 and <= 14) return Get(manyKey);
            return (n % 10) switch
            {
                1 => Get(oneKey),
                2 or 3 or 4 => Get(fewKey),
                _ => Get(manyKey),
            };
        }
        return count == 1 ? Get(oneKey) : Get(manyKey);
    }

    public static string Items(long count) =>
        Plural(count, "ItemsCountOne", "ItemsCountFew", "ItemsCountMany");

    public static string Files(long count) =>
        Plural(count, "FilesCountOne", "FilesCountFew", "FilesCountMany");
}
