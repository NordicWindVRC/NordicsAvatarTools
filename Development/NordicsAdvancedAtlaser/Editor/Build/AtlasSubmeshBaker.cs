using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using NordicWind.TextureWrapper;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Builds transient material maps with Nordic's Texture Wrapper 0.8.3.
    // UV-space correspondence is intentional: optimizer can change indices and vertex count.
    public static class AtlasSubmeshBaker
    {
        private static TextureSettings Normals(TextureDataType kind) => new TextureSettings {
            DataType=kind, SRGB=false, Mipmaps=false, Compression=TextureCompression.None,
            WrapMode=TextureWrapMode.Clamp, OutputChannels=TextureOutputChannels.RGBA
        };
        private static TextureSettings Masks() => new TextureSettings {
            DataType=TextureDataType.Mask, SRGB=false, Mipmaps=false, Compression=TextureCompression.None,
            WrapMode=TextureWrapMode.Clamp, OutputChannels=TextureOutputChannels.RGB
        };
        // Corrections larger than this more likely come from a mismatch between the original
        // and reduced mesh than from real shape, so they fade out (see NormalDifferenceOptions).
        private const float MaxCorrectionAngle = 60f;
        // One summary line per material, never one per texel. Overlapping UVs are a normal
        // modeling choice and are handled safely, so they are information only. Corrections
        // cut by the angle limit point to a mismatch: one warning when they reach 1%.
        private static void ReportCorrection(string material,NormalDifferenceStatistics stats,List<string> diagnostics)
        {
            if(stats.Baked==0) return;
            string Share(int count) => (100.0*count/stats.Baked).ToString("F1")+"%";
            diagnostics.Add(material+": normal correction on "+stats.Baked+" texels; overlapping UVs left uncorrected on "+
                Share(stats.Overlapping)+"; corrections reduced by the "+MaxCorrectionAngle+"° limit on "+Share(stats.Limited)+
                "; largest correction kept "+stats.LargestAngle.ToString("F1")+"°.");
            if(stats.Limited*100>=stats.Baked)
                diagnostics.Add("[Warning] "+material+": "+Share(stats.Limited)+" of the normal correction is larger than "+
                    MaxCorrectionAngle+"° and was reduced or left out. The original and reduced meshes may not match there, " +
                    "for example if UVs moved during reduction.");
        }
        private static NordicsTextureWrapper Flat(int width,int height,TextureDataType kind)
        {
            var map = new NordicsTextureWrapper(width,height,Normals(kind));
            map.Fill(new Color(0,0,1,1)); return map;
        }
        // One material -> one baked entry. Original sections are a union over ALL
        // pre-merge renderers/submeshes, without renderer-path correspondence.
        public static AtlasBakedInputSet Bake(Material material,int width,int height,
            IReadOnlyList<AtlasMeshSection> originalSections,
            IReadOnlyList<SkinnedMeshRenderer> currentRenderers,
            Transform avatarRoot, List<string> diagnostics, string debugFolder = null, bool normalCorrection = true,
            bool excludeHidden = true)
        {
            if(material==null) throw new ArgumentNullException(nameof(material));
            if(width<1 || height<1) throw new ArgumentOutOfRangeException(nameof(width));
            var current=Current(material,currentRenderers,avatarRoot);
            if(current.Count>1)
                throw new InvalidOperationException("[Nordic's Advanced Atlaser] material '"+material.name+
                    "' has "+current.Count+" surviving submeshes; exactly one is required after mesh merging. " +
                    "Resolve the duplicate material assignment or merge the submeshes before atlasing.");
            var result=new AtlasBakedInputSet { Width=width, Height=height };
            try
            {
                if(current.Count==0)
                {
                    result.Submeshes.Add(Missing(width,height,0,"<missing>"));
                    diagnostics.Add(material.name+": zero surviving submeshes; one empty mask supplied.");
                    ExportDebug(debugFolder, material, result.Submeshes[0], 0, 0, diagnostics);
                    return result;
                }
                var section=current[0];
                diagnostics.Add(material.name+": current pose: "+section.PoseDescription+
                    "; renderer-to-root="+section.RendererToAvatarRoot.ToString());
                if (originalSections != null)
                    foreach (var src in originalSections)
                        diagnostics.Add(material.name+": captured pose ["+src.RendererPath+"]: "+src.PoseDescription+
                            "; renderer-to-root="+src.RendererToAvatarRoot.ToString());
                var bake=new AtlasSubmeshBake { RendererName=section.RendererPath,SubmeshIndex=section.SubmeshIndex };
                result.Submeshes.Add(bake);
                bool hasOriginals=originalSections!=null && originalSections.Count>0;
                string skip=!normalCorrection ? "it is turned off for this material"
                    : !hasOriginals ? null
                    : AtlasGeometryComparison.SurvivingTrianglesUnchanged(originalSections,section)
                        ? "the surviving triangles are unchanged from the original" : null;
                if(skip!=null || !hasOriginals)
                {
                    // No bake: only the fast UV usage mask is needed, and the correction is flat.
                    bake.CurrentMask=new NordicsTextureWrapper(width,height,Masks());
                    Rasterize(section,null,bake.CurrentMask,null,null,null);
                    bake.TangentNormalCorrection=Flat(width,height,TextureDataType.NormalTangent);
                    bake.CorrectionIsFlat=true;
                    diagnostics.Add(skip!=null
                        ? material.name+": normal correction skipped because "+skip+"; the correction is flat."
                        : material.name+": original source geometry unavailable; tangent correction is flat.");
                    ExportDebug(debugFolder,material,bake,hasOriginals?originalSections.Count:0,current.Count,diagnostics);
                    return result;
                }
                // Export excluded original triangles separately; they must never
                // contribute to the original normal or current-coverage textures.
                if (debugFolder != null && originalSections.Any(s =>
                    s.ExcludedBlendShapeTriangles != null && s.ExcludedBlendShapeTriangles.Length > 0))
                {
                    TryExport(() => ExportExcludedTriangles(debugFolder, originalSections, width, height), diagnostics);
                }
                bake.TangentNormalCorrection=BakeCorrection(originalSections,section,width,height,MaxCorrectionAngle,
                    out var statistics, maps =>
                    {
                        SampleNormalAgreement(maps.Original,maps.FrameNormal,diagnostics,material.name);
                        if(debugFolder==null) return;
                        TryExport(() => AtlasDebugExporter.Normal(debugFolder,"TangentBasis_Tangent",maps.Tangent),diagnostics);
                        TryExport(() => AtlasDebugExporter.Normal(debugFolder,"TangentBasis_Bitangent",maps.Bitangent),diagnostics);
                        TryExport(() => AtlasDebugExporter.Normal(debugFolder,"TangentBasis_Normal",maps.FrameNormal),diagnostics);
                        ExportGeometryDebug(debugFolder,material,maps.Original,maps.FrameNormal,diagnostics);
                        // Preserve the raw correction for debug inspection before padding.
                        TryExport(() => AtlasDebugExporter.Normal(debugFolder,
                            "TangentNormalCorrection_PreDilation", maps.Correction), diagnostics);
                    },diagnostics,excludeHidden);
                if(excludeHidden)
                    diagnostics.Add(material.name+": surfaces hidden behind another surface with the same UVs were left out of the bake (depth test).");
                ReportCorrection(material.name,statistics,diagnostics);
                diagnostics.Add(material.name+": full correction dilation completed; original coverage unchanged.");
                diagnostics.Add(material.name+": combined "+originalSections.Count+
                    " original section(s) into one material bake; current submeshes=1.");
                // The usage mask is a result, so it's painted once the bake's maps are released.
                bake.CurrentMask=new NordicsTextureWrapper(width,height,Masks());
                Rasterize(section,null,bake.CurrentMask,null,null,null);
                ExportDebug(debugFolder,material,bake,originalSections.Count,current.Count,diagnostics);
                return result;
            }
            catch { result.Dispose(); throw; }
        }
        /// <summary>The maps of one bake, handed to the inspect callback of BakeCorrection.</summary>
        internal sealed class CorrectionMaps
        {
            public NordicsTextureWrapper Original, Tangent, Bitangent, FrameNormal, Correction;
            public NormalDifferenceStatistics Statistics;
        }
        /// <summary>
        /// Bakes the tangent-space normal correction that restores the originals' shading on the
        /// current section, with the safety rules, and dilates it. Used by the build and by the
        /// Normal Baker. Only the maps the bake needs exist at the same time; they're released
        /// before it returns. <paramref name="inspect"/> runs after the bake, before dilation and
        /// release, for diagnostics and debug exports. Reading a map normalizes it in place, so
        /// nothing may read the frame maps before the bake. <paramref name="excludeHidden"/>: surfaces
        /// hidden close behind another surface with the same UVs are left out (the depth test).
        /// </summary>
        internal static NordicsTextureWrapper BakeCorrection(IReadOnlyList<AtlasMeshSection> originals,
            AtlasMeshSection current,int width,int height,float maxAngle,out NormalDifferenceStatistics statistics,
            Action<CorrectionMaps> inspect=null, List<string> timings=null, bool excludeHidden=true)
        {
            NordicsTextureWrapper correction;
            using(var originalNormals=new NordicsTextureWrapper(width,height,Normals(TextureDataType.NormalWorld)))
            using(var tangent=new NordicsTextureWrapper(width,height,Normals(TextureDataType.NormalWorld)))
            using(var bitangent=new NordicsTextureWrapper(width,height,Normals(TextureDataType.NormalWorld)))
            using(var normalBasis=new NordicsTextureWrapper(width,height,Normals(TextureDataType.NormalWorld)))
            using(var coverage=new NordicsTextureWrapper(width,height,Masks()))
            {
                // Coverage per UV winding finds overlapping UVs (mirrored halves, stacked
                // copies), which get no correction rather than a wrong one. All original sections
                // paint in one batch, so a hidden part of one can be hidden by another.
                using(new AtlasStageTimer($"paint original normals ({width}×{height})",timings))
                    Rasterize(originals,originalNormals,null,null,null,null,coverage,false,excludeHidden);
                // Tangent-space correction must be expressed in the current mesh's frame. The
                // frame-normal map covers exactly the current triangles, so it also serves as
                // the bake's coverage area.
                using(new AtlasStageTimer($"paint current frames ({width}×{height})",timings))
                    Rasterize(current,null,null,tangent,bitangent,normalBasis,coverage,true,excludeHidden);
                using(new AtlasStageTimer($"normal difference ({width}×{height})",timings))
                    correction=originalNormals.BakeNormalDifference(normalBasis,
                        new TangentBasisData { Tangent=tangent,Bitangent=bitangent,Normal=normalBasis },
                        new NormalDifferenceOptions { Coverage=coverage, MaxAngle=maxAngle },
                        out statistics);
                try
                {
                    inspect?.Invoke(new CorrectionMaps { Original=originalNormals,Tangent=tangent,Bitangent=bitangent,
                        FrameNormal=normalBasis,Correction=correction,Statistics=statistics });
                }
                catch { correction.Dispose(); throw; }
            }
            using(new AtlasStageTimer($"dilate correction ({width}×{height})",timings))
                AtlasNormalPadding.DilateAndFinalize(correction);
            return correction;
        }
        // Bounded CPU diagnostic: detects systematic coordinate mismatches without
        // allocating another full-resolution float texture. Only compare overlapping UVs.
        private static void ExportExcludedTriangles(string folder,
            IReadOnlyList<AtlasMeshSection> originals, int width, int height)
        {
            using (var mask = new NordicsTextureWrapper(width, height, Masks()))
            {
                foreach (var original in originals)
                {
                    if (original.ExcludedBlendShapeTriangles == null || original.ExcludedBlendShapeTriangles.Length == 0)
                        continue;
                    var excluded = new AtlasMeshSection {
                        UV = original.UV, Vertices = original.Vertices,
                        Normals = original.Normals, Tangents = original.Tangents,
                        Triangles = original.ExcludedBlendShapeTriangles,
                        LocalToWorld = original.LocalToWorld
                    };
                    Rasterize(excluded, null, mask, null, null, null);
                }
                AtlasDebugExporter.Coverage(folder, "ExcludedBlendShapeTriangles", mask);
            }
        }

        private static void SampleNormalAgreement(NordicsTextureWrapper original,
            NordicsTextureWrapper current, List<string> diagnostics, string material)
        {
            int step=Math.Max(1,Math.Max(original.Width,original.Height)/256);
            int samples=0, aboveOne=0, aboveTen=0;
            double sum=0, maximum=0;
            for(int y=step/2;y<original.Height;y+=step)
                for(int x=step/2;x<original.Width;x+=step)
                {
                    if(!original.IsPixelUsed(x,y)||!current.IsPixelUsed(x,y)) continue;
                    var o=original.GetPixel(x,y); var c=current.GetPixel(x,y);
                    var a=new Vector3(o.r,o.g,o.b); var b=new Vector3(c.r,c.g,c.b);
                    if(a.sqrMagnitude<1e-12f||b.sqrMagnitude<1e-12f) continue;
                    float angle=Vector3.Angle(a,b);
                    sum+=angle; maximum=Math.Max(maximum,angle); samples++;
                    if(angle>1f) aboveOne++;
                    if(angle>10f) aboveTen++;
                }
            if(samples>0) diagnostics.Add(material+": sampled world normal agreement (avatar-root space): " +
                samples+" pixels, mean="+(sum/samples).ToString("F3")+" deg, max="+
                maximum.ToString("F3")+" deg, >1 deg="+aboveOne+", >10 deg="+aboveTen+".");
        }
        private static void TryExport(Action export, List<string> diagnostics)
        {
            try { export(); }
            catch(Exception e) { diagnostics.Add("Debug map export failed: " + e.Message); }
        }
        // The original and current world normals exist only during the bake, so they're exported
        // just before they're released.
        private static void ExportGeometryDebug(string folder,Material material,NordicsTextureWrapper original,
            NordicsTextureWrapper current,List<string> diagnostics)
        {
            if (folder==null) return;
            // Export failures are debug-only and must not affect avatar processing.
            try
            {
                AtlasDebugExporter.Normal(folder,"OriginalWorldNormals",original);
                AtlasDebugExporter.Normal(folder,"CurrentWorldNormals",current);
                AtlasDebugExporter.Coverage(folder,"OriginalCoverage",original);
                AtlasDebugExporter.Heatmap(folder,original,current);
            }
            catch(Exception e) { diagnostics.Add("Debug map export failed for '"+material.name+"': "+e.Message); }
        }
        private static void ExportDebug(string folder,Material material,AtlasSubmeshBake bake,int originals,int current,List<string> diagnostics)
        {
            if (folder==null) return;
            try
            {
                AtlasDebugExporter.Normal(folder,"TangentNormalCorrection",bake.TangentNormalCorrection);
                AtlasDebugExporter.Coverage(folder,"CurrentCoverage",bake.CurrentMask);
                AtlasDebugExporter.Info(folder,material.name,folder,originals,current,
                    bake.CurrentMask.Width,bake.CurrentMask.Height);
            }
            catch(Exception e) { diagnostics.Add("Debug map export failed for '"+material.name+"': "+e.Message); }
        }
        private static AtlasSubmeshBake Missing(int width,int height,int index,string path)
        {
            return new AtlasSubmeshBake { CurrentSubmeshMissing=true,CorrectionIsFlat=true,SubmeshIndex=index,
                RendererName=path,
                CurrentMask=new NordicsTextureWrapper(width,height,Masks()),
                TangentNormalCorrection=Flat(width,height,TextureDataType.NormalTangent) };
        }
        public static int CountCurrent(Material material,IReadOnlyList<SkinnedMeshRenderer> renderers)
        {
            int count=0;
            foreach(var renderer in renderers)
            {
                if(renderer==null || renderer.sharedMesh==null) continue;
                var mats=renderer.sharedMaterials;
                for(int i=0;i<Math.Min(renderer.sharedMesh.subMeshCount,mats.Length);i++)
                    if(mats[i]==material) count++;
            }
            return count;
        }
        private static List<AtlasMeshSection> Current(Material material,IReadOnlyList<SkinnedMeshRenderer> renderers,Transform root)
        {
            var list=new List<AtlasMeshSection>();
            foreach(var renderer in renderers)
            {
                if(renderer==null || renderer.sharedMesh==null) continue;
                var mesh=renderer.sharedMesh; var mats=renderer.sharedMaterials;
                AtlasGeometryPose.Capture(renderer,root,out var v,out var n,out var t,out var poseDetails);
                var uv=mesh.uv;
                for(int i=0;i<Math.Min(mesh.subMeshCount,mats.Length);i++)
                {
                    if(mats[i]!=material) continue;
                    list.Add(new AtlasMeshSection { Material=material,
                        RendererPath=AtlasOriginalGeometryCapture.GetPath(root,renderer.transform),SubmeshIndex=i,
                        Vertices=v,Normals=n,Tangents=t,UV=uv,Triangles=mesh.GetTriangles(i),
                        RendererToAvatarRoot=(root!=null ? root.worldToLocalMatrix * renderer.localToWorldMatrix : renderer.localToWorldMatrix),
                        PoseDescription=poseDetails,
                        // Use the same avatar-root space as early Resolving capture.
                        LocalToWorld=Matrix4x4.identity });
                }
            }
            return list;
        }
        private static readonly TrianglePaintOptions Quality = new TrianglePaintOptions { Mode=TriangleRasterizationMode.Quality,SamplesPerAxis=2 };
        private static readonly TrianglePaintOptions Fast = new TrianglePaintOptions { Mode=TriangleRasterizationMode.Fast };
        // Surfaces more than this far (in avatar-root units: meters on a normally scaled avatar)
        // behind another surface with the same UVs are hidden.
        private const float HiddenMargin=0.002f;
        private static void Rasterize(AtlasMeshSection section,NordicsTextureWrapper normals,NordicsTextureWrapper mask,
            NordicsTextureWrapper tangent,NordicsTextureWrapper bitangent,NordicsTextureWrapper basisNormal,
            NordicsTextureWrapper coverage=null,bool target=false,bool hidden=false)
            => Rasterize(new[]{section},normals,mask,tangent,bitangent,basisNormal,coverage,target,hidden);
        // Each map gets the sections' triangles in one batch (PaintTriangles), painted on every
        // core or the GPU; the values are the same as painting one triangle at a time. With
        // `hidden`, surfaces hidden close behind another surface with the same UVs are left out
        // of every map but the usage mask.
        private static void Rasterize(IReadOnlyList<AtlasMeshSection> sections,NordicsTextureWrapper normals,NordicsTextureWrapper mask,
            NordicsTextureWrapper tangent,NordicsTextureWrapper bitangent,NordicsTextureWrapper basisNormal,
            NordicsTextureWrapper coverage=null,bool target=false,bool hidden=false)
        {
            bool frames=tangent!=null && bitangent!=null && basisNormal!=null;
            var uvs=new List<Vector2>();
            var normalValues=normals!=null||frames ? new List<Color>() : null;
            var coverValues=coverage!=null ? new List<Color>() : null;
            var tangentValues=frames ? new List<Color>() : null;
            var bitangentValues=frames ? new List<Color>() : null;
            var positions=hidden ? new List<Vector3>() : null;
            var surfaceNormals=hidden ? new List<Vector3>() : null;
            foreach(var section in sections)
            {
                if(section==null || section.UV==null || section.Vertices==null || section.Triangles==null) continue;
                int count=section.Vertices.Length;
                var nMat=section.LocalToWorld.inverse.transpose;
                float sign=section.LocalToWorld.determinant < 0f ? -1f : 1f;
                for(int i=0;i+2<section.Triangles.Length;i+=3)
                {
                    int a=section.Triangles[i], b=section.Triangles[i+1], c=section.Triangles[i+2];
                    if(a<0||b<0||c<0||a>=count||b>=count||c>=count ||
                        a>=section.UV.Length||b>=section.UV.Length||c>=section.UV.Length) continue;
                    var uv0=section.UV[a];var uv1=section.UV[b];var uv2=section.UV[c];
                    if(!Finite(uv0)||!Finite(uv1)||!Finite(uv2)) continue;
                    Vector3 face=Vector3.Cross(section.Vertices[b]-section.Vertices[a],section.Vertices[c]-section.Vertices[a]);
                    if(face.sqrMagnitude<1e-12f) continue;
                    face.Normalize();
                    uvs.Add(uv0);uvs.Add(uv1);uvs.Add(uv2);
                    Vector3 n0=WorldNormal(section,a,nMat,face),n1=WorldNormal(section,b,nMat,face),n2=WorldNormal(section,c,nMat,face);
                    if(normalValues!=null){normalValues.Add(Pack(n0));normalValues.Add(Pack(n1));normalValues.Add(Pack(n2));}
                    if(hidden)
                    {
                        positions.Add(section.LocalToWorld.MultiplyPoint3x4(section.Vertices[a]));
                        positions.Add(section.LocalToWorld.MultiplyPoint3x4(section.Vertices[b]));
                        positions.Add(section.LocalToWorld.MultiplyPoint3x4(section.Vertices[c]));
                        surfaceNormals.Add(n0);surfaceNormals.Add(n1);surfaceNormals.Add(n2);
                    }
                    if(coverValues!=null)
                    {
                        Color cover=NormalDifferenceOptions.CoverageColor(uv0,uv1,uv2,target);
                        coverValues.Add(cover);coverValues.Add(cover);coverValues.Add(cover);
                    }
                    if(!frames) continue;
                    Vector3 t0=Tangent(section,a,n0,sign),t1=Tangent(section,b,n1,sign),t2=Tangent(section,c,n2,sign);
                    tangentValues.Add(Pack(t0));tangentValues.Add(Pack(t1));tangentValues.Add(Pack(t2));
                    // Reconstruct bitangent using original handedness (includes mirrored transforms).
                    bitangentValues.Add(Pack(Bitangent(section,a,n0,t0,sign)));
                    bitangentValues.Add(Pack(Bitangent(section,b,n1,t1,sign)));
                    bitangentValues.Add(Pack(Bitangent(section,c,n2,t2,sign)));
                }
            }
            var depth=hidden ? new HiddenSurfaceOptions { Positions=positions, Normals=surfaceNormals, Margin=HiddenMargin } : null;
            normals?.PaintTriangles(uvs,normalValues,Quality,depth);
            if(mask!=null)
            {
                var white=new Color[uvs.Count];
                for(int i=0;i<white.Length;i++) white[i]=Color.white;
                mask.PaintTriangles(uvs,white,Fast);
            }
            coverage?.PaintTriangles(uvs,coverValues,Quality,depth);
            if(!frames) return;
            tangent.PaintTriangles(uvs,tangentValues,Quality,depth);
            bitangent.PaintTriangles(uvs,bitangentValues,Quality,depth);
            basisNormal.PaintTriangles(uvs,normalValues,Quality,depth);
        }
        private static bool Finite(Vector2 uv) => !float.IsNaN(uv.x) && !float.IsNaN(uv.y) && !float.IsInfinity(uv.x) && !float.IsInfinity(uv.y);
        private static Color Pack(Vector3 v) => new Color(v.x,v.y,v.z,1);
        private static Vector3 WorldNormal(AtlasMeshSection m,int index,Matrix4x4 normalMatrix,Vector3 fallback)
        {
            Vector3 n=m.Normals!=null && index<m.Normals.Length && m.Normals[index].sqrMagnitude>1e-10f ? m.Normals[index] : fallback;
            n=normalMatrix.MultiplyVector(n);
            return n.sqrMagnitude>1e-10f ? n.normalized : Vector3.forward;
        }
        private static Vector3 Tangent(AtlasMeshSection m,int index,Vector3 n,float sign)
        {
            Vector3 t=m.Tangents!=null && index<m.Tangents.Length ? new Vector3(m.Tangents[index].x,m.Tangents[index].y,m.Tangents[index].z) : Vector3.right;
            t=m.LocalToWorld.MultiplyVector(t);
            t-=n*Vector3.Dot(n,t);
            if(t.sqrMagnitude<1e-10f) t=Vector3.Cross(n,Mathf.Abs(n.y)<0.99f?Vector3.up:Vector3.right);
            return t.normalized;
        }
        private static Vector3 Bitangent(AtlasMeshSection m,int index,Vector3 n,Vector3 t,float sign)
        {
            float hand=m.Tangents!=null && index<m.Tangents.Length ? Mathf.Sign(m.Tangents[index].w) : 1f;
            return Vector3.Cross(n,t).normalized * hand * sign;
        }
    }
}
