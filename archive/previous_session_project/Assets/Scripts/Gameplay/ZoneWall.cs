using UnityEngine;

namespace Samsar
{
    /// <summary>Визуальная граница опасной зоны: стена и кольцо на земле.</summary>
    public class ZoneWall : MonoBehaviour
    {
        GameObject wall;
        GameObject ring;
        Material wallMat;

        public void Build()
        {
            var builder = new MeshBuilder();
            builder.AddCylinderWall(Vector3.zero, GameConfig.ZoneStartRadius, 90f, 128,
                new Color(0.9f, 0.25f, 0.2f, 0.35f), new Color(0.9f, 0.35f, 0.2f, 0f));
            wall = new GameObject("wall");
            wall.transform.SetParent(transform, false);
            wall.AddComponent<MeshFilter>().mesh = builder.ToMesh(false);
            wallMat = MatLib.Transparent(new Color(0.9f, 0.25f, 0.2f), 0.28f);
            wallMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            var wr = wall.AddComponent<MeshRenderer>();
            wr.sharedMaterial = wallMat;
            wr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var rb = new MeshBuilder();
            rb.AddRing(Vector3.zero, GameConfig.ZoneStartRadius, 14f, 128, new Color(1f, 0.55f, 0.15f, 0.75f));
            ring = new GameObject("ring");
            ring.transform.SetParent(transform, false);
            ring.AddComponent<MeshFilter>().mesh = rb.ToMesh(false);
            ring.GetComponent<MeshRenderer>().sharedMaterial = MatLib.Transparent(new Color(1f, 0.55f, 0.15f), 0.8f);
            ring.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        public void UpdateZone(Vector2 center, float radius, bool warning)
        {
            if (wall == null) return;
            float scale = radius / GameConfig.ZoneStartRadius;
            wall.transform.localScale = new Vector3(scale, 1f, scale);
            ring.transform.localScale = new Vector3(scale, 1f, scale);
            Vector3 pos = new Vector3(center.x, 0f, center.y);
            wall.transform.position = pos;
            ring.transform.position = pos + Vector3.up * 0.6f;
            var c = warning ? new Color(1f, 0.2f, 0.15f) : new Color(1f, 0.6f, 0.15f);
            if (wallMat != null) wallMat.SetColor("_Color", new Color(c.r, c.g, c.b, warning ? 0.42f : 0.25f));
        }
    }
}
