using SynToolkit.Services;

namespace SynToolkit.SystemInformationTests;

internal static class WindowsVersionServiceTests
{
    public static void ClassificationRules()
    {
        True(WindowsVersionService.ClassifyIsWindows10(10240), "10240 is Win10");
        True(WindowsVersionService.ClassifyIsWindows10(19041), "19041 is Win10");
        True(WindowsVersionService.ClassifyIsWindows10(19045), "19045 is Win10");
        True(!WindowsVersionService.ClassifyIsWindows10(22000), "22000 is not Win10");
        True(WindowsVersionService.ClassifyIsWindows11(22000), "22000 is Win11");
        True(WindowsVersionService.ClassifyIsWindows11(26100), "26100 is Win11");
        True(!WindowsVersionService.ClassifyIsWindows11(19045), "19045 is not Win11");

        True(WindowsVersionService.ClassifyIsSupported(19041, true), "19041 supported");
        True(WindowsVersionService.ClassifyIsSupported(19045, true), "19045 supported");
        True(WindowsVersionService.ClassifyIsSupported(26100, true), "26100 supported");
        True(!WindowsVersionService.ClassifyIsSupported(19040, true), "19040 unsupported");
        True(!WindowsVersionService.ClassifyIsSupported(17763, true), "17763 unsupported");
        True(!WindowsVersionService.ClassifyIsSupported(19045, false), "failed read unsupported");
    }

    public static void FormatDetectedLabel()
    {
        WindowsVersionInfo win10 = new(19045, 5608, "22H2", true);
        Equal(
            "Detected: Windows 10 22H2 (build 19045.5608)",
            WindowsVersionService.FormatDetected(win10));

        WindowsVersionInfo win11 = new(26100, 1, "24H2", true);
        Equal(
            "Detected: Windows 11 24H2 (build 26100.1)",
            WindowsVersionService.FormatDetected(win11));

        WindowsVersionInfo failed = WindowsVersionInfo.Failed;
        Equal(
            "Detected: Windows (build 0)",
            WindowsVersionService.FormatDetected(failed));
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Equal(string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected '{expected}' but got '{actual}'.");
        }
    }
}
