using System;
using System.Collections.Generic;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Captured BEFORE AAO modifies renderer/submesh geometry.
    // Arrays are detached from Mesh so AAO may destroy/rebuild its inputs safely.
    public sealed class AtlasMeshSection
    {
        public Material Material;
        public string RendererPath;
        public int SubmeshIndex;
        public Vector3[] Vertices, Normals;
        public Vector4[] Tangents;
        public Vector2[] UV;
        public int[] Triangles;
        // Removed triangles for optional debug rendering; never submitted to original normals.
        public int[] ExcludedBlendShapeTriangles;
        public Matrix4x4 LocalToWorld;
        public Matrix4x4 RendererToAvatarRoot;
        public string PoseDescription;
    }
    public sealed class AtlasOriginalGeometry
    {
        public readonly Dictionary<Material,List<AtlasMeshSection>> Sections =
            new Dictionary<Material,List<AtlasMeshSection>>();
        public readonly HashSet<Material> DetectedMaterials = new HashSet<Material>();
        // Keyed by the configured Atlaser/output/source slot instead of Material instance.
        // The lists share the detached mesh section arrays; no additional geometry copies.
        public readonly Dictionary<string,List<AtlasMeshSection>> SourceSections =
            new Dictionary<string,List<AtlasMeshSection>>();
        public static string SourceKey(int atlaserIndex,int outputIndex,int sourceIndex) =>
            atlaserIndex + ":" + outputIndex + ":" + sourceIndex;
        public readonly List<string> CaptureDiagnostics = new List<string>();
        public int SourceRendererCount;
        public int CapturedSectionCount;
        public int ConfiguredMaterialCount;
    }
    // Builds a detached geometry snapshot. Its lifetime is owned by NDMF BuildContext state.
    // There is deliberately no static per-avatar cache or GameObject-keyed storage.
    public static class AtlasOriginalGeometryCapture
    {
        public static AtlasOriginalGeometry Capture(GameObject avatarRoot)
        {
            var snapshot = new AtlasOriginalGeometry();
            if (avatarRoot == null) { snapshot.CaptureDiagnostics.Add("No avatar root; capture skipped."); return snapshot; }
            int atlaserCount = 0;
            var atlasers = avatarRoot.GetComponentsInChildren<NordicsAdvancedAtlaser>(true);
            for (int atlaserIndex=0; atlaserIndex<atlasers.Length; atlaserIndex++)
            {
                var atlaser = atlasers[atlaserIndex];
                if (!atlaser.EnableGeometryBaking) continue;
                atlaserCount++;
                // Keep the same source selection used by the main Atlaser inspector:
                // AAO Merge Skinned Mesh inputs, or the local renderer otherwise.
                var discovery = AtlasMaterialDiscovery.Discover(atlaser.gameObject);
                foreach (var material in discovery.Materials) snapshot.DetectedMaterials.Add(material);
                if (!string.IsNullOrEmpty(discovery.Warning))
                    snapshot.CaptureDiagnostics.Add(atlaser.name + ": " + discovery.Warning);
                if (atlaser.IgnoreGeometryAffectedByBlendShape && atlaser.ExcludedBlendShapes != null)
                    foreach (var excluded in atlaser.ExcludedBlendShapes)
                        if (excluded != null && excluded.Renderer != null &&
                            !discovery.Renderers.Contains(excluded.Renderer))
                            snapshot.CaptureDiagnostics.Add(atlaser.name + ": excluded blend shape '" +
                                excluded.BlendShapeName + "' targets renderer '" + excluded.Renderer.name +
                                "' outside the discovered source renderer set; ignored.");
                var assigned = new HashSet<Material>();
                foreach (var output in atlaser.GetComponents<NordicsAtlasOutput>())
                {
                    if (output.Configuration.Sources == null) continue;
                    foreach (var item in output.Configuration.Sources)
                        if (item != null && item.Material != null) assigned.Add(item.Material);
                }
                snapshot.ConfiguredMaterialCount += assigned.Count;
                snapshot.SourceRendererCount += discovery.Renderers.Count;
                int before = snapshot.CapturedSectionCount;
                var localSections = new Dictionary<Material,List<AtlasMeshSection>>();
                foreach (var renderer in discovery.Renderers)
                {
                    if (renderer == null) { snapshot.CaptureDiagnostics.Add(atlaser.name + ": null source renderer."); continue; }
                    if (renderer.sharedMesh == null) { snapshot.CaptureDiagnostics.Add(atlaser.name + ": source renderer " + renderer.name + " has no mesh."); continue; }
                    CaptureRenderer(renderer, avatarRoot.transform, assigned, atlaser, snapshot, localSections);
                }
                snapshot.CaptureDiagnostics.Add(atlaser.name + ": " + (discovery.HasAAO ? "AAO merge sources" : "local renderer")
                    + "; renderers=" + discovery.Renderers.Count + ", configured materials=" + assigned.Count
                    + ", captured sections=" + (snapshot.CapturedSectionCount-before) + ".");
                // Bind each configured source slot to the already-captured material union.
                // Later processors may clone or replace the Material object; the serialized
                // Atlaser/source ordering is the identity, not Unity reference equality.
                var outputs = atlaser.GetComponents<NordicsAtlasOutput>();
                for (int oi=0; oi<outputs.Length; oi++)
                {
                    var sources = outputs[oi].Configuration.Sources;
                    if (sources == null) continue;
                    for (int si=0; si<sources.Count; si++)
                    {
                        var source = sources[si];
                        if (source == null || source.Material == null) continue;
                        List<AtlasMeshSection> sections;
                        if (localSections.TryGetValue(source.Material,out sections) && sections.Count>0)
                            snapshot.SourceSections[AtlasOriginalGeometry.SourceKey(atlaserIndex,oi,si)] = sections;
                    }
                }
                foreach (var material in assigned)
                {
                    List<AtlasMeshSection> sections;
                    if (!localSections.TryGetValue(material, out sections) || sections.Count == 0)
                        snapshot.CaptureDiagnostics.Add(atlaser.name + ": NOT CAPTURED material '" + material.name
                            + "' (" + (discovery.Materials.Contains(material) ? "material discovered, but no matching mesh submesh" : "material not on discovered renderers") + ").");
                }
            }
            if (atlaserCount == 0) snapshot.CaptureDiagnostics.Add("No geometry-enabled Atlaser components found at early capture.");
            return snapshot;
        }

        private static void CaptureRenderer(SkinnedMeshRenderer renderer, Transform root, HashSet<Material> assigned, NordicsAdvancedAtlaser atlaser, AtlasOriginalGeometry snapshot,
            Dictionary<Material,List<AtlasMeshSection>> localSections)
        {
            if (renderer == null || renderer.sharedMesh == null) return;
            var mesh = renderer.sharedMesh;
            var mats = renderer.sharedMaterials;
            AtlasGeometryPose.Capture(renderer, root, out var vertices, out var normals, out var tangents, out var poseDetails);
            var uv = mesh.uv;
            int removed = 0;
            snapshot.CaptureDiagnostics.Add("Renderer " + GetPath(root, renderer.transform) + ": " + poseDetails);
            string path = GetPath(root,renderer.transform);
            int count = Math.Min(mesh.subMeshCount,mats.Length);
            for (int i=0; i<count; i++)
            {
                var material=mats[i];
                if (material == null || !assigned.Contains(material)) continue;
                List<AtlasMeshSection> list;
                if (!snapshot.Sections.TryGetValue(material,out list))
                    snapshot.Sections[material]=list=new List<AtlasMeshSection>();
                int[] kept, excluded;
                int[] triangles = mesh.GetTriangles(i);
                var excludedFlags = AtlasBlendShapeExclusion.BuildExcludedTriangles(mesh, renderer, atlaser, triangles, snapshot.CaptureDiagnostics);
                AtlasBlendShapeExclusion.SplitTriangles(triangles, excludedFlags, out kept, out excluded);
                removed += excluded.Length / 3;
                var section=new AtlasMeshSection { Material=material, RendererPath=path, SubmeshIndex=i,
                    Vertices=vertices, Normals=normals, Tangents=tangents, UV=uv,
                    RendererToAvatarRoot=(root != null ? root.worldToLocalMatrix * renderer.localToWorldMatrix : renderer.localToWorldMatrix),
                    PoseDescription=poseDetails,
                    Triangles=kept, ExcludedBlendShapeTriangles=excluded,
                    // Snapshot in avatar-root space. Absolute world orientation can change between NDMF phases.
                    LocalToWorld=Matrix4x4.identity };
                list.Add(section);
                List<AtlasMeshSection> localList;
                if(!localSections.TryGetValue(material,out localList))
                    localSections[material]=localList=new List<AtlasMeshSection>();
                localList.Add(section);
                snapshot.CapturedSectionCount++;
            }
            if (removed > 0)
                snapshot.CaptureDiagnostics.Add("Blend shape exclusion [" + path + "]: excluded triangles=" + removed + ".");
        }
        public static string GetPath(Transform root,Transform t)
        {
            if (root == null || t == null) return "";
            var segments = new List<string>();
            while (t != null && t != root) { segments.Add(t.name); t=t.parent; }
            segments.Reverse(); return string.Join("/",segments.ToArray());
        }
    }
}
