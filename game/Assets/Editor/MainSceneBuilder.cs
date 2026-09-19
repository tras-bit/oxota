// Сборка главной сцены «Main»: 3D-ангар с вращающейся машиной и меню.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Samsar.EditorTools
{
    public static class MainSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Samsar/3. Собрать главную сцену (ангар)", false, 30)]
        public static void Build()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // свет и окружение ангара
            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.45f, 0.5f, 0.6f);
            RenderSettings.ambientEquatorColor = new Color(0.3f, 0.32f, 0.34f);
            RenderSettings.ambientGroundColor = new Color(0.15f, 0.15f, 0.14f);

            var sunGo = new GameObject("AngarLight");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sun.color = new Color(1f, 0.97f, 0.9f);
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(42f, 160f, 0f);

            var fillGo = new GameObject("FillLight");
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.intensity = 2.2f;
            fill.range = 30f;
            fill.color = new Color(0.75f, 0.85f, 1f);
            fillGo.transform.position = new Vector3(-6f, 6f, -6f);

            // площадка
            var platform = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            platform.name = "Platform";
            platform.transform.position = new Vector3(0f, -0.2f, 0f);
            platform.transform.localScale = new Vector3(11f, 0.2f, 11f);
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/MAT_Concrete.mat");
            if (mat != null) platform.GetComponent<Renderer>().material = mat;

            // камера
            var camGo = new GameObject("MainCamera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.08f, 0.09f);
            cam.fieldOfView = 42f;
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(9f, 4.2f, 9f);
            camGo.transform.LookAt(new Vector3(0f, 1.2f, 0f));

            // библиотека моделей и предпросмотр
            var libGo = new GameObject("TankLibrary");
            var lib = libGo.AddComponent<TankLibrary>();
            var entries = new System.Collections.Generic.List<TankLibrary.Entry>();
            foreach (var spec in TankSpec.Roster)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tanks/" + spec.id + ".prefab");
                entries.Add(new TankLibrary.Entry { id = spec.id, model = prefab });   // ключ — как у TankSpec.id: так его ищут AngarUI и TankRegistry
            }
            lib.tanks = entries.ToArray();

            var uiGo = new GameObject("AngarUI");
            var ui = uiGo.AddComponent<AngarUI>();
            ui.library = lib;
            var preview = new GameObject("PreviewPoint");
            preview.transform.position = new Vector3(0f, 0f, 0f);
            ui.previewPoint = preview.transform;
            ui.previewCamera = cam;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("Главная сцена собрана: " + ScenePath);
        }
    }
}
