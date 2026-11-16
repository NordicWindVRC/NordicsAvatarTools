namespace NordicWind.AdvancedAtlaser.Editor
{
    // BlendToColor: a colour target becomes lerp(target, Color, value).
    public enum AtlasModulatorMode { Add, Multiply, Replace, BlendToColor }
    public enum AtlasModulatorSource { Audio, Time, LightLevel, Sweep, CameraDistance }

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
        // Shapes the source's value first: lerp(x, y, smoothstep(z, w, value)); (0, 1, 0, 1) leaves it.
        public UnityEngine.Vector4 Curve = new UnityEngine.Vector4(0f, 1f, 0f, 1f);
        // For BlendToColor. Linear.
        public UnityEngine.Color Color = UnityEngine.Color.white;
        // For Source Audio.
        public readonly AtlasAudioSource Audio = new AtlasAudioSource();
        // For Source Time.
        public readonly AtlasTimeSource Time = new AtlasTimeSource();
        // For Source LightLevel.
        public readonly AtlasLightLevelSource LightLevel = new AtlasLightLevelSource();
        // For Source Sweep.
        public readonly AtlasSweepSource Sweep = new AtlasSweepSource();
        // For Source CameraDistance.
        public readonly AtlasCameraDistanceSource CameraDistance = new AtlasCameraDistanceSource();
    }

    // The distance to the camera from the pixel or the object's origin: saturate((d − Near) /
    // (Far − Near)) through a four-key curve (values at 0, ⅓, ⅔, 1, linear between), stepped at
    // 0.5 when Step.
    public sealed class AtlasCameraDistanceSource
    {
        public float Near, Far = 1f;
        public bool PixelPosition = true;
        public UnityEngine.Vector4 Curve = new UnityEngine.Vector4(0f, 1f / 3f, 2f / 3f, 1f);
        public bool Step;
    }

    // A band sweeping along a direction over time: value = (p^Width + (1 − p)^(4 Width)) / 2,
    // p = frac((position · Direction − (t + Offset) × Speed) / Interval), the position in object
    // space (or the vertex colour's RGB).
    public sealed class AtlasSweepSource
    {
        public UnityEngine.Vector3 Direction = new UnityEngine.Vector3(0f, -10f, 0f);
        public float Width = 10f;
        public float Speed = 10f;
        public float Interval = 20f;
        public float Offset;
        public bool VertexColorPosition;
    }

    // 0–1 of t′ = t + Offset: sine 0.5 + 0.5 sin(2π f t′), square its rounding, triangle, saw
    // rising, reverse saw falling, pulse 1 while frac(f t′) ≤ Duty.
    public enum AtlasWaveform { Sine, Square, Triangle, Saw, ReverseSaw, Pulse }

    public sealed class AtlasTimeSource
    {
        public AtlasWaveform Waveform = AtlasWaveform.Sine;
        // Cycles per second.
        public float Frequency = 1f;
        // Seconds added to the time.
        public float Offset;
        public float Duty = 0.5f;
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
        // The level is the direct light's luminance alone.
        public bool DirectOnly;
        // Linear: saturate((level − Low) / (High − Low)) (when not Smooth).
        public bool Linear;
        public float Low, High = 1f;
    }

    public enum AtlasAudioBand { Bass, LowMids, HighMids, Highs, Volume }

    public enum AtlasAudioSpread
    {
        // The band's current level everywhere.
        None,
        // The level from (offset + coordinate × length) samples ago: audio travels along the coordinate.
        History,
        // 1 where coordinate ≤ level, fading over the softness below it.
        Fill,
        // AudioLink's chronotensity: time that runs with the band (ChronoMotion picks how), in seconds.
        Chrono
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
        // Without AudioLink in the world the target keeps its own value (no fallback beat).
        public bool IdleWithoutAudioLink;
        // value = saturate((level − Threshold) / (1 − Threshold)).
        public float Threshold;
        // Chrono: AudioLink's chronotensity motion, 0–7.
        public int ChronoMotion;
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
