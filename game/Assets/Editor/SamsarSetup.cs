// Меню «Samsar»: сборка проекта одним нажатием, настройки и проверка готовности.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Samsar.EditorTools
{
    public static class SamsarSetup
    {
        [MenuItem("Samsar/0. СОБРАТЬ ВСЁ (материалы + сцены + настройки)", false, 1)]
        public static void BuildEverything()
        {
            if (!EditorUtility.DisplayDialog("Сборка проекта Samsar",
                "Будут созданы материалы, префабы и обе сцены (боевая и ангар).\n" +
                "Это занимает 5–20 минут (генерация карты 3×3 км и выпечка NavMesh).\n\nПродолжить?",
                "Собрать", "Отмена"))
                return;

            try
            {
                EditorUtility.DisplayProgressBar("Samsar", "Материалы и префабы модели техники", 0.1f);
                TankAssetBuilder.BuildMaterials();
                TankAssetBuilder.BuildTankPrefabs();
                TankAssetBuilder.BuildPropPrefabs();

                EditorUtility.DisplayProgressBar("Samsar", "Боевая сцена: карта 3×3 км, город, лес, станция", 0.35f);
                BattleSceneBuilder.Build();

                EditorUtility.DisplayProgressBar("Samsar", "Главная сцена: 3D-ангар и меню", 0.8f);
                MainSceneBuilder.Build();

                ApplyProjectSettings();
                CheckReady();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            EditorUtility.DisplayDialog("Готово",
                "Проект собран.\n\n1) Нажми Play — откроется ангар.\n" +
                "2) Выбери машину и нажми «В БОЙ».\n\n" +
                "Если боты ходят напрямик — испеки NavMesh: Window → AI → Navigation → Bake.",
                "Отлично");
        }

        [MenuItem("Samsar/Проверка готовности проекта", false, 40)]
        public static void CheckReady()
        {
            int ok = 0, problems = 0;
            System.Action<string, bool> check = (label, condition) =>
            {
                if (condition) { ok++; Debug.Log("✅ " + label); }
                else { problems++; Debug.LogWarning("❌ " + label); }
            };

            check("Текстуры (Assets/Textures)", Directory.Exists("Assets/Textures") &&
                  Directory.GetFiles("Assets/Textures").Length > 20);
            check("Модели техники (4 FBX)", Directory.Exists("Assets/Models/Tanks") &&
                  Directory.GetFiles("Assets/Models/Tanks", "*.fbx").Length >= 4);
            check("Модели объектов", Directory.Exists("Assets/Models/Props") &&
                  Directory.GetFiles("Assets/Models/Props", "*.fbx").Length >= 10);
            check("Префабы техники", Directory.Exists("Assets/Prefabs/Tanks") &&
                  Directory.GetFiles("Assets/Prefabs/Tanks", "*.prefab").Length >= 4);
            check("Материалы", Directory.Exists("Assets/Materials") &&
                  Directory.GetFiles("Assets/Materials", "*.mat").Length >= 8);
            check("Боевая сцена", File.Exists("Assets/Scenes/Battle.unity"));
            check("Главная сцена", File.Exists("Assets/Scenes/Main.unity"));
            check("Сцены в сборке", EditorBuildSettings.scenes.Length >= 2);

            Debug.Log(string.Format("Проверка завершена: {0} успешно, проблем {1}.", ok, problems));
        }

        [MenuItem("Samsar/Настройки проекта", false, 41)]
        public static void ApplyProjectSettingsMenu() { ApplyProjectSettings(); }

        public static void ApplyProjectSettings()
        {
            PlayerSettings.productName = "Samsar";
            PlayerSettings.companyName = "Samsar Team";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.runInBackground = true;
            QualitySettings.vSyncCount = 1;
            QualitySettings.shadowDistance = 300f;
            QualitySettings.antiAliasing = 4;
            Time.fixedDeltaTime = 0.02f;

            var scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Battle.unity", true),
            };
            EditorBuildSettings.scenes = scenes;
            AssetDatabase.SaveAssets();
            Debug.Log("Настройки проекта применены: Samsar, линейное цветовое пространство, сцены Main → Battle.");
        }
    }
}
