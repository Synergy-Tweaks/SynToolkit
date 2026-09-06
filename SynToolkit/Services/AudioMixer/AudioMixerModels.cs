#nullable enable

namespace SynToolkit.Services.AudioMixer
{
    public readonly record struct AudioMixerHotkey(uint Modifiers, uint VirtualKey)
    {
        public const uint ModAlt = 0x1;
        public const uint ModControl = 0x2;
        public const uint ModShift = 0x4;
        public const uint ModWin = 0x8;

        public static AudioMixerHotkey Default => new(ModControl | ModShift, 0x56);

        public bool HasModifier =>
            (Modifiers & (ModAlt | ModControl | ModShift | ModWin)) != 0;
    }

    public sealed record AudioSessionInfo(
        string InstanceId,
        string Name,
        float ScalarVolume,
        bool IsSystemSounds,
        int ProcessId,
        string? ExecutablePath);

    public sealed record SavedAudioSessionInfo(string Name, float ScalarVolume);

    public sealed record AudioMixerSessionRow(
        string Name,
        string DisplayName,
        bool IsActive,
        bool IsSaved,
        bool IsSystemSounds,
        int Volume,
        string StatusText,
        string? ExecutablePath);
}
