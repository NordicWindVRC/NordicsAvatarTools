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

        // Times a stage into the diagnostics (listed with Verbose Logging): `using (context.Time(...))`.
        public IDisposable Time(string stage) => new AtlasStageTimer(stage, Diagnostics);
        public void Dispose()
        {
            foreach (var group in Groups)
                foreach (var input in group.Inputs)
                    foreach (var set in input.Bakes.Values) set.Dispose();
        }
    }
    // One stage's wall-clock time, added to a list of lines when disposed.
    public sealed class AtlasStageTimer : IDisposable
    {
        private readonly string stage;
        private readonly List<string> lines;
        private readonly System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

        public AtlasStageTimer(string stage, List<string> lines)
        {
            this.stage = stage;
            this.lines = lines;
        }

        public double Milliseconds => watch.Elapsed.TotalMilliseconds;

        public void Dispose()
        {
            if (!watch.IsRunning) return;
            watch.Stop();
            lines?.Add($"[Time] {stage}: {watch.Elapsed.TotalMilliseconds:0} ms");
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
        public bool NormalCorrection = true;
        public Texture2D PrebakedNormalMap;
        // Null if baking is disabled; non-null empty/flat buffers mean missing geometry.
        public AtlasBakedInputSet Bakes;
    }
    public sealed class AtlasOutputJob
    {
        public NordicsAtlasOutput Component;
        public AtlasOutputConfiguration Configuration;
        public IAtlasOutputShader Shader;
        public readonly List<AtlasOutputSourceJob> Sources = new List<AtlasOutputSourceJob>();
        // The generator job and the resolved output; null until the output is prepared, or when
        // it can't be built.
        public AtlasGeneratorJob GeneratorJob;
        public AtlasOutputPlan Plan;
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
        // True when TangentNormalCorrection is flat (no correction), for example because it was
        // turned off or the surviving triangles are unchanged. Writers can skip it then.
        public bool CorrectionIsFlat;
        // Which texels the current submesh covers (painted fast, without sub-samples).
        public NordicsTextureWrapper CurrentMask;
        // The tangent-space normal correction. The original and current world normals used to
        // make it exist only during the bake.
        public NordicsTextureWrapper TangentNormalCorrection;
        public void Dispose()
        {
            CurrentMask?.Dispose(); TangentNormalCorrection?.Dispose();
        }
    }
}
