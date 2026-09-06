#nullable enable

using System.Collections.Concurrent;

namespace SynToolkit.Services.AudioMixer
{
    public sealed class VolumePolicy
    {
        private readonly ConcurrentDictionary<string, float> _savedVolumes = new(StringComparer.OrdinalIgnoreCase);
        private float _defaultVolumeScalar = 1f;

        public float DefaultVolumeScalar
        {
            get => Volatile.Read(ref _defaultVolumeScalar);
            set => Volatile.Write(ref _defaultVolumeScalar, Math.Clamp(value, 0f, 1f));
        }

        public void Load(IDictionary<string, float> savedVolumes)
        {
            _savedVolumes.Clear();
            foreach ((string name, float volume) in savedVolumes)
            {
                _savedVolumes[name] = Math.Clamp(volume, 0f, 1f);
            }
        }

        public void SetSaved(string name, float scalarVolume) =>
            _savedVolumes[name] = Math.Clamp(scalarVolume, 0f, 1f);

        public void RemoveSaved(string name) => _savedVolumes.TryRemove(name, out _);

        public float? GetSaved(string name) =>
            _savedVolumes.TryGetValue(name, out float volume) ? volume : null;

        public IReadOnlyDictionary<string, float> GetAllSaved() =>
            new Dictionary<string, float>(_savedVolumes, StringComparer.OrdinalIgnoreCase);

        public float GetDesiredVolume(string name) => GetSaved(name) ?? DefaultVolumeScalar;
    }
}
