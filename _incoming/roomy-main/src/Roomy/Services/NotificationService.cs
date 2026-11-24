using Microsoft.Toolkit.Uwp.Notifications;
using Windows.UI.Notifications;
using Roomy.Localization;

namespace Roomy.Services;

public sealed class NotificationService : IDisposable
{
    bool _registered;
    public event Action<string>? Activated;
    public string? Error { get; private set; }

    public bool Initialize()
    {
        if (_registered) return true;
        try
        {
            ToastNotificationManagerCompat.OnActivated += OnActivated;
            _registered = true;
            _ = ToastNotificationManagerCompat.CreateToastNotifier();
            Error = null;
            return true;
        }
        catch (Exception ex)
        {
            Error = S.NotificationsUnavailable + ex.Message;
            Dispose();
            return false;
        }
    }

    void OnActivated(ToastNotificationActivatedEventArgsCompat args)
    {
        Activated?.Invoke(Command(args.Argument));
    }

    internal static string Command(string argument) => argument switch
    {
        "snooze" => "snooze",
        "disable" => "disable",
        "scan" => "scan",
        _ => "open",
    };

    public bool Show(string title, string body, string tag)
    {
        if (!Initialize()) return false;
        try
        {
            var notifier = ToastNotificationManagerCompat.CreateToastNotifier();
            if (notifier.Setting != NotificationSetting.Enabled) return false;
            var content = BuildContent(title, body);
            var toast = new ToastNotification(content.GetXml())
            {
                Tag = tag, Group = "reminders", ExpirationTime = DateTimeOffset.Now.AddDays(1),
            };
            notifier.Show(toast);
            return true;
        }
        catch (Exception ex)
        {
            Error = S.CantSendNotification + ex.Message;
            return false;
        }
    }

    internal static ToastContent BuildContent(string title, string body) => new()
    {
        Launch = "scan",
        Visual = new ToastVisual
        {
            BindingGeneric = new ToastBindingGeneric
            {
                Children = { new AdaptiveText { Text = title }, new AdaptiveText { Text = body } },
            },
        },
        Actions = new ToastActionsCustom
        {
            Buttons =
            {
                new ToastButton(S.NotificationReview, "scan"),
                new ToastButton(S.NotificationTomorrow, "snooze") { ActivationType = ToastActivationType.Background },
                new ToastButton(S.NotificationTurnOff, "disable") { ActivationType = ToastActivationType.Background },
            },
        },
        Audio = new ToastAudio { Silent = true },
    };

    public void Clear()
    {
        if (!_registered) return;
        try { ToastNotificationManagerCompat.History.Clear(); } catch { }
    }

    public void Dispose()
    {
        if (_registered) ToastNotificationManagerCompat.OnActivated -= OnActivated;
        _registered = false;
    }
}
