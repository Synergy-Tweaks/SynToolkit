using System.Xml.Linq;
using Roomy.Services;

namespace Roomy.Tests;

public sealed class NotificationTests
{
    [Theory]
    [InlineData("scan", "scan")]
    [InlineData("snooze", "snooze")]
    [InlineData("disable", "disable")]
    [InlineData("delete", "open")]
    [InlineData("C:\\Windows", "open")]
    [InlineData("", "open")]
    public void NotificationActivationOnlyAcceptsKnownCommands(string input, string expected) =>
        Assert.Equal(expected, NotificationService.Command(input));

    [Fact]
    public void NotificationContentEscapesTextAndOffersOnlyReviewOrReminderControls()
    {
        var xml = XDocument.Parse(NotificationService.BuildContent("<Title>", "A & B").GetContent());
        Assert.Equal("scan", xml.Root!.Attribute("launch")!.Value);
        Assert.Equal("<Title>", xml.Descendants("text").First().Value);
        Assert.Equal("A & B", xml.Descendants("text").Skip(1).First().Value);
        Assert.Equal(new[] { "scan", "snooze", "disable" }, xml.Descendants("action").Select(x => x.Attribute("arguments")!.Value));
        Assert.Equal("true", xml.Descendants("audio").Single().Attribute("silent")!.Value);
    }

    [Fact]
    public async Task StartupStateCanBeReadWithoutChangingRegistration()
    {
        var first = await StartupRegistration.ReadAsync();
        Assert.Equal(first, await StartupRegistration.ReadAsync());
    }
}
