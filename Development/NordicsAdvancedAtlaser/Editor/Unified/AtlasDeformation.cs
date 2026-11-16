using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    public enum AtlasDeformationKind
    {
        // Along the normal per axis (object units).
        Inflate,
        // The xy, xz and yz planes in that order (degrees), around the object origin; normals turn too.
        Rotate,
        // Object units.
        Translate,
        // The position rounded up to a grid of Precision steps per metre (world space unless Local);
        // with Centred, to the middle of its cell (ceil(p × precision) − 0.5) / precision.
        Snap,
        // × Amount per axis, around the object origin.
        Scale,
        // About the x, y and z axes (degrees, + Spin × t), around the object origin, by Poiyomi's
        // quaternion product of the three; normals and tangents turn too.
        RotateAxes,
        // World units.
        TranslateWorld,
        // Along the normal by (the map's channel − Bias) × Amount.x (world units).
        Displace,
        // xz = lerp(xz, normalize(xz) × Radius + xz × Height, strength × mask).
        Barrel,
        // p = lerp(p, normalize(p + Centre) × Radius + p × Height, strength × mask).
        Sphere,
        // Around the y axis: f = pow(smoothstep(HeightRange.x, HeightRange.y, y), Funnel) × mask;
        // p.xz += (cos a, sin a) × Radius × f, a = t × Speed + y × Twist; p.y += Pull × f; blended by f.
        Tornado,
        // The wind's waves, in world space.
        Wind,
        // Parts turned toward the camera by a chain of rigs; see AtlasLookAtRig.
        LookAt,
        // Horizontal jitter across the view: offset = a × d × Amount.x / 100 × gate × mask, d the
        // horizontal direction across the view of the object's origin, a = (the glitch map's red and
        // green at (y × Density + t / 20 × Speed), minus its red and blue at (y × Density − t / 20 ×
        // Speed), each × 2 − 1) or a random value per world height and time; gate = step(Threshold,
        // the mean of sin(f), sin(2.2f + 5.52), sin(2.9f + 0.93), sin(4.6f + 8.94)), f = t × Frequency;
        // with an AudioLink band, gate = the band (AudioOverride) or gate + the band.
        Glitch,
        // Where the scene behind (the depth texture) is within FadeLength metres, pushed along the
        // normal by Amount.x × (1 − smoothstep(0, FadeLength, the separation)); not in mirrors.
        DepthBulge,
        // View clip prevention: parts closer than the near plane (+ Bias) and facing the camera
        // (N·forward < −0.2) are pushed along the view direction, at most Amount.x × the object's
        // scale × ObjectScale (Bias scaled too).
        CameraPush
    }

    public enum AtlasLookAtUp { ViewUp, WorldUp, CameraUp, ModelUp }

    // One rig of a look-at: the parts its mask covers turn around Pivot so Forward (with Up)
    // points from Origin to the camera, slerped by Strength × the mask × the falloffs, its
    // Euler angles clamped to the pitch, yaw and roll ranges. Each rig's vectors are turned by the
    // rigs before it (a chain, like bones).
    public sealed class AtlasLookAtRig
    {
        public AtlasMask Mask;
        public float Strength = 1f;
        // The target's up: the world up kept square to the view, the world up, the camera's up,
        // or the object's up.
        public AtlasLookAtUp TargetUp = AtlasLookAtUp.ViewUp;
        // Object space.
        public Vector3 Forward = Vector3.forward;
        public Vector3 Up = Vector3.up;
        public Vector3 Pivot;
        public Vector3 Origin;
        // smoothstep(y, x, the camera's distance from Origin) and of the angle (degrees) between
        // Forward and the camera; (0, 0): no falloff.
        public Vector2 DistanceFalloff;
        public Vector2 FovFalloff;
        // Degrees, min and max.
        public Vector2 Pitch = new Vector2(-180f, 180f);
        public Vector2 Yaw = new Vector2(-180f, 180f);
        public Vector2 Roll = new Vector2(-180f, 180f);
    }

    // 5.3: one vertex operation. The material applies them in list order.
    public sealed class AtlasDeformation
    {
        public AtlasDeformationKind Kind;
        public Vector3 Amount;
        // RotateAxes: degrees per second.
        public Vector3 Spin;
        // Inflate: along the normal's absolute value, so everything moves outward on that axis.
        public bool AbsoluteNormal;
        // Snap: steps per metre.
        public float Precision = 100f;
        public bool Local;
        public bool Centred;
        // Displace.
        public AtlasTextureReference Map;
        public AtlasChannel MapChannel = AtlasChannel.Red;
        public float Bias;
        // Barrel, Sphere, Tornado.
        public float Radius = 1f;
        public float Height;
        public Vector3 Centre;
        public float Speed;
        public float Twist;
        public float Funnel = 1f;
        public float Pull;
        public Vector2 HeightRange = new Vector2(0f, 1f);
        // Wind; null for other kinds.
        public AtlasWind Wind;
        // LookAt: the rigs in order (Strength scales them all); views it doesn't turn in show the
        // parts unturned. Null: every view.
        public readonly System.Collections.Generic.List<AtlasLookAtRig> Rigs = new System.Collections.Generic.List<AtlasLookAtRig>();
        public AtlasVisibility Visibility;
        // Glitch.
        public float Frequency = 1f;
        public float Threshold = 1f;
        public float Density = 10f;
        // −1: none.
        public int AudioBand = -1;
        public bool AudioOverride;
        public AtlasMirrorVisibility Mirror = AtlasMirrorVisibility.Both;
        // DepthBulge.
        public float FadeLength = 0.02f;
        // CameraPush: Bias (metres) and ObjectScale.
        public float ObjectScale = 1f;
        // 0–1.
        public float Strength = 1f;
        // Sampled per vertex (a map on its UV, or a vertex colour); null: everywhere.
        public AtlasMask Mask;
    }

    // Two sine waves along their directions: sin(t × speed + dot(p, direction) × frequency) ×
    // turbulence × direction × amplitude, p the world position; turbulence = lerp(1, the noise
    // texture's channel × 2 − 1 at p.xz × NoiseScale × 0.1 + t × NoiseSpeed, NoiseStrength).
    public sealed class AtlasWind
    {
        public bool Enabled;
        public Vector3 PrimaryDirection = new Vector3(1f, 0f, 0f);
        public float PrimaryAmplitude = 0.2f;
        public float PrimarySpeed = 1f;
        public float PrimaryFrequency = 1f;
        public Vector3 DetailDirection = new Vector3(0f, 0f, 1f);
        public float DetailAmplitude = 0.1f;
        public float DetailSpeed = 2.5f;
        public float DetailFrequency = 5f;
        // Null: no turbulence.
        public AtlasTextureReference Noise;
        public AtlasChannel NoiseChannel = AtlasChannel.Red;
        public float NoiseStrength;
        public float NoiseScale = 10f;
        public float NoiseSpeed = 0.5f;
    }
}
