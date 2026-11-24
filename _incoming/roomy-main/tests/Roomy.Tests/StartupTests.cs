using Roomy.Services;

namespace Roomy.Tests;

public sealed class StartupTests
{
    [Theory]
    [InlineData(@"C:\Program Files\Roomy\Roomy.exe")]
    [InlineData(@"D:\My downloads\Roomy-portable.exe")]
    public void StartupQuotesInstallerAndPortablePaths(string path) =>
        Assert.Equal($"\"{path}\" --background", StartupRegistration.StartupCommand(path));

    [Theory]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe")]
    [InlineData("Roomy.exe")]
    [InlineData(@"C:\Roomy\Roomy.dll")]
    public void StartupRejectsFrameworkHostsAndIncompletePaths(string path) =>
        Assert.Throws<InvalidOperationException>(() => StartupRegistration.StartupCommand(path));

    [Fact]
    public void StartupRejectsCommandsBeyondWindowsRunLimit() =>
        Assert.Throws<InvalidOperationException>(() => StartupRegistration.StartupCommand(@"C:\" + new string('x', 250) + @"\Roomy.exe"));
}
