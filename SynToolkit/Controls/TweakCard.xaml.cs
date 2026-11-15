#nullable enable

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SynToolkit.ViewModels;

namespace SynToolkit.Controls;

public sealed partial class TweakCard : UserControl
{
    public static readonly DependencyProperty ItemProperty =
        DependencyProperty.Register(
            nameof(Item),
            typeof(NvidiaTweakRowViewModel),
            typeof(TweakCard),
            new PropertyMetadata(null));

    public TweakCard()
    {
        InitializeComponent();
    }

    public NvidiaTweakRowViewModel? Item
    {
        get => (NvidiaTweakRowViewModel?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public event EventHandler<TweakCardActionEventArgs>? ActionRequested;

    private void Raise(TweakCardActionType actionType)
    {
        if (Item is null)
        {
            return;
        }

        ActionRequested?.Invoke(this, new TweakCardActionEventArgs(Item.Id, actionType));
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e) => Raise(TweakCardActionType.Apply);
    private void RevertButton_Click(object sender, RoutedEventArgs e) => Raise(TweakCardActionType.Revert);
    private void CheckButton_Click(object sender, RoutedEventArgs e) => Raise(TweakCardActionType.Check);
    private void CustomValueButton_Click(object sender, RoutedEventArgs e) => Raise(TweakCardActionType.CustomValue);
    private void CopyPathButton_Click(object sender, RoutedEventArgs e) => Raise(TweakCardActionType.CopyRegistryPath);
    private void OpenRegeditButton_Click(object sender, RoutedEventArgs e) => Raise(TweakCardActionType.OpenInRegedit);
}
