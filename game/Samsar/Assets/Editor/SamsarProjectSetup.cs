using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Samsar.EditorTools
{
    /// <summary>
    /// Автоматическая настройка проекта при первом открытии:
    /// создаёт сцены, добавляет их в Build Settings, выставляет параметры игрока.
    /// Меню: Samsar → Настроить проект.
    /// </summary>
    public static class SamsarProjectSetup
    {
        const string SceneFolder = "Assets/Scenes";
        static readonly string[] Scenes = { "MainMenu", "Loading", "Hangar", "Battle", "Results" };

        [InitializeOnLoadMethod]
        static void AutoSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(SceneFolder + "/Battle.unity"))
                {
                    Debug.Log("[Samsar] Первый запуск: создаю сцены и настраиваю проект…");
                    SetupProject(true);
                }
            };
        }

        [MenuItem("Samsar/Настроить проект (создать сцены)")]
        public static void SetupMenu()
        {
            SetupProject(false);
        }

        public static void SetupProject(bool silent)
        {
            Directory.CreateDirectory(SceneFolder);

            var list = new List<EditorBuildSettingsScene>();
            foreach (var name in Scenes)
            {
                string path = SceneFolder + "/" + name + ".unity";
                if (!File.Exists(path)) CreateScene(path);
                list.Add(new EditorBuildSettingsScene(path, true));
            }
            EditorBuildSettings.scenes = list.ToArray();

            // параметры игрока
            PlayerSettings.productName = "SAMSAR";
            PlayerSettings.companyName = "Samsar Team";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultIsNativeResolution = true;

            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = 120;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (!silent)
                EditorUtility.DisplayDialog("SAMSAR", "Готово: созданы сцены и настроен проект.\nОткрой Assets/Scenes/MainMenu.unity и нажми Play.", "Ок");
            Debug.Log("[Samsar] Проект настроен. Сцены: " + string.Join(", ", Scenes));
        }

        [MenuItem("Samsar/Собрать сборку для Windows")]
        public static void BuildWindows()
        {
            SetupProject(true);
            string outDir = Path.Combine(Application.dataPath, "../../Builds/Windows");
            Directory.CreateDirectory(outDir);
            string exe = Path.Combine(outDir, "SAMSAR.exe");
            var options = new BuildPlayerOptions
            {
                scenes = new[] { SceneFolder + "/MainMenu.unity", SceneFolder + "/Loading.unity", SceneFolder + "/Hangar.unity", SceneFolder + "/Battle.unity", SceneFolder + "/Results.unity" },
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                EditorUtility.RevealInFinder(exe);
                EditorUtility.DisplayDialog("SAMSAR", "Сборка готова:\n" + exe, "Ок");
            }
            else
            {
                EditorUtility.DisplayDialog("SAMSAR", "Ошибка сборки: " + report.summary.result, "Ок");
            }
        }

        static void CreateScene(string path)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Bootstrap");
            go.AddComponent<SceneBootstrap>();
            EditorSceneManager.SaveScene(scene, path);
        }
    }
}
