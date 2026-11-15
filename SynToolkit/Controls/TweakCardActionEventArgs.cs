#nullable enable

namespace SynToolkit.Controls;

public enum TweakCardActionType
{
    Apply,
    Revert,
    Check,
    CustomValue,
    CopyRegistryPath,
    OpenInRegedit
}

public sealed class TweakCardActionEventArgs : EventArgs
{
    public TweakCardActionEventArgs(string tweakId, TweakCardActionType actionType)
    {
        TweakId = tweakId;
        ActionType = actionType;
    }

    public string TweakId { get; }
    public TweakCardActionType ActionType { get; }
}
