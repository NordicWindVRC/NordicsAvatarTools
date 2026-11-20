using System;
using System.Collections.Generic;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// Finds out whether the surviving mesh shades exactly like the original, so the normal
    /// correction would be flat and its bake can be skipped.
    ///
    /// That is the case when every surviving triangle is an original triangle with unchanged
    /// corners: removed submeshes or triangles (for example a mesh cut along existing edges)
    /// keep the rest identical. Vertices that are unchanged aren't enough on their own: a
    /// reducer that removes vertices builds new, larger triangles from the remaining ones,
    /// and those shade differently.
    ///
    /// Corners are compared in avatar-root space after rounding (0.1 mm for positions, 0.001
    /// for directions, 0.00001 for UVs). A value that changed only by rounding noise may still
    /// land on the other side of a rounding step; then the bake simply runs. Either outcome is
    /// safe: a flat correction never makes shading worse.
    /// </summary>
    public static class AtlasGeometryComparison
    {
        private const float PositionStep = 1e-4f, DirectionStep = 1e-3f, UVStep = 1e-5f;

        private readonly struct Corner : IEquatable<Corner>
        {
            private readonly int px, py, pz, nx, ny, nz, tx, ty, tz, tw, u, v;
            public Corner(Vector3 p, Vector3 n, Vector4 t, Vector2 uv)
            {
                px = Round(p.x, PositionStep); py = Round(p.y, PositionStep); pz = Round(p.z, PositionStep);
                nx = Round(n.x, DirectionStep); ny = Round(n.y, DirectionStep); nz = Round(n.z, DirectionStep);
                tx = Round(t.x, DirectionStep); ty = Round(t.y, DirectionStep); tz = Round(t.z, DirectionStep);
                tw = t.w < 0f ? -1 : 1;
                u = Round(uv.x, UVStep); v = Round(uv.y, UVStep);
            }
            private static int Round(float value, float step) => Mathf.RoundToInt(value / step);
            public bool Equals(Corner o) => px == o.px && py == o.py && pz == o.pz && nx == o.nx && ny == o.ny && nz == o.nz &&
                tx == o.tx && ty == o.ty && tz == o.tz && tw == o.tw && u == o.u && v == o.v;
            public override bool Equals(object obj) => obj is Corner c && Equals(c);
            public override int GetHashCode()
            {
                unchecked
                {
                    int h = px;
                    h = h * 31 + py; h = h * 31 + pz; h = h * 31 + nx; h = h * 31 + ny; h = h * 31 + nz;
                    h = h * 31 + tx; h = h * 31 + ty; h = h * 31 + tz; h = h * 31 + tw; h = h * 31 + u; h = h * 31 + v;
                    return h;
                }
            }
        }

        /// <summary>True when every triangle of <paramref name="current"/> is one of the original
        /// triangles with unchanged corners. False when anything can't be compared.</summary>
        public static bool SurvivingTrianglesUnchanged(IReadOnlyList<AtlasMeshSection> originals, AtlasMeshSection current)
        {
            if (originals == null || originals.Count == 0 || !Complete(current)) return false;
            var ids = new Dictionary<Corner, int>();
            var triangles = new HashSet<(int, int, int)>();
            foreach (var original in originals)
            {
                if (!Complete(original)) return false;
                var corners = Corners(original);
                var tri = original.Triangles;
                for (int i = 0; i + 2 < tri.Length; i += 3)
                {
                    if (!Valid(original, tri[i], tri[i + 1], tri[i + 2])) continue;
                    triangles.Add(Key(Id(ids, corners[tri[i]]), Id(ids, corners[tri[i + 1]]), Id(ids, corners[tri[i + 2]])));
                }
            }
            var currentCorners = Corners(current);
            var currentTri = current.Triangles;
            for (int i = 0; i + 2 < currentTri.Length; i += 3)
            {
                if (!Valid(current, currentTri[i], currentTri[i + 1], currentTri[i + 2])) return false;
                if (!ids.TryGetValue(currentCorners[currentTri[i]], out int a) ||
                    !ids.TryGetValue(currentCorners[currentTri[i + 1]], out int b) ||
                    !ids.TryGetValue(currentCorners[currentTri[i + 2]], out int c) ||
                    !triangles.Contains(Key(a, b, c)))
                    return false;
            }
            return true;
        }

        private static bool Complete(AtlasMeshSection s) =>
            s != null && s.Vertices != null && s.Normals != null && s.Tangents != null && s.UV != null && s.Triangles != null &&
            s.Normals.Length == s.Vertices.Length && s.Tangents.Length == s.Vertices.Length && s.UV.Length == s.Vertices.Length;
        private static bool Valid(AtlasMeshSection s, int a, int b, int c) =>
            a >= 0 && b >= 0 && c >= 0 && a < s.Vertices.Length && b < s.Vertices.Length && c < s.Vertices.Length;
        private static int Id(Dictionary<Corner, int> ids, Corner corner)
        {
            if (!ids.TryGetValue(corner, out int id)) { id = ids.Count; ids.Add(corner, id); }
            return id;
        }
        // The same triangle in any vertex order; winding doesn't change shading.
        private static (int, int, int) Key(int a, int b, int c)
        {
            if (a > b) { int x = a; a = b; b = x; }
            if (b > c) { int x = b; b = c; c = x; }
            if (a > b) { int x = a; a = b; b = x; }
            return (a, b, c);
        }
        // Every vertex in avatar-root space, transformed the way the bake rasterizes it.
        private static Corner[] Corners(AtlasMeshSection s)
        {
            var m = s.LocalToWorld;
            var normalMatrix = m.inverse.transpose;
            float sign = m.determinant < 0f ? -1f : 1f;
            var corners = new Corner[s.Vertices.Length];
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 n = normalMatrix.MultiplyVector(s.Normals[i]).normalized;
                Vector4 t4 = s.Tangents[i];
                Vector3 t = m.MultiplyVector(new Vector3(t4.x, t4.y, t4.z)).normalized;
                corners[i] = new Corner(m.MultiplyPoint3x4(s.Vertices[i]), n, new Vector4(t.x, t.y, t.z, t4.w * sign), s.UV[i]);
            }
            return corners;
        }
    }
}
