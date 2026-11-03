using System;
using System.Collections.Generic;
using UnityEngine;
using NordicWind.TextureWrapper;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Build-scoped, nonserialized. Dispose all Texture Wrapper buffers after output preparation.
    public sealed class AtlasBuildContext : IDisposable
    {
        public readonly GameObject AvatarRoot;
        public readonly List<AtlasBuildGroup> Groups = new List<AtlasBuildGroup>();
        public readonly List<string> Diagnostics = new List<string>();
        public AtlasBuildContext(GameObject root) { AvatarRoot = root; }
        public void Dispose()
        {
            foreach (var group in Groups)
                foreach (var input in group.Inputs)
                    foreach (var set in input.Bakes.Values) set.Dispose();
        }
    }
    public sealed class AtlasBuildGroup
    {
        public NordicsAdvancedAtlaser Component;
        public readonly List<SkinnedMeshRenderer> Renderers = new List<SkinnedMeshRenderer>();
        public readonly List<Material> DiscoveredMaterials = new List<Material>();
        public readonly List<AtlasInputMaterialJob> Inputs = new List<AtlasInputMaterialJob>();
        public readonly List<AtlasOutputJob> Outputs = new List<AtlasOutputJob>();
    }
    public sealed class AtlasInputMaterialJob
    {
        public Material SourceMaterial;
        public Shader SourceShader;
        public IAtlasSourceShaderParser Parser;
        public AtlasUnifiedMaterial Unified;
        // Key: WxH. All outputs with the same bake dimensions share the buffers.
        public readonly Dictionary<string, AtlasBakedInputSet> Bakes = new Dictionary<string, AtlasBakedInputSet>();
    }
    public sealed class AtlasOutputSourceJob
    {
        public AtlasInputMaterialJob Input;
        public Rect UVRegion;
        public string SourceKey;
        // Null if baking is disabled; non-null empty/flat buffers mean missing geometry.
        public AtlasBakedInputSet Bakes;
    }
    public sealed class AtlasOutputJob
    {
        public NordicsAtlasOutput Component;
        public AtlasOutputConfiguration Configuration;
        public IAtlasOutputShader Shader;
        public readonly List<AtlasOutputSourceJob> Sources = new List<AtlasOutputSourceJob>();
    }
    public sealed class AtlasBakedInputSet : IDisposable
    {
        public int Width, Height;
        public readonly List<AtlasSubmeshBake> Submeshes = new List<AtlasSubmeshBake>();
        public void Dispose() { foreach(var mesh in Submeshes) mesh.Dispose(); }
    }
    public sealed class AtlasSubmeshBake : IDisposable
    {
        public string RendererName;
        public int SubmeshIndex;
        public bool CurrentSubmeshMissing;
        public NordicsTextureWrapper CurrentMask;
        public NordicsTextureWrapper OriginalWorldNormals;
        public NordicsTextureWrapper CurrentWorldNormals;
        public NordicsTextureWrapper TangentNormalCorrection;
        public void Dispose()
        {
            CurrentMask?.Dispose(); OriginalWorldNormals?.Dispose();
            CurrentWorldNormals?.Dispose(); TangentNormalCorrection?.Dispose();
        }
    }
}
