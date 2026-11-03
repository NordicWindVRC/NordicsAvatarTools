using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace NordicWind.AvatarStatistics.Editor
{
    // Runs on NDMF's build clone. No changes to Unity assets or components.
    internal static class AvatarPerformanceCollector
    {
        private const double MiB = 1048576.0;
        internal static void Collect(GameObject root, AvatarStatisticsReport report)
        {
            var components = root.GetComponentsInChildren<Component>(true);
            var bounds = new Bounds();
            bool hasBounds = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                // VRChat excludes line and trail renderer bounds.
                if (r is LineRenderer || r is TrailRenderer) continue;
                if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                else bounds.Encapsulate(r.bounds);
            }
            var size = hasBounds ? bounds.size : Vector3.zero;
            var bones = new HashSet<Transform>();
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (var b in skin.bones) if (b != null) bones.Add(b);
            // Explicitly a rig-bone approximation; SDK bone counting can differ.
            int pbCount = 0, pbColliderCount = 0, contactCount = 0, affected = 0;

            bool affectedAccurate = true, contactAccurate = true;
            foreach (var c in components)
            {
                if (c == null) continue;
                var name = c.GetType().Name;
                if (name == "VRCPhysBone")
                {
                    pbCount++;
                    try
                    {
                        var rootBone = GetMember(c, "rootTransform") as Transform ?? c.transform;
                        var ignored = GetMember(c, "ignoreTransforms") as IEnumerable;
                        var exclusions = new HashSet<Transform>();
                        if (ignored != null) foreach (var item in ignored) if (item is Transform t) exclusions.Add(t);
                        int componentCount = CountChain(rootBone, exclusions);
                        affected += componentCount;

                    }
                    catch (Exception) { affectedAccurate = false; }
                }
                else if (name == "VRCPhysBoneCollider") pbColliderCount++;
                else if (name == "VRCContactSender") contactCount++;
                else if (name == "VRCContactReceiver")
                {
                    try
                    {
                        var local = GetMember(c, "localOnly");
                        if (local is bool b) { if (!b) contactCount++; }
                        else { contactCount++; contactAccurate = false; }
                    }
                    catch { contactCount++; contactAccurate = false; }
                }
            }
            var metrics = report.Performance;
            Add(metrics,"Triangles",report.TriangleCount,new[]{32000d,70000,70000,70000},new[]{7500d,10000,15000,20000});
            // Bounds ranks are dimension-specific, not scalar. Both PC and Quest use
            // the same maxima, with Poor and Medium sharing the upper bound.
            metrics.Add(new PerformanceMetric {
                Name = "Bounds",
                Display = string.Format("{0:F2} × {1:F2} × {2:F2} m", size.x, size.y, size.z),
                IsBounds = true, BoundsX = size.x, BoundsY = size.y, BoundsZ = size.z,
                Note = "Renderer bounds estimate; not guaranteed to match VRChat bounds exactly."
            });
            var textureMetric = new PerformanceMetric {
                Name = "Texture Memory", Value = report.GpuTextureBytes / MiB,
                Display = AvatarStatisticsInfo.MiB(report.GpuTextureBytes),
                PcValue = report.PcGpuTextureBytes / MiB, QuestValue = report.QuestGpuTextureBytes / MiB,
                PcDisplay = AvatarStatisticsInfo.MiB(report.PcGpuTextureBytes),
                QuestDisplay = AvatarStatisticsInfo.MiB(report.QuestGpuTextureBytes),
                Pc = new[]{40d,75,110,150}, Quest = new[]{10d,18,25,40},
                Note = "Platform GPU-format sizes calculated from importer settings. VRChat may report slightly higher usage due to differences in texture accounting."
            };
            if (report.PcTextureFallbacks > 0 || report.QuestTextureFallbacks > 0)
                textureMetric.Note += string.Format(" Fallbacks using current imported sizes: PC {0}, Quest {1} texture(s).", report.PcTextureFallbacks, report.QuestTextureFallbacks);
            metrics.Add(textureMetric);
            Add(metrics,"Skinned Meshes",report.SkinnedCount,new[]{1d,2,8,16},new[]{1d,1,2,2});
            Add(metrics,"Basic Meshes",report.RegularCount,new[]{4d,8,16,24},new[]{1d,1,2,2});
            Add(metrics,"Material Slots",report.MaterialSlots,new[]{4d,8,16,32},new[]{1d,1,2,4},note:"All render slots, including duplicate and empty material references.");
            const string pbNote = "VRChat may incorrectly count components removed during the build, causing its reported value to be higher than the final avatar count.";
            Add(metrics,"PhysBones Components",pbCount,new[]{4d,8,16,32},new[]{0d,4,6,8},note:pbNote);
            Add(metrics,"PhysBones Affected Transforms",affected,new[]{16d,64,128,256},new[]{0d,16,32,64},note:pbNote,available:affectedAccurate);
            Add(metrics,"PhysBones Colliders",pbColliderCount,new[]{4d,8,16,32},new[]{0d,4,8,16});
            Add(metrics,"Contacts",contactCount,new[]{8d,16,24,32},new[]{2d,4,8,16},note:contactAccurate?null:"Contact receiver Local Only setting could not be read reliably.",available:contactAccurate);
            Add(metrics,"Animators",Math.Max(1,root.GetComponentsInChildren<Animator>(true).Length),new[]{1d,4,16,32},new[]{1d,1,1,2});
            Add(metrics,"Bones",bones.Count,new[]{75d,150,256,400},new[]{75d,90,150,150},available:true);
            Add(metrics,"Lights",root.GetComponentsInChildren<Light>(true).Length,new[]{0d,0,0,1},null,questApplicable:false);
            Add(metrics,"Particle Systems",root.GetComponentsInChildren<ParticleSystem>(true).Length,new[]{0d,4,8,16},new[]{0d,0,0,2});
            Add(metrics,"Audio Sources",root.GetComponentsInChildren<AudioSource>(true).Length,new[]{1d,4,8,8},null,questApplicable:false,note:"Audio Sources are removed on mobile.");
            // Remaining VRChat performance categories, including disabled/inactive objects.
            var particles = root.GetComponentsInChildren<ParticleSystem>(true);
            long maxParticles = 0, meshParticleTriangles = 0;
            bool trailsEnabled = false, collisionsEnabled = false;
            foreach (var ps in particles)
            {
                int maximum = Math.Max(0, ps.main.maxParticles);
                maxParticles += maximum;
                trailsEnabled |= ps.trails.enabled;
                collisionsEnabled |= ps.collision.enabled;
                var pr = ps.GetComponent<ParticleSystemRenderer>();
                if (pr != null && pr.renderMode == ParticleSystemRenderMode.Mesh)
                {
                    // Multiple mesh choices are supported; conservative maximum per emitted particle.
                    var meshes = new Mesh[pr.meshCount];
                    int n = pr.GetMeshes(meshes);
                    int maxTriangles = 0;
                    for (int i = 0; i < n; i++)
                    {
                        var mesh = meshes[i];
                        if (mesh == null) continue;
                        int tris = 0;
                        for (int j = 0; j < mesh.subMeshCount; j++)
                            if (mesh.GetTopology(j) == MeshTopology.Triangles) tris += (int)(mesh.GetIndexCount(j) / 3);
                        maxTriangles = Math.Max(maxTriangles, tris);
                    }
                    meshParticleTriangles += (long)maximum * maxTriangles;
                }
            }
            Add(metrics,"Total Particles Active",maxParticles,new[]{0d,300,1000,2500},new[]{0d,0,0,200});
            Add(metrics,"Mesh Particle Active Polys",meshParticleTriangles,new[]{0d,1000,2000,5000},new[]{0d,0,0,400},note:"Estimated from particle maximum capacity and the largest assigned particle mesh.");
            Add(metrics,"Particle Trails Enabled",trailsEnabled?1:0,new[]{0d,0,1,1},new[]{0d,0,0,1},trailsEnabled?"True":"False");
            Add(metrics,"Particle Collision Enabled",collisionsEnabled?1:0,new[]{0d,0,1,1},new[]{0d,0,0,1},collisionsEnabled?"True":"False");
            Add(metrics,"Trail Renderers",root.GetComponentsInChildren<TrailRenderer>(true).Length,new[]{1d,2,4,8},new[]{0d,0,0,1});
            Add(metrics,"Line Renderers",root.GetComponentsInChildren<LineRenderer>(true).Length,new[]{1d,2,4,8},new[]{0d,0,0,1});
            int raycastCount=0, physicsColliders=0, rigidbodies=root.GetComponentsInChildren<Rigidbody>(true).Length;
            int constraintCount=0;
            foreach (var c in components)
            {
                if (c == null) continue;
                string n=c.GetType().Name;
                if (n == "VRCRaycast") raycastCount++;
                if (c is Collider) physicsColliders++;
                if (c is UnityEngine.Animations.IConstraint || IsVrcConstraint(n)) constraintCount++;
            }
            Add(metrics,"Raycasts",raycastCount,new[]{1d,4,8,15},new[]{1d,2,4,8});
            Add(metrics,"Physics Colliders",physicsColliders,new[]{0d,1,8,8},null,questApplicable:false);
            Add(metrics,"Physics Rigidbodies",rigidbodies,new[]{0d,1,8,8},null,questApplicable:false);
            var cloths=root.GetComponentsInChildren<Cloth>(true);
            long clothVertices=0;
            foreach (var cloth in cloths) { var skin=cloth.GetComponent<SkinnedMeshRenderer>(); if (skin != null && skin.sharedMesh != null) clothVertices += skin.sharedMesh.vertexCount; }
            Add(metrics,"Cloths",cloths.Length,new[]{0d,1,1,1},null,questApplicable:false);
            Add(metrics,"Total Cloth Vertices",clothVertices,new[]{0d,50,100,200},null,questApplicable:false);
            // VRChat owns these calculations. Do not fall back to local estimates.
            // Other established Nordic metrics (including full index-buffer triangles
            // and cross-platform texture memory) are deliberately unchanged.
            OfficialSdkPerformanceCollector.Apply(root, report);


        }
        private static void Add(List<PerformanceMetric> list,string name,double value,double[] pc,double[] quest,string display=null,string note=null,bool available=true,bool questApplicable=true)
        { list.Add(new PerformanceMetric{Name=name,Value=value,Display=display??value.ToString("N0"),Pc=pc,Quest=quest,Note=note,Available=available,QuestApplicable=questApplicable}); }
        private static object GetMember(object source,string name)
        {
            if (source == null) return null;
            var type=source.GetType(); const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
            var f=type.GetField(name,flags); if(f!=null) return f.GetValue(source);
            var p=type.GetProperty(name,flags); return p!=null?p.GetValue(source,null):null;
        }
        private static bool IsVrcConstraint(string name)
        {
            return name == "VRCAimConstraint" || name == "VRCLookAtConstraint" || name == "VRCParentConstraint" ||
                   name == "VRCPositionConstraint" || name == "VRCRotationConstraint" || name == "VRCScaleConstraint";
        }
        private static int CountChain(Transform root,HashSet<Transform> excluded)
        {
            if(root==null || excluded.Contains(root)) return 0;
            int n=1;
            foreach(Transform child in root) n+=CountChain(child,excluded);
            return n;
        }
    }
}
