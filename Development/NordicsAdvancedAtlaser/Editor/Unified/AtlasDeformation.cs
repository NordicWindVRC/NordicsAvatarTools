using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    public enum AtlasTessellationMode { Uniform, EdgeLength, Distance }

    // 5.4: triangles subdivided before the deformations (Poiyomi Pro's Tessellated variants).
    // Uniform: each edge split Factor times; EdgeLength: into pieces about EdgeLength screen pixels
    // long; Distance: Factor near the camera, falling to 1 between Distance.x and .y meters.
    // Phong (0–1) bends the new vertices toward the curved surface the vertex normals describe;
    // triangles outside the view (with CullMargin) aren't subdivided.
    public sealed class AtlasTessellation
    {
        public bool Enabled;
        public AtlasTessellationMode Mode = AtlasTessellationMode.EdgeLength;
        public float Factor = 2.5f;
        public float EdgeLength = 30f;
        public Vector2 Distance = new Vector2(2f, 8f);
        public float Phong;
        public float CullMargin = 0.1f;
    }

    public enum AtlasDeformationKind
    {
        // Along the normal per axis (object units).
        Inflate,
        // The xy, xz and yz planes in that order (degrees), around the object origin; normals turn too.
        Rotate,
        // Object units.
        Translate,
        // The position rounded up to a grid of Precision steps per meter (world space unless Local);
        // with Centered, to the middle of its cell (ceil(p × precision) − 0.5) / precision.
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
        // p = lerp(p, normalize(p + Center) × Radius + p × Height, strength × mask).
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
        // Where the scene behind (the depth texture) is within FadeLength meters, pushed along the
        // normal by Amount.x × (1 − smoothstep(0, FadeLength, the separation)); not in mirrors.
        DepthBulge,
        // View clip prevention: parts closer than the near plane (+ Bias) and facing the camera
        // (N·forward < −0.2) are pushed along the view direction, at most Amount.x × the object's
        // scale × ObjectScale (Bias scaled too).
        CameraPush,
        // TPS (Thry's Penetration System); see AtlasTps.
        Tps,
        // Flattened against a contact or an orifice plane; see AtlasSquish.
        Squish
    }

    // Thry's Penetration System: the mesh (measured by the setup tool: length, scale and axes, a
    // baked mesh or vertex colors) bends along a Bézier curve toward a matching orifice light on
    // its channel, squeezed and bulged around the orifice (each × its hole and ring multipliers),
    // with pumping and an idle shrink when nothing is near.
    public sealed class AtlasTps
    {
        public float Length = 1f;
        public Vector3 Scale = Vector3.one;
        public Vector3 Right = Vector3.right, Up = Vector3.up, Forward = Vector3.forward;
        public AtlasTextureReference BakedMesh;
        public bool BakedVertexColors;
        public bool TwoSidedRings = true;
        public int Channel;
        public float BezierStart;
        public float BezierSmoothness = 0.4f;
        public float SmoothStart = 1f;
        public float Squeeze = 0.3f, SqueezeDistance = 0.2f;
        public Vector2 SqueezeMultipliers = Vector2.one;
        public float Bulge = 0.3f, BulgeDistance = 0.2f, BulgeFalloff = 0.05f;
        public Vector2 BulgeMultipliers = Vector2.one;
        public float PumpingStrength, PumpingSpeed = 1f, PumpingWidth = 0.2f;
        public float IdleShrinkWidth = 1f, IdleShrinkLength = 1f;
    }

    // Flattened against a collider: a VRChat contact (1–8) or an orifice light's plane of
    // PlaneSize; the squish (Strength, spreading sideways by Spread, at most Max, eased over
    // Falloff, pushed ClipOffset off the plane) is also a color: the gradient sampled between
    // GradientMin and GradientMax by the squish, × intensity, replacing, multiplying and adding
    // unlit by their weights (a pattern layer of kind Squish).
    public sealed class AtlasSquish
    {
        public bool Orifice;
        public int Contact;
        public Vector2 PlaneSize = Vector2.one;
        public float Strength = 0.97f, Spread = 0.15f, Max = 1f, Falloff = 0.05f, ClipOffset = 0.05f;
        public AtlasTextureReference Gradient;
        public float GradientMin = 0.135f, GradientMax = 0.97f, GradientIntensity = 1f;
        public float Replace, Multiply = 1f, AddUnlit;
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
        // Snap: steps per meter.
        public float Precision = 100f;
        public bool Local;
        public bool Centered;
        // Displace.
        public AtlasTextureReference Map;
        public AtlasChannel MapChannel = AtlasChannel.Red;
        public float Bias;
        // Barrel, Sphere, Tornado.
        public float Radius = 1f;
        public float Height;
        public Vector3 Center;
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
        // CameraPush: Bias (meters) and ObjectScale.
        public float ObjectScale = 1f;
        public AtlasTps Tps;
        public AtlasSquish Squish;
        // 0–1.
        public float Strength = 1f;
        // Sampled per vertex (a map on its UV, or a vertex color); null: everywhere.
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
