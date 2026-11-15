namespace NordicWind.AdvancedAtlaser.Editor
{
    public enum AtlasModulatorMode { Add, Multiply, Replace }
    public enum AtlasModulatorSource { Audio }

    // 1.9: a runtime value driving a parameter: target = mode(target, lerp(min, max, value)).
    public sealed class AtlasModulator
    {
        // The parameter's path, such as "layers[3].source.color"; indices are list positions.
        public string Target;
        public AtlasModulatorMode Mode = AtlasModulatorMode.Multiply;
        public float Min;
        public float Max = 1f;
        public AtlasModulatorSource Source = AtlasModulatorSource.Audio;
        // For Source Audio.
        public readonly AtlasAudioSource Audio = new AtlasAudioSource();
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
