namespace NordicWind.AdvancedAtlaser.Editor
{
    public enum AtlasModulatorMode { Add, Multiply, Replace }
    public enum AtlasModulatorSource { Audio, Time, LightLevel }

    // 1.9: a runtime value driving a parameter: target = mode(target, lerp(min, max, value)).
    public sealed class AtlasModulator
    {
        // The parameter's path, such as "layers[3].source.color"; indices are list positions.
        public string Target;
        public AtlasModulatorMode Mode = AtlasModulatorMode.Multiply;
        public float Min;
        public float Max = 1f;
        public AtlasModulatorSource Source = AtlasModulatorSource.Audio;
        // Where the modulation applies: lerp(target, modulated, mask). Null: everywhere.
        public AtlasMask Mask;
        // For Source Audio.
        public readonly AtlasAudioSource Audio = new AtlasAudioSource();
        // For Source Time.
        public readonly AtlasTimeSource Time = new AtlasTimeSource();
        // For Source LightLevel.
        public readonly AtlasLightLevelSource LightLevel = new AtlasLightLevelSource();
    }

    // 0–1: sine 0.5 + 0.5 sin(2π f t), square its rounding, triangle, saw rising, reverse saw falling.
    public enum AtlasWaveform { Sine, Square, Triangle, Saw, ReverseSaw }

    public sealed class AtlasTimeSource
    {
        public AtlasWaveform Waveform = AtlasWaveform.Sine;
        // Cycles per second.
        public float Frequency = 1f;
    }

    // How bright the surroundings are: the average of direct, ambient and vertex light, 0–1.
    public sealed class AtlasLightLevelSource
    {
        // Smooth: rises from threshold − crossfade to threshold + crossfade. Off: the raw level.
        public bool Smooth;
        public float Threshold = 0.5f;
        public float Crossfade = 0.1f;
        // The level includes the surface's own shading.
        public bool IncludeShadows;
    }

    public enum AtlasAudioBand { Bass, LowMids, HighMids, Highs, Volume }

    public enum AtlasAudioSpread
    {
        // The band's current level everywhere.
        None,
        // The level from (offset + coordinate × length) samples ago: audio travels along the coordinate.
        History,
        // 1 where coordinate ≤ level, fading over the softness below it.
        Fill
    }

    public enum AtlasSpreadCoordinate
    {
        UVU, UVV,
        // 1 − abs(view · normal): 0 facing the viewer, 1 at the silhouette.
        ViewAngle,
        // A channel of a texture.
        TextureChannel
    }

    // 1.9: a value from the world's AudioLink.
    public sealed class AtlasAudioSource
    {
        public AtlasAudioBand Band = AtlasAudioBand.Bass;
        // 0 = the raw level, 1 = AudioLink's most smoothed.
        public float Smoothing;
        // Without AudioLink in the world: a decaying pulse, (1 − frac(t × speed)) every other cycle.
        public bool FallbackBeat;
        public float FallbackSpeed = 1f;
        // The value when the world has no AudioLink and there's no fallback beat.
        public float ValueWithoutAudioLink;
        public AtlasAudioSpread Spread = AtlasAudioSpread.None;
        public AtlasSpreadCoordinate Coordinate = AtlasSpreadCoordinate.UVU;
        // For the UV coordinates.
        public AtlasTextureSpace CoordinateUV = AtlasTextureSpace.UV0;
        // For TextureChannel.
        public AtlasTextureReference CoordinateMap;
        public AtlasChannel CoordinateChannel = AtlasChannel.Red;
        // History, in samples.
        public float HistoryOffset;
        public float HistoryLength = 128f;
        public float FillSoftness = 0.1f;
    }
}
