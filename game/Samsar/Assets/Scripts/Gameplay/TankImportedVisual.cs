using UnityEngine;

namespace Samsar
{
    /// <summary>
    /// Подключение моделей из Blender (папка Assets/Resources/Models/*.fbx).
    /// Если файл модели есть — визуальная часть машины берётся из Blender,
    /// примитивная геометрия скрывается, а коллайдеры и точки вращения остаются.
    /// Если файла нет — машина выглядит как раньше (примитивы). Масштаб и разворот
    /// подгоняются автоматически, поэтому единицы измерения в FBX не важны.
    /// </summary>
    public static class TankImportedVisual
    {
        static Material importedMat;

        public static bool TryAttach(TankModel model, TankSpec spec)
        {
            if (!GameConfig.UseImportedModels) return false;
            var prefab = Resources.Load<GameObject>("Models/" + spec.Id);
            if (prefab == null) return false;

            var inst = Object.Instantiate(prefab);
            inst.name = "Visual_" + spec.Id;

            var hull = FindDeep(inst.transform, "Hull");
            var turret = FindDeep(inst.transform, "Turret");
            var barrel = FindDeep(inst.transform, "Barrel");
            var muzzle = FindDeep(inst.transform, "MuzzlePoint");
            if (hull == null || turret == null || barrel == null)
            {
                Debug.LogWarning("[Samsar] Модель " + spec.Id + ": не найдены части Hull/Turret/Barrel — используется примитив.");
                Object.Destroy(inst);
                return false;
            }

            // ==== масштаб: подгоняем длину модели под примитивную (единицы FBX могут отличаться) ====
            Bounds imported = BoundsOf(inst);
            Bounds prim = BoundsOf(model.Root);
            float k = 1f;
            if (imported.size.sqrMagnitude > 0.0001f && prim.size.sqrMagnitude > 0.0001f)
            {
                float a = Mathf.Max(imported.size.x, imported.size.z);
                float b = Mathf.Max(prim.size.x, prim.size.z);
                if (a > 0.0001f) k = b / a;
            }
            inst.transform.localScale = Vector3.one * k;
            inst.transform.rotation = Quaternion.identity;
            inst.transform.position = model.Root.transform.position;
            Physics.SyncTransforms();

            // ==== разворот: смотрим, где орудие относительно корпуса ====
            float barrelLocalZ = inst.transform.InverseTransformPoint(barrel.position).z;
            bool facesBack = barrelLocalZ < 0f;
            if (facesBack)
                inst.transform.rotation = model.Root.transform.rotation * Quaternion.Euler(0f, 180f, 0f);

            // ==== скрыть примитивную геометрию (коллайдеры остаются) ====
            HideRenderers(model.BodyRoot);
            HideRenderers(model.Turret);
            HideRenderers(model.Barrel);

            // ==== разложить детали по существующим точкам вращения ====
            hull.SetParent(model.BodyRoot, false);
            hull.localPosition = Vector3.zero;
            hull.localRotation = Quaternion.identity;
            hull.localScale = Vector3.one * k;

            turret.SetParent(model.Turret, false);
            turret.localPosition = Vector3.zero;
            turret.localRotation = Quaternion.identity;
            turret.localScale = Vector3.one * k;

            barrel.SetParent(model.Barrel, false);
            barrel.localPosition = Vector3.zero;
            barrel.localRotation = Quaternion.identity;
            barrel.localScale = Vector3.one * k;

            if (muzzle != null)
            {
                model.BarrelTip = muzzle;
            }
            else
            {
                var tip = new GameObject("BarrelTip");
                tip.transform.SetParent(model.Barrel, false);
                tip.transform.localPosition = Vector3.forward * spec.BarrelLength;
                model.BarrelTip = tip.transform;
            }

            Object.Destroy(inst);

            // ==== материалы и PBR-текстуры ====
            var mat = ImportedMaterial(spec);
            foreach (var r in model.BodyRoot.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            foreach (var r in model.Turret.GetComponentsInChildren<Renderer>()) if (r.enabled) r.sharedMaterial = mat;
            foreach (var r in model.Barrel.GetComponentsInChildren<Renderer>()) if (r.enabled) r.sharedMaterial = mat;

            Debug.Log("[Samsar] Модель Blender подключена: " + spec.Id + " (масштаб " + k.ToString("0.###") + ")");
            return true;
        }

        static void HideRenderers(Transform root)
        {
            if (root == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                r.enabled = false;
        }

        static Bounds BoundsOf(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        static Material ImportedMaterial(TankSpec spec)
        {
            // каждый тип машины получает свой оттенок поверх общей PBR-текстуры
            var mat = new Material(MatLib.Standard) { name = "Tank_" + spec.Id };
            var albedo = Resources.Load<Texture2D>("Textures/tank_albedo");
            var normal = Resources.Load<Texture2D>("Textures/tank_normal");
            if (albedo != null && mat.HasProperty("_MainTex"))
            {
                mat.SetTexture("_MainTex", albedo);
                mat.SetTextureScale("_MainTex", Vector2.one);
            }
            if (normal != null && mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", normal);
                mat.SetTextureScale("_BumpMap", Vector2.one);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.Lerp(spec.BodyColor, Color.white, 0.25f));
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.45f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.38f);
            return mat;
        }
    }
}
