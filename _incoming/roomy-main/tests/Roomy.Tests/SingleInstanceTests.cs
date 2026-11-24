using Roomy.Services;

namespace Roomy.Tests;

public sealed class SingleInstanceTests
{
    // A Windows mutex must be released on the thread that owns it.
#pragma warning disable xUnit1031
    [Fact]
    public void SecondLaunchForwardsCommandToPrimary()
    {
        var name = "Roomy.Tests." + Guid.NewGuid().ToString("N");
        using var primary = new SingleInstance(name);
        using var second = new SingleInstance(name);
        Assert.True(primary.IsPrimary);
        Assert.False(second.IsPrimary);
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.Listen(command => received.TrySetResult(command));
        Assert.True(second.SendAsync("scan").GetAwaiter().GetResult());
        Assert.Equal("scan", received.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
    }

    [Fact]
    public void ExitingPrimaryAllowsANewPrimary()
    {
        var name = "Roomy.Tests." + Guid.NewGuid().ToString("N");
        using (var first = new SingleInstance(name)) Assert.True(first.IsPrimary);
        using var next = new SingleInstance(name);
        Assert.True(next.IsPrimary);
    }
#pragma warning restore xUnit1031
}
