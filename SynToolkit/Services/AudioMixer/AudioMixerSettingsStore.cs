#nullable enable

using System.Text.Json;
using System.IO;

namespace SynToolkit.Services.AudioMixer
{
    public sealed class AudioMixerSettingsStore
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true
        };

        private readonly object _lock = new();
        private readonly string _settingsPath;
        private AudioMixerSettingsDocument _document;

        public AudioMixerSettingsStore()
        {
            _settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SynToolkit",
                "audio-mixer-settings.json");
            _document = Load();
        }

        public float GetDefaultVolumeScalar()
        {
            lock (_lock)
            {
                return Math.Clamp(_document.DefaultVolumeScalar, 0f, 1f);
            }
        }

        public void SetDefaultVolumeScalar(float value)
        {
            lock (_lock)
            {
                _document.DefaultVolumeScalar = Math.Clamp(value, 0f, 1f);
                SaveUnlocked();
            }
        }

        public IReadOnlyDictionary<string, float> GetSavedVolumes()
        {
            lock (_lock)
            {
                return _document.SavedSessions.ToDictionary(
                    pair => pair.Key,
                    pair => Math.Clamp(pair.Value.VolumeScalar, 0f, 1f),
                    StringComparer.OrdinalIgnoreCase);
            }
        }

        public void SaveSessionVolume(string name, float scalarVolume)
        {
            lock (_lock)
            {
                _document.SavedSessions[name] = new SavedAudioSessionDocument
                {
                    VolumeScalar = Math.Clamp(scalarVolume, 0f, 1f)
                };
                SaveUnlocked();
            }
        }

        public void RemoveSavedSession(string name)
        {
            lock (_lock)
            {
                if (_document.SavedSessions.Remove(name))
                {
                    SaveUnlocked();
                }
            }
        }

        public bool GetTipsDismissed()
        {
            lock (_lock)
            {
                return _document.TipsDismissed;
            }
        }

        public void SetTipsDismissed(bool dismissed)
        {
            lock (_lock)
            {
                _document.TipsDismissed = dismissed;
                SaveUnlocked();
            }
        }

        public AudioMixerHotkey GetHotkey()
        {
            lock (_lock)
            {
                return new AudioMixerHotkey(
                    _document.Hotkey.Modifiers,
                    _document.Hotkey.VirtualKey);
            }
        }

        public void SetHotkey(AudioMixerHotkey hotkey)
        {
            lock (_lock)
            {
                _document.Hotkey = new AudioMixerHotkeyDocument
                {
                    Modifiers = hotkey.Modifiers,
                    VirtualKey = hotkey.VirtualKey
                };
                SaveUnlocked();
            }
        }

        private AudioMixerSettingsDocument Load()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    return CreateDefaultDocument();
                }

                AudioMixerSettingsDocument? document = JsonSerializer.Deserialize<AudioMixerSettingsDocument>(
                    File.ReadAllText(_settingsPath),
                    SerializerOptions);
                return document ?? CreateDefaultDocument();
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "Unable to load audio mixer settings. Using defaults.");
                return CreateDefaultDocument();
            }
        }

        private AudioMixerSettingsDocument CreateDefaultDocument() =>
            new()
            {
                DefaultVolumeScalar = 1f,
                Hotkey = new AudioMixerHotkeyDocument
                {
                    Modifiers = AudioMixerHotkey.Default.Modifiers,
                    VirtualKey = AudioMixerHotkey.Default.VirtualKey
                },
                SavedSessions = new Dictionary<string, SavedAudioSessionDocument>(StringComparer.OrdinalIgnoreCase)
            };

        private void SaveUnlocked()
        {
            try
            {
                string? directory = Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonSerializer.Serialize(_document, SerializerOptions);
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "Unable to save audio mixer settings.");
            }
        }

        private sealed class AudioMixerSettingsDocument
        {
            public float DefaultVolumeScalar { get; set; } = 1f;

            public bool TipsDismissed { get; set; }

            public AudioMixerHotkeyDocument Hotkey { get; set; } = new();

            public Dictionary<string, SavedAudioSessionDocument> SavedSessions { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class AudioMixerHotkeyDocument
        {
            public uint Modifiers { get; set; } = AudioMixerHotkey.Default.Modifiers;

            public uint VirtualKey { get; set; } = AudioMixerHotkey.Default.VirtualKey;
        }

        private sealed class SavedAudioSessionDocument
        {
            public float VolumeScalar { get; set; }
        }
    }
}
