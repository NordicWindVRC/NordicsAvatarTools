using System;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// Evaluate mesh attributes in the avatar-root coordinate system at each NDMF pass.
    /// Skinned meshes need bone * bindpose transformations; renderer.localToWorld alone
    /// does not transform a bind-pose normal into its actual pose. No AAO dependency.
    /// </summary>
    internal static class AtlasGeometryPose
    {
        internal static void Capture(SkinnedMeshRenderer renderer, Transform root,
            out Vector3[] vertices, out Vector3[] normals, out Vector4[] tangents,
            out string details)
        {
            var mesh = renderer.sharedMesh;
            vertices = mesh.vertices;
            normals = mesh.normals;
            tangents = mesh.tangents;
            var toRoot = root != null ? root.worldToLocalMatrix : Matrix4x4.identity;
            var rigid = toRoot * renderer.localToWorldMatrix;
            var binds = mesh.bindposes;
            var bones = renderer.bones;
            bool skin = binds != null && binds.Length != 0 && bones != null && bones.Length != 0 &&
                        mesh.vertexCount == vertices.Length;
            if (skin)
            {
                // The all-influences API is required for 8-weight skinned meshes. Do not
                // truncate to mesh.boneWeights (which contains at most four influences).
                using (var counts = mesh.GetBonesPerVertex())
                using (var weights = mesh.GetAllBoneWeights())
                {
                    int total = 0;
                    for (int i = 0; i < counts.Length; i++) total += counts[i];
                    if (counts.Length != vertices.Length || total != weights.Length)
                    {
                        skin = false;
                    }
                    else
                    {
                        var transforms = new Matrix4x4[binds.Length];
                        var normalMatrices = new Matrix4x4[binds.Length];
                        var valid = new bool[binds.Length];
                        int available = 0;
                        for (int i=0;i<binds.Length;i++)
                        {
                            if (i >= bones.Length || bones[i] == null) continue;
                            transforms[i] = toRoot * bones[i].localToWorldMatrix * binds[i];
                            normalMatrices[i] = transforms[i].inverse.transpose;
                            valid[i] = true;
                            available++;
                        }
                        if (available == 0) skin = false;
                        else
                        {
                            var posedVertices = new Vector3[vertices.Length];
                            var posedNormals = normals.Length == vertices.Length ? new Vector3[vertices.Length] : normals;
                            var posedTangents = tangents.Length == vertices.Length ? new Vector4[vertices.Length] : tangents;
                            int cursor = 0, invalid = 0;
                            var fallbackNormal = rigid.inverse.transpose;
                            for (int v=0;v<vertices.Length;v++)
                            {
                                Vector3 p=Vector3.zero, n=Vector3.zero, t=Vector3.zero;
                                float sum=0f, handed=1f, strongest=-1f;
                                for (int j=0;j<counts[v];j++)
                                {
                                    var bw=weights[cursor++];
                                    int b=bw.boneIndex;
                                    if (bw.weight <= 0f || b<0 || b>=valid.Length || !valid[b]) continue;
                                    float w=bw.weight;
                                    p+=transforms[b].MultiplyPoint3x4(vertices[v])*w;
                                    if (posedNormals.Length==vertices.Length)
                                        n+=normalMatrices[b].MultiplyVector(normals[v])*w;
                                    if (posedTangents.Length==vertices.Length)
                                        t+=transforms[b].MultiplyVector(new Vector3(tangents[v].x,tangents[v].y,tangents[v].z))*w;
                                    sum+=w;
                                    if (w>strongest)
                                    {
                                        strongest=w;
                                        handed=transforms[b].determinant<0f ? -1f : 1f;
                                    }
                                }
                                if (sum>1e-8f)
                                {
                                    posedVertices[v]=p/sum;
                                    if(posedNormals.Length==vertices.Length)
                                        posedNormals[v]=n.sqrMagnitude>1e-12f?n.normalized:Vector3.forward;
                                    if(posedTangents.Length==vertices.Length)
                                    {
                                        if (posedNormals.Length==vertices.Length) t-=posedNormals[v]*Vector3.Dot(posedNormals[v],t);
                                        t=t.sqrMagnitude>1e-12f?t.normalized:Vector3.right;
                                        posedTangents[v]=new Vector4(t.x,t.y,t.z,tangents[v].w*handed);
                                    }
                                }
                                else
                                {
                                    invalid++;
                                    posedVertices[v]=rigid.MultiplyPoint3x4(vertices[v]);
                                    if(posedNormals.Length==vertices.Length)
                                        posedNormals[v]=fallbackNormal.MultiplyVector(normals[v]).normalized;
                                    if(posedTangents.Length==vertices.Length)
                                    {
                                        var ft=rigid.MultiplyVector(new Vector3(tangents[v].x,tangents[v].y,tangents[v].z)).normalized;
                                        posedTangents[v]=new Vector4(ft.x,ft.y,ft.z,tangents[v].w*(rigid.determinant<0?-1f:1f));
                                    }
                                }
                            }
                            vertices=posedVertices;
                            normals=posedNormals;
                            tangents=posedTangents;
                            details="skinned pose; bones="+available+"/"+binds.Length+", vertices="+vertices.Length+", fallback vertices="+invalid;
                            return;
                        }
                    }
                }
            }
            // Non-skinned or incomplete skinning data: transform mesh-local data with
            // the renderer transform, still into the same avatar-root coordinate space.
            var nm=rigid.inverse.transpose;
            var vOut=new Vector3[vertices.Length];
            for(int i=0;i<vertices.Length;i++) vOut[i]=rigid.MultiplyPoint3x4(vertices[i]);
            vertices=vOut;
            if(normals.Length==vertices.Length)
            {
                var nOut=new Vector3[normals.Length];
                for(int i=0;i<nOut.Length;i++) nOut[i]=nm.MultiplyVector(normals[i]).normalized;
                normals=nOut;
            }
            if(tangents.Length==vertices.Length)
            {
                var tOut=new Vector4[tangents.Length];
                float sign=rigid.determinant<0?-1f:1f;
                for(int i=0;i<tOut.Length;i++)
                {
                    var t=rigid.MultiplyVector(new Vector3(tangents[i].x,tangents[i].y,tangents[i].z)).normalized;
                    tOut[i]=new Vector4(t.x,t.y,t.z,tangents[i].w*sign);
                }
                tangents=tOut;
            }
            details=(skin?"incomplete skinning data, renderer fallback":"renderer transform")+
                    "; vertices="+vertices.Length+", determinant="+rigid.determinant.ToString("G5");
        }
    }
}
