// Объединение статичных объектов (деревья, рельсы, заборы) в крупные меши — для 3×3 км карты.
using System.Collections.Generic;
using UnityEngine;

namespace Samsar.EditorTools
{
    public static class StaticBatcher
    {
        /// <summary>Объединяет объекты по материалу в один меш (до 60k вершин на меш).</summary>
        public static List<GameObject> Combine(List<GameObject> sources, string rootName,
                                               bool addColliders, int maxVerticesPerMesh = 60000)
        {
            var byMaterial = new Dictionary<Material, List<CombineInstance>>();
            foreach (var go in sources)
            {
                if (go == null) continue;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                {
                    if (mf.sharedMesh == null) continue;
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || mr.sharedMaterial == null) continue;
                    if (!byMaterial.ContainsKey(mr.sharedMaterial))
                        byMaterial[mr.sharedMaterial] = new List<CombineInstance>();
                    byMaterial[mr.sharedMaterial].Add(new CombineInstance
                    {
                        mesh = mf.sharedMesh,
                        transform = mf.transform.localToWorldMatrix
                    });
                }
            }

            var created = new List<GameObject>();
            var parent = new GameObject(rootName);
            int chunk = 0;
            foreach (var kv in byMaterial)
            {
                var list = kv.Value;
                int start = 0;
                while (start < list.Count)
                {
                    var part = new List<CombineInstance>();
                    int verts = 0;
                    while (start < list.Count && verts < maxVerticesPerMesh)
                    {
                        verts += list[start].mesh.vertexCount;
                        part.Add(list[start]);
                        start++;
                    }
                    var mesh = new Mesh();
                    mesh.indexFormat = part.Count > 0 && verts > 65000
                        ? UnityEngine.Rendering.IndexFormat.UInt32
                        : UnityEngine.Rendering.IndexFormat.UInt16;
                    mesh.CombineMeshes(part.ToArray(), true, true);
                    mesh.RecalculateBounds();

                    var go = new GameObject(rootName + "_" + chunk++);
                    go.transform.SetParent(parent.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = kv.Key;
                    if (addColliders) go.AddComponent<MeshCollider>().sharedMesh = mesh;
                    created.Add(go);
                }
            }

            foreach (var go in sources) if (go != null) Object.DestroyImmediate(go);
            return created;
        }
    }
}
