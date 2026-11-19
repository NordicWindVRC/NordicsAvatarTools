using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // A mask an output feature needs over the whole atlas: what it is at each source (as a key
    // that compares equal for equal masks), how to evaluate it there, and whether the reading
    // place can invert what it reads.
    public sealed class AtlasMaskRequest
    {
        public string Name;
        // Per index into the job's sources: AtlasMaskKeys.Of or AtlasMaskKeys.Constant.
        public Func<int, string> Key;
        // Per source: the key of 1 − the mask there, or null where it has none.
        public Func<int, string> InverseKey;
        public Func<int, AtlasUnifiedMaterial, int, int, NordicsTextureWrapper> Evaluate;
        public bool CanInvert;
    }

    // Where an allocated mask is: a channel of a mask texture (possibly read inverted), or no
    // texture when it's one value everywhere (Constant).
    public sealed class AtlasAllocatedMask
    {
        public AtlasMask Mask;
        // The mask place (Mask1 to Mask8) it's in.
        public AtlasVirtualMap Place;
        public float Constant = 1f;
        public bool IsConstant => Mask == null;
    }

    // Keys describing a mask at one source, equal for masks that give the same values.
    public static class AtlasMaskKeys
    {
        public static string Constant(float value) => "c:" + value.ToString("0.####", CultureInfo.InvariantCulture);

        /// <summary>The mask × <paramref name="scale"/>; a constant without a mask. Masks that read
        /// the bus or vertex colors depend on their material, so its identity is part of the key.</summary>
        public static string Of(AtlasMask mask, float scale, AtlasUnifiedMaterial material)
        {
            if (mask == null) return Constant(scale);
            string own = mask.Bus >= 0 || mask.VertexColor || mask.TimesVertexColor
                ? "@" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(material) : "";
            return "m:" + AtlasFieldwise.Signature(mask) + own + "×" + scale.ToString("0.####", CultureInfo.InvariantCulture);
        }

        /// <summary>The key of 1 − the mask: a constant's complement, or the same map read inverted
        /// where that is exactly the complement (full strength, no levels, no bus); otherwise null.</summary>
        public static string Inverse(AtlasMask mask, float scale, AtlasUnifiedMaterial material)
        {
            if (mask == null) return Constant(1f - scale);
            if (!Mathf.Approximately(scale, 1f) || mask.Strength < 1f || mask.Levels != null || mask.Bus >= 0) return null;
            var inverted = new AtlasMask();
            AtlasFieldwise.Copy(mask, inverted);
            inverted.Invert = !mask.Invert;
            return Of(inverted, 1f, material);
        }

        public static bool IsConstant(string key, out float value)
        {
            value = 0f;
            return key != null && key.StartsWith("c:", StringComparison.Ordinal) &&
                   float.TryParse(key.Substring(2), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }

    // The mask allocator (ATLAS_OUTPUT.md, "Masks"): a mask that is one value everywhere needs no
    // texture; equal masks share a place; a mask that is the inverse of a placed one shares it
    // where the reader can invert; the rest take the next free channel of the output's mask
    // textures (Mask1 to Mask8), in the order features ask.
    public static class AtlasMaskAllocator
    {
        public static readonly AtlasVirtualMap[] Places =
        {
            AtlasVirtualMap.Mask1, AtlasVirtualMap.Mask2, AtlasVirtualMap.Mask3, AtlasVirtualMap.Mask4,
            AtlasVirtualMap.Mask5, AtlasVirtualMap.Mask6, AtlasVirtualMap.Mask7, AtlasVirtualMap.Mask8
        };

        /// <summary>Null when every place is taken.</summary>
        public static AtlasAllocatedMask Allocate(AtlasFeatureContext context, AtlasMaskRequest request)
        {
            var plan = context.Plan;
            int count = context.Job.Sources.Count;
            var keys = Enumerable.Range(0, count).Select(request.Key).ToList();
            if (keys.Count > 0 && keys.All(k => k == keys[0]) && AtlasMaskKeys.IsConstant(keys[0], out float constant))
                return new AtlasAllocatedMask { Constant = constant };
            string vector = string.Join("|", keys);
            if (plan.PlacedMasks.TryGetValue(vector, out var placed))
                return new AtlasAllocatedMask { Mask = Read(placed, false), Place = plan.PlacedMaskPlaces[vector] };
            if (request.CanInvert && request.InverseKey != null)
            {
                var inverse = Enumerable.Range(0, count).Select(request.InverseKey).ToList();
                if (inverse.All(k => k != null) && plan.PlacedMasks.TryGetValue(string.Join("|", inverse), out var other))
                    return new AtlasAllocatedMask { Mask = Read(other, true), Place = plan.PlacedMaskPlaces[string.Join("|", inverse)] };
            }
            foreach (var place in Places)
            {
                var texture = context.Capabilities.TextureFor(place);
                if (texture == null || plan.MaskPlaces.Contains(place)) continue;
                plan.MaskPlaces.Add(place);
                var map = new AtlasMapRequest
                {
                    Map = place,
                    Reference = new AtlasTextureReference { Interpretation = AtlasTextureInterpretation.Data },
                    Evaluate = request.Evaluate
                };
                for (int i = 0; i < count; i++) map.Sources.Add(i);
                context.Maps.Add(map);
                var mask = new AtlasMask { Map = map.Reference, Channel = Channel(texture, place) };
                plan.PlacedMasks[vector] = mask;
                plan.PlacedMaskPlaces[vector] = place;
                return new AtlasAllocatedMask { Mask = Read(mask, false), Place = place };
            }
            return null;
        }

        // A reader's own mask object over a placed one.
        private static AtlasMask Read(AtlasMask placed, bool invert) =>
            new AtlasMask { Map = placed.Map, Channel = placed.Channel, Invert = invert ? !placed.Invert : placed.Invert };

        private static AtlasChannel Channel(AtlasOutputTexture texture, AtlasVirtualMap map)
        {
            switch (texture.Channels.Find(c => c.Map == map)?.Channels ?? AtlasTextureChannels.R)
            {
                case AtlasTextureChannels.G: return AtlasChannel.Green;
                case AtlasTextureChannels.B: return AtlasChannel.Blue;
                case AtlasTextureChannels.A: return AtlasChannel.Alpha;
                default: return AtlasChannel.Red;
            }
        }
    }
}
