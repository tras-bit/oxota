// Создание материалов и префабов из FBX/текстур, сгенерированных в Blender.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Samsar.EditorTools
{
    public static class TankAssetBuilder
    {
        const string TexDir = "Assets/Textures";
        const string MatDir = "Assets/Materials";
        const string TankModelDir = "Assets/Models/Tanks";
        const string PropModelDir = "Assets/Models/Props";
        const string TankPrefabDir = "Assets/Prefabs/Tanks";
        const string PropPrefabDir = "Assets/Prefabs/Props";

        public static readonly string[] CamoTextures =
            { "steel_olive", "steel_sand", "steel_grey", "steel_green" };

        [MenuItem("Samsar/1. Обновить материалы и префабы", false, 10)]
        public static void RebuildAll()
        {
            BuildMaterials();
            BuildTankPrefabs();
            BuildPropPrefabs();
            Debug.Log("Материалы и префабы обновлены.");
        }

        // ---------- материалы ----------
        public static void BuildMaterials()
        {
            Directory.CreateDirectory(MatDir);

            var rubberAlbedo = LoadTex("rubber_albedo.jpg");
            var rubberNormal = LoadTex("rubber_normal.png");
            var rubberMask = LoadTex("rubber_mask.png");
            MakeMaterial("MAT_Rubber", rubberAlbedo, rubberNormal, rubberMask, 1f, new Color(1f, 1f, 1f));

            var optAlbedo = LoadTex("optics_albedo.jpg");
            var optNormal = LoadTex("optics_normal.png");
            var optMask = LoadTex("optics_mask.png");
            MakeMaterial("MAT_Optics", optAlbedo, optNormal, optMask, 1f, new Color(1f, 1f, 1f));

            var rustyAlbedo = LoadTex("metal_rusty_albedo.jpg");
            var rustyNormal = LoadTex("metal_rusty_normal.png");
            var rustyMask = LoadTex("metal_rusty_mask.png");
            MakeMaterial("MAT_Rusty", rustyAlbedo, rustyNormal, rustyMask, 1f, new Color(1f, 1f, 1f));

            var concreteAlbedo = LoadTex("concrete_albedo.jpg");
            var concreteNormal = LoadTex("concrete_normal.png");
            var concreteMask = LoadTex("concrete_mask.png");
            MakeMaterial("MAT_Concrete", concreteAlbedo, concreteNormal, concreteMask, 1f, new Color(1f, 1f, 1f));

            var woodAlbedo = LoadTex("wood_albedo.jpg");
            var woodNormal = LoadTex("wood_normal.png");
            var woodMask = LoadTex("wood_mask.png");
            MakeMaterial("MAT_Wood", woodAlbedo, woodNormal, woodMask, 1f, new Color(1f, 1f, 1f));

            var leafAlbedo = LoadTex("ground_grass_albedo.jpg");
            var leafNormal = LoadTex("ground_grass_normal.png");
            MakeMaterial("MAT_Leaf", leafAlbedo, leafNormal, null, 0f, new Color(0.32f, 0.5f, 0.24f));

            foreach (var camo in CamoTextures)
            {
                var a = LoadTex(camo + "_albedo.jpg");
                var n = LoadTex(camo + "_normal.png");
                var m = LoadTex(camo + "_mask.png");
                MakeMaterial("MAT_" + camo, a, n, m, 1f, Color.white);
            }
            MakeMaterial("MAT_SteelOlive", LoadTex("steel_olive_albedo.jpg"), LoadTex("steel_olive_normal.png"),
                         LoadTex("steel_olive_mask.png"), 1f, Color.white);
        }

        static Texture2D LoadTex(string name)
        {
            var path = TexDir + "/" + name;
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t == null) return null;
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null)
            {
                bool wantNormal = name.Contains("_normal");
                if (imp.textureType != (wantNormal ? TextureImporterType.NormalMap : TextureImporterType.Default) ||
                    imp.maxTextureSize < 2048 || !imp.mipmapEnabled)
                {
                    imp.textureType = wantNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                    imp.maxTextureSize = 2048;
                    imp.mipmapEnabled = true;
                    imp.anisoLevel = 4;
                    imp.SaveAndReimport();
                }
            }
            return t;
        }

        static void MakeMaterial(string name, Texture2D albedo, Texture2D normal, Texture2D mask,
                                 float metallic, Color tint)
        {
            var path = MatDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = Shader.Find("Standard");
            mat.color = tint;
            if (albedo != null) mat.SetTexture("_MainTex", albedo);
            if (normal != null)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (mask != null)
            {
                // наш mask: R = metalness, A = smoothness — точь-в-точь формат Standard
                mat.SetTexture("_MetallicGlossMap", mask);
                mat.EnableKeyword("_METALLICGLOSSMAP");
            }
            else
            {
                mat.SetFloat("_Metallic", metallic);
                mat.SetFloat("_Glossiness", 0.25f);
            }
            mat.SetFloat("_GlossyReflections", 1f);
            EditorUtility.SetDirty(mat);
        }

        // ---------- префабы техники ----------
        public static void BuildTankPrefabs()
        {
            Directory.CreateDirectory(TankPrefabDir);
            foreach (var spec in TankSpec.Roster)
            {
                var fbxPath = TankModelDir + "/" + spec.id + ".fbx";
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                if (model == null)
                {
                    Debug.LogWarning("Нет модели " + fbxPath + " — сгенерируй её в Blender (tools/build_models.py).");
                    continue;
                }
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                instance.name = spec.id;
                AssignMaterials(instance);
                RemoveChildColliders(instance);
                var prefabPath = TankPrefabDir + "/" + spec.id + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                Object.DestroyImmediate(instance);
            }
        }

        static void AssignMaterials(GameObject root)
        {
            var camoMap = new Dictionary<string, string>
            {
                { "lt", "steel_olive" }, { "mt", "steel_sand" },
                { "ht", "steel_grey" }, { "td", "steel_green" },
            };
            string camo = "steel_olive";
            foreach (var kv in camoMap)
                if (root.name.Contains(kv.Key)) camo = kv.Value;

            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                var list = new List<Material>();
                foreach (var m in r.sharedMaterials)
                {
                    string n = m != null ? m.name : "";
                    Material repl = null;
                    if (n.Contains("Rubber")) repl = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/MAT_Rubber.mat");
                    else if (n.Contains("Optics")) repl = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/MAT_Optics.mat");
                    else if (n.Contains("Rusty")) repl = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/MAT_Rusty.mat");
                    else if (n.Contains("Concrete")) repl = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/MAT_Concrete.mat");
                    else if (n.Contains("Wood")) repl = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/MAT_Wood.mat");
                    else if (n.Contains("Leaf")) repl = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/MAT_Leaf.mat");
                    else if (n.Contains("Armor") || n.Contains("steel"))
                        repl = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/MAT_" + camo + ".mat");
                    list.Add(repl != null ? repl : m);
                }
                r.sharedMaterials = list.ToArray();
            }
        }

        static void RemoveChildColliders(GameObject root)
        {
            foreach (var c in root.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(c);
        }

        // ---------- префабы объектов окружения ----------
        public static void BuildPropPrefabs()
        {
            Directory.CreateDirectory(PropPrefabDir);
            var names = new[]
            {
                "house", "house_small", "warehouse", "station", "tower",
                "tree_spruce", "tree_birch", "fence", "container", "block",
                "rubble", "rail_segment", "bale"
            };
            foreach (var n in names)
            {
                var fbxPath = PropModelDir + "/" + n + ".fbx";
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                if (model == null)
                {
                    Debug.LogWarning("Нет модели объекта " + fbxPath);
                    continue;
                }
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                instance.name = n;
                AssignPropMaterials(instance, n);
                PrefabUtility.SaveAsPrefabAsset(instance, PropPrefabDir + "/" + n + ".prefab");
                Object.DestroyImmediate(instance);
            }
        }

        static void AssignPropMaterials(GameObject root, string kind)
        {
            Material concrete = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/MAT_Concrete.mat");
            Material wood = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/MAT_Wood.mat");
            Material metal = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/MAT_Rusty.mat");
            Material steel = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/MAT_steel_olive.mat");
            Material glass = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/MAT_Optics.mat");
            Material leaf = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/MAT_Leaf.mat");

            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                var list = new List<Material>();
                foreach (var m in r.sharedMaterials)
                {
                    string n = m != null ? m.name : "";
                    Material repl = null;
                    if (n.Contains("Rubber")) repl = metal;
                    else if (n.Contains("Optics")) repl = glass;
                    else if (n.Contains("Rusty")) repl = metal;
                    else if (n.Contains("Concrete")) repl = concrete;
                    else if (n.Contains("Wood")) repl = wood;
                    else if (n.Contains("Leaf")) repl = leaf;
                    else if (n.Contains("SteelOlive") || n.Contains("steel")) repl = steel;
                    list.Add(repl != null ? repl : m);
                }
                r.sharedMaterials = list.ToArray();
            }
        }

        public static Dictionary<string, GameObject> LoadPropPrefabs()
        {
            var dict = new Dictionary<string, GameObject>();
            var names = new[]
            {
                "house", "house_small", "warehouse", "station", "tower",
                "tree_spruce", "tree_birch", "fence", "container", "block",
                "rubble", "rail_segment", "bale"
            };
            foreach (var n in names)
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>(PropPrefabDir + "/" + n + ".prefab");
                if (p != null) dict[n] = p;
            }
            return dict;
        }
    }
}
