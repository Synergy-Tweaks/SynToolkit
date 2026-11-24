// Portions adapted from Roomy (MIT) © 2026 krezio — https://github.com/krezio/roomy
namespace SynToolkit.Drives.Core;

public static class DrivesText
{
    private const string Missing = "Description unavailable.";

    public static string Get(string key, string fallback)
    {
        string value = App.GetValueFromItemList(key);
        return string.Equals(value, Missing, StringComparison.Ordinal) ? fallback : value;
    }

    public static string Format(string key, string fallback, params object[] args)
    {
        string template = Get(key, fallback);
        try
        {
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }
}
