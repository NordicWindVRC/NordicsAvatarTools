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
            Transform avatarRoot, List<string> diagnostics, string debugFolder = null)
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
                bake.CurrentMask=new NordicsTextureWrapper(width,height,Masks());
                bake.CurrentWorldNormals=new NordicsTextureWrapper(width,height,Normals(TextureDataType.NormalWorld));
                Rasterize(section,bake.CurrentWorldNormals,bake.CurrentMask,null,null,null);
                if(originalSections==null || originalSections.Count==0)
                {
                    bake.OriginalWorldNormals=Flat(width,height,TextureDataType.NormalWorld);
                    bake.TangentNormalCorrection=Flat(width,height,TextureDataType.NormalTangent);
                    diagnostics.Add(material.name+": original source geometry unavailable; tangent correction is flat.");
                }
                else
                {
                    bake.OriginalWorldNormals=new NordicsTextureWrapper(width,height,Normals(TextureDataType.NormalWorld));
                    using(var tangent=new NordicsTextureWrapper(width,height,Normals(TextureDataType.NormalWorld)))
                    using(var bitangent=new NordicsTextureWrapper(width,height,Normals(TextureDataType.NormalWorld)))
                    using(var normalBasis=new NordicsTextureWrapper(width,height,Normals(TextureDataType.NormalWorld)))
                    {
                        foreach(var src in originalSections)
                            Rasterize(src,bake.OriginalWorldNormals,null,null,null,null);
                        // Export excluded original triangles separately; they must never
                        // contribute to the original normal or current-coverage textures.
                        if (debugFolder != null && originalSections.Any(s =>
                            s.ExcludedBlendShapeTriangles != null && s.ExcludedBlendShapeTriangles.Length > 0))
                        {
                            TryExport(() => ExportExcludedTriangles(debugFolder, originalSections, width, height), diagnostics);
                        }
                        // Tangent-space correction must be expressed in the surviving mesh basis.
                        Rasterize(section,null,null,tangent,bitangent,normalBasis);
                        // The bake inverts the frame exactly as interpolated, so it must run
                        // before the debug export, which normalizes the maps in place.
                        bake.TangentNormalCorrection=bake.OriginalWorldNormals.BakeNormalDifference(
                            bake.CurrentWorldNormals,
                            new TangentBasisData { Tangent=tangent,Bitangent=bitangent,Normal=normalBasis });
                        if(debugFolder!=null)
                        {
                            TryExport(() => AtlasDebugExporter.Normal(debugFolder,"TangentBasis_Tangent",tangent),diagnostics);
                            TryExport(() => AtlasDebugExporter.Normal(debugFolder,"TangentBasis_Bitangent",bitangent),diagnostics);
                            TryExport(() => AtlasDebugExporter.Normal(debugFolder,"TangentBasis_Normal",normalBasis),diagnostics);
                        }
                        // Preserve the raw correction for debug inspection before padding.
                        // Both coverage masks remain untouched by padding.
                        if (debugFolder != null)
                            TryExport(() => AtlasDebugExporter.Normal(debugFolder,
                                "TangentNormalCorrection_PreDilation", bake.TangentNormalCorrection), diagnostics);
                        AtlasNormalPadding.DilateAndFinalize(bake.TangentNormalCorrection);
                        diagnostics.Add(material.name+": full correction dilation completed; original coverage unchanged.");
                    }
                    diagnostics.Add(material.name+": combined "+originalSections.Count+
                        " original section(s) into one material bake; current submeshes=1.");
                }
                if (originalSections!=null && originalSections.Count>0)
                    SampleNormalAgreement(bake.OriginalWorldNormals,bake.CurrentWorldNormals,diagnostics,material.name);
                ExportDebug(debugFolder,material,bake,originalSections==null?0:originalSections.Count,current.Count,diagnostics);
                return result;
            }
            catch { result.Dispose(); throw; }
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
        private static void ExportDebug(string folder,Material material,AtlasSubmeshBake bake,int originals,int current,List<string> diagnostics)
        {
            if (folder==null) return;
            // Export failures are debug-only and must not affect avatar processing.
            try
            {
                AtlasDebugExporter.Normal(folder,"OriginalWorldNormals",bake.OriginalWorldNormals);
                AtlasDebugExporter.Normal(folder,"CurrentWorldNormals",bake.CurrentWorldNormals);
                AtlasDebugExporter.Normal(folder,"TangentNormalCorrection",bake.TangentNormalCorrection);
                AtlasDebugExporter.Coverage(folder,"OriginalCoverage",bake.OriginalWorldNormals);
                AtlasDebugExporter.Coverage(folder,"CurrentCoverage",bake.CurrentMask);
                AtlasDebugExporter.Heatmap(folder,bake.OriginalWorldNormals,bake.CurrentWorldNormals);
                AtlasDebugExporter.Info(folder,material.name,folder,originals,current,
                    bake.CurrentMask.Width,bake.CurrentMask.Height);
            }
            catch(Exception e) { diagnostics.Add("Debug map export failed for '"+material.name+"': "+e.Message); }
        }
        private static AtlasSubmeshBake Missing(int width,int height,int index,string path)
        {
            return new AtlasSubmeshBake { CurrentSubmeshMissing=true,SubmeshIndex=index,
                RendererName=path,
                CurrentMask=new NordicsTextureWrapper(width,height,Masks()),
                OriginalWorldNormals=Flat(width,height,TextureDataType.NormalWorld),
                CurrentWorldNormals=Flat(width,height,TextureDataType.NormalWorld),
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
        private static void Rasterize(AtlasMeshSection section,NordicsTextureWrapper normals,NordicsTextureWrapper mask,
            NordicsTextureWrapper tangent,NordicsTextureWrapper bitangent,NordicsTextureWrapper basisNormal)
        {
            if(section.UV==null || section.Vertices==null || section.Triangles==null) return;
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
                var n0=WorldNormal(section,a,nMat,face);var n1=WorldNormal(section,b,nMat,face);var n2=WorldNormal(section,c,nMat,face);
                normals?.PaintTriangle(uv0,uv1,uv2,Pack(n0),Pack(n1),Pack(n2),Quality);
                mask?.PaintTriangle(uv0,uv1,uv2,Color.white,Color.white,Color.white,Fast);
                if(tangent==null || bitangent==null || basisNormal==null) continue;
                Vector3 t0=Tangent(section,a,n0,sign),t1=Tangent(section,b,n1,sign),t2=Tangent(section,c,n2,sign);
                tangent.PaintTriangle(uv0,uv1,uv2,Pack(t0),Pack(t1),Pack(t2),Quality);
                // Reconstruct bitangent using original handedness (includes mirrored transforms).
                bitangent.PaintTriangle(uv0,uv1,uv2,Pack(Bitangent(section,a,n0,t0,sign)),Pack(Bitangent(section,b,n1,t1,sign)),Pack(Bitangent(section,c,n2,t2,sign)),Quality);
                basisNormal.PaintTriangle(uv0,uv1,uv2,Pack(n0),Pack(n1),Pack(n2),Quality);
            }
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
