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
        // The world position rounded up to a grid of Precision steps per metre.
        Snap
    }

    // 5.3: one vertex operation. The material applies them in list order.
    public sealed class AtlasDeformation
    {
        public AtlasDeformationKind Kind;
        public Vector3 Amount;
        // Inflate: along the normal's absolute value, so everything moves outward on that axis.
        public bool AbsoluteNormal;
        // Snap: steps per metre.
        public float Precision = 100f;
        // 0–1.
        public float Strength = 1f;
        // Sampled per vertex on UV0; null: everywhere.
        public AtlasMask Mask;
    }
}
