using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // One light of the virtual rig, ready to evaluate: plain values, safe to read on every core.
    public struct AtlasRigLight
    {
        // Toward the light, in avatar-root space.
        public Vector3 Direction;
        // Linear; luminance 1 at intensity 1. The temperature's color by the color influence.
        public Color Color;
        // The role's weights, × the light's intensity.
        public float Diffuse, Specular;
        // 0: sharp; 1: very wide.
        public float Softness;

        // The widest diffuse wrap and angular size, at softness 1.
        public const float MaxWrap = 0.5f, MaxAngularSize = 30f;

        // How far past the edge between the lit and the shadow side the light reaches, in N·L.
        public float Wrap => Softness * MaxWrap;
        // The light's angular size in degrees, which widens the roughness its highlights see.
        public float AngularSize => Softness * MaxAngularSize;

        // Wrapped Lambert, max(0, (N·L + wrap) / (1 + wrap)): softness approximated by a little
        // wrap rather than by integrating an area light.
        public float Lambert(Vector3 normal, Vector3 direction) =>
            Mathf.Max(0f, (Vector3.Dot(normal, direction) + Wrap) / (1f + Wrap));
    }

    // The virtual light rig (ATLAS_OUTPUT.md, "Virtual light rigs"): key, fill and back lights
    // and a sky and ground ambient, in avatar-root space (the avatar faces +Z, up is +Y). The role
    // sets a light's weights, the rig its direction, the settings its intensity, temperature and
    // softness. All values are starting values, to tune with real avatars.
    public sealed class AtlasLightRig
    {
        public AtlasRigLight Key, Fill, Back;
        // The ambient hemisphere: the sky's light from above, the ground's from below.
        public Color Sky, Ground;
        public bool Symmetric;

        // The roles' diffuse and specular weights.
        public const float KeyDiffuse = 1f, KeySpecular = 1f;
        public const float FillDiffuse = 0.35f, FillSpecular = 0.1f;
        public const float BackDiffuse = 0.6f, BackSpecular = 0.6f;
        public const float SkyWeight = 0.25f, GroundWeight = 0.1f;
        public const float SkyTemperature = 9000f, GroundTemperature = 4000f;

        // Directions as azimuth (degrees from +Z toward +X) and elevation (degrees above the
        // horizon). Classic: the key in front, above, to one side; the fill in front, low, on
        // the other side; the back light behind and above. Symmetric: the same, centered.
        public static readonly Vector2 ClassicKey = new Vector2(40f, 35f), ClassicFill = new Vector2(-55f, 5f), ClassicBack = new Vector2(-150f, 45f);
        public static readonly Vector2 SymmetricKey = new Vector2(0f, 35f), SymmetricFill = new Vector2(0f, -10f), SymmetricBack = new Vector2(180f, 45f);

        public static AtlasLightRig Create(AtlasBakeSettings settings, bool symmetric)
        {
            settings = settings ?? new AtlasBakeSettings();
            float influence = Mathf.Clamp01(settings.colorInfluence);
            return new AtlasLightRig
            {
                Symmetric = symmetric,
                Key = Light(settings.key, symmetric ? SymmetricKey : ClassicKey, KeyDiffuse, KeySpecular, influence),
                Fill = Light(settings.fill, symmetric ? SymmetricFill : ClassicFill, FillDiffuse, FillSpecular, influence),
                Back = Light(settings.back, symmetric ? SymmetricBack : ClassicBack, BackDiffuse, BackSpecular, influence),
                Sky = Temperature(SkyTemperature, influence) * SkyWeight,
                Ground = Temperature(GroundTemperature, influence) * GroundWeight
            };
        }

        private static AtlasRigLight Light(AtlasBakeLight light, Vector2 angles, float diffuse, float specular, float influence)
        {
            light = light ?? new AtlasBakeLight();
            float intensity = Mathf.Max(0f, light.intensity);
            return new AtlasRigLight
            {
                Direction = Direction(angles.x, angles.y),
                Color = Temperature(light.temperature, influence),
                Diffuse = diffuse * intensity,
                Specular = specular * intensity,
                Softness = Mathf.Clamp01(light.softness)
            };
        }

        // The unit vector toward a light at this azimuth and elevation.
        public static Vector3 Direction(float azimuth, float elevation)
        {
            float a = azimuth * Mathf.Deg2Rad, e = elevation * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(a) * Mathf.Cos(e));
        }

        // A color temperature's linear color (Unity's own conversion), scaled to luminance 1 and
        // blended from white by the color influence: a baked light color stays in every world,
        // so only a little of it is baked.
        public static Color Temperature(float kelvin, float influence)
        {
            var color = Mathf.CorrelatedColorTemperatureToRGB(Mathf.Clamp(kelvin, 1000f, 40000f));
            float luminance = 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
            if (luminance > 1e-5f) color /= luminance;
            var result = Color.Lerp(Color.white, color, Mathf.Clamp01(influence));
            result.a = 1f;
            return result;
        }

        // The ambient light a surface facing `normal` gets: the sky and ground blended by how far
        // it faces up.
        public Color Ambient(Vector3 normal) => Color.Lerp(Ground, Sky, Mathf.Clamp01(normal.y * 0.5f + 0.5f));
    }

    // The virtual view (ATLAS_OUTPUT.md, "Virtual view"): a camera far in front of the avatar,
    // looking along −Z. A texel facing it is seen from the front (+Z); one facing away is seen
    // along its own normal instead, blended over the sides, so the sides and the back aren't
    // baked as a silhouette.
    public static class AtlasVirtualView
    {
        // Where the view is fully frontal (N·Z at or above) and fully along the normal (at or below).
        public const float Frontal = 0.5f, AlongNormal = -0.5f;

        // The unit vector from a texel facing `normal` (unit length) toward its viewer.
        public static Vector3 Direction(Vector3 normal)
        {
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Frontal, AlongNormal, normal.z));
            var view = Vector3.Lerp(Vector3.forward, normal, t);
            return view.sqrMagnitude > 1e-8f ? view.normalized : Vector3.forward;
        }

        // `direction` turned as the front (+Z) turns toward `view` (unit length): tilted up or
        // down, then turned around up, so up stays up and a rig turned with the view keeps its
        // lights above. Plain arithmetic, safe on every core.
        public static Vector3 Turn(Vector3 direction, Vector3 view)
        {
            float cosTilt = Mathf.Sqrt(view.x * view.x + view.z * view.z), sinTilt = view.y;
            float sinTurn = 0f, cosTurn = 1f;
            if (cosTilt > 1e-6f)
            {
                sinTurn = view.x / cosTilt;
                cosTurn = view.z / cosTilt;
            }
            float y = direction.y * cosTilt + direction.z * sinTilt;
            float z = -direction.y * sinTilt + direction.z * cosTilt;
            return new Vector3(direction.x * cosTurn + z * sinTurn, y, -direction.x * sinTurn + z * cosTurn);
        }
    }
}
