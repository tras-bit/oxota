using System.Collections.Generic;
using UnityEngine;

namespace Samsar
{
    /// <summary>Сборщик произвольных мешей: дороги, зоны, карта, детали окружения.</summary>
    public class MeshBuilder
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<int> T = new List<int>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<Color> C = new List<Color>();

        public int AddVertex(Vector3 p, Vector2 uv, Color c)
        {
            V.Add(p); UV.Add(uv); C.Add(c);
            return V.Count - 1;
        }

        public void AddTriangle(int a, int b, int c)
        {
            T.Add(a); T.Add(b); T.Add(c);
        }

        public void AddQuad(int a, int b, int c, int d)
        {
            T.Add(a); T.Add(b); T.Add(c);
            T.Add(a); T.Add(c); T.Add(d);
        }

        /// <summary>Прямоугольник из 4 точек (по часовой стрелке при взгляде снаружи).</summary>
        public void AddFace(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Color col, float uvScale = 1f)
        {
            int a = AddVertex(p0, new Vector2(0f, 0f), col);
            int b = AddVertex(p1, new Vector2(uvScale, 0f), col);
            int c = AddVertex(p2, new Vector2(uvScale, uvScale), col);
            int d = AddVertex(p3, new Vector2(0f, uvScale), col);
            AddQuad(a, b, c, d);
        }

        /// <summary>Параллелепипед (центр, размер, поворот).</summary>
        public void AddBox(Vector3 center, Vector3 size, Quaternion rot, Color col, float uvScale = 1f)
        {
            Vector3 h = size * 0.5f;
            Vector3[] c = new Vector3[8];
            int i = 0;
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                        c[i++] = center + rot * new Vector3(h.x * x, h.y * y, h.z * z);
            // индексы: [x,y,z] = (x>0?4:0)+(y>0?2:0)+(z>0?1:0)
            AddFace(c[0], c[1], c[3], c[2], col, uvScale); // -X
            AddFace(c[5], c[4], c[6], c[7], col, uvScale); // +X
            AddFace(c[4], c[5], c[1], c[0], col, uvScale); // -Y
            AddFace(c[2], c[3], c[7], c[6], col, uvScale); // +Y
            AddFace(c[4], c[0], c[2], c[6], col, uvScale); // -Z
            AddFace(c[1], c[5], c[7], c[3], col, uvScale); // +Z
        }

        public void AddCylinder(Vector3 baseCenter, float radius, float height, int segments, Color col)
        {
            int bottom = V.Count;
            for (int s = 0; s < segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                Vector3 p = baseCenter + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                AddVertex(p, new Vector2(s / (float)segments, 0f), col);
            }
            int top = V.Count;
            for (int s = 0; s < segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                Vector3 p = baseCenter + new Vector3(Mathf.Cos(a) * radius, height, Mathf.Sin(a) * radius);
                AddVertex(p, new Vector2(s / (float)segments, 1f), col);
            }
            for (int s = 0; s < segments; s++)
            {
                int n = (s + 1) % segments;
                AddQuad(bottom + s, bottom + n, top + n, top + s);
            }
        }

        /// <summary>Кольцо (граница зоны), лежит в плоскости XZ.</summary>
        public void AddRing(Vector3 center, float radius, float width, int segments, Color col)
        {
            float r0 = radius - width * 0.5f, r1 = radius + width * 0.5f;
            int start = V.Count;
            for (int s = 0; s <= segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                AddVertex(center + dir * r0, new Vector2(0f, 0f), col);
                AddVertex(center + dir * r1, new Vector2(1f, 0f), col);
            }
            for (int s = 0; s < segments; s++)
            {
                int a = start + s * 2, b = start + s * 2 + 1, c = start + s * 2 + 3, d = start + s * 2 + 2;
                AddQuad(a, b, c, d);
            }
        }

        /// <summary>Стена-цилиндр (визуальная граница опасной зоны).</summary>
        public void AddCylinderWall(Vector3 center, float radius, float height, int segments, Color colBottom, Color colTop)
        {
            int start = V.Count;
            for (int s = 0; s <= segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                AddVertex(center + dir * radius, new Vector2(s / (float)segments, 0f), colBottom);
                AddVertex(center + dir * radius + Vector3.up * height, new Vector2(s / (float)segments, 1f), colTop);
            }
            for (int s = 0; s < segments; s++)
            {
                int a = start + s * 2, b = start + s * 2 + 1, c = start + s * 2 + 3, d = start + s * 2 + 2;
                AddTriangle(a, c, b);
                AddTriangle(a, d, c);
            }
        }

        /// <summary>Лента по точкам (дорога, траншея).</summary>
        public void AddRibbon(List<Vector3> points, float width, float yOffset, Color col, float uvTiling = 0.1f)
        {
            if (points == null || points.Count < 2) return;
            int start = V.Count;
            float dist = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 fwd = i < points.Count - 1 ? (points[i + 1] - points[i]) : (points[i] - points[i - 1]);
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
                fwd.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, fwd) * (width * 0.5f);
                if (i > 0) dist += Vector3.Distance(points[i - 1], points[i]);
                AddVertex(points[i] - side + Vector3.up * yOffset, new Vector2(0f, dist * uvTiling), col);
                AddVertex(points[i] + side + Vector3.up * yOffset, new Vector2(1f, dist * uvTiling), col);
            }
            for (int i = 0; i < points.Count - 1; i++)
            {
                int a = start + i * 2, b = a + 1, c = a + 3, d = a + 2;
                AddQuad(a, b, c, d);
            }
        }

        public Mesh ToMesh(bool recalcNormals = true)
        {
            var m = new Mesh();
            if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(V);
            m.SetTriangles(T, 0);
            m.SetUVs(0, UV);
            m.SetColors(C);
            if (recalcNormals) m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
