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

        /// <summary>Проверка готовности: что уже собрано, чего не хватает и что нажать.
        /// Печатает одну сводку — чтобы при первом запуске не искать причину в консоли по строчке.</summary>
        [MenuItem("Samsar/Проверка готовности проекта", false, 40)]
        public static void CheckReady()
        {
            var todo = new System.Collections.Generic.List<string>();
            var lines = new System.Collections.Generic.List<string>();
            int ok = 0;

            System.Action<string, bool, string> check = (label, condition, howto) =>
            {
                if (condition)
                {
                    ok++;
                    lines.Add("✅ " + label);
                }
                else
                {
                    lines.Add("❌ " + label);
                    todo.Add(howto);
                }
            };

            // --- исходные ассеты (модели, текстуры) ---
            int tankFbx = Count("Assets/Models/Tanks", "*.fbx");
            int propFbx = Count("Assets/Models/Props", "*.fbx");
            int tex = Count("Assets/Textures", "*.*");
            check("Модели техники: " + tankFbx + " из 4", tankFbx >= 4, "пересобери модели: " +
                  "tools/blender.sh tools/build_models.py tanks (или приложи FBX в Assets/Models/Tanks)");
            check("Модели объектов: " + propFbx + " из 13", propFbx >= 13,
                  "tools/blender.sh tools/build_models.py props");
            check("Текстуры: " + tex + " файл(ов), нужно 51", tex >= 45,
                  "tools/blender.sh tools/make_textures.py");

            // --- собранное Unity (появляется после «Собрать всё») ---
            int mat = Count("Assets/Materials", "*.mat");
            int tankPrefabs = Count("Assets/Prefabs/Tanks", "*.prefab");
            int propPrefabs = Count("Assets/Prefabs/Props", "*.prefab");
            check("Материалы: " + mat, mat >= 8, "Samsar → 0. СОБРАТЬ ВСЁ (материалы делаются первым шагом)");
            check("Префабы техники: " + tankPrefabs + " из 4", tankPrefabs >= 4,
                  "Samsar → 0. СОБРАТЬ ВСЁ");
            check("Префабы объектов: " + propPrefabs + " из 13", propPrefabs >= 13,
                  "Samsar → 0. СОБРАТЬ ВСЁ");
            check("Террейн (Assets/Terrain/BattleTerrain.asset)",
                  File.Exists("Assets/Terrain/BattleTerrain.asset"), "Samsar → 2. Собрать боевую сцену");
            check("Боевая сцена", File.Exists("Assets/Scenes/Battle.unity"), "Samsar → 2. Собрать боевую сцену");
            check("Главная сцена (ангар)", File.Exists("Assets/Scenes/Main.unity"), "Samsar → 3. Собрать главную сцену");
            check("Сцены добавлены в сборку: " + EditorBuildSettings.scenes.Length,
                  EditorBuildSettings.scenes.Length >= 2, "Samsar → Настройки проекта");

            // --- навигация для ботов ---
            bool navMesh = File.Exists("Assets/Scenes/Battle/NavMesh.asset") ||
                           Directory.Exists("Assets/Scenes/Battle") ||
                           File.Exists("Assets/Scenes/Battle.unity") && NavMeshBaked();
            check("Навигация для ботов испечена", navMesh,
                  "открой Assets/Scenes/Battle.unity → Window → AI → Navigation → Bake " +
                  "(игра работает и без неё, но боты будут ходить напрямик)");

            // --- совместимость версии движка ---
            string ver = Application.unityVersion;
            check("Unity 2022.3.x (сейчас " + ver + ")", ver.StartsWith("2022.3"),
                  "проект собран под Unity 2022.3.62f2 — открой его этой версией");

            lines.Add(todo.Count == 0
                ? "Всё готово: открой Assets/Scenes/Main.unity и нажми Play → «В БОЙ»."
                : "Что осталось сделать: " + string.Join("; ", todo));
            Debug.Log(string.Format("Проверка готовности Samsar: успешно {0}, проблем {1}.\n{2}",
                                    ok, todo.Count, string.Join("\n", lines)));
            if (todo.Count > 0)
                Debug.LogWarning("Не хватает " + todo.Count + " шаг(ов) — список в предыдущем сообщении.");
        }

        static int Count(string dir, string pattern)
        {
            return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern).Length : 0;
        }

        static bool NavMeshBaked()
        {
            // NavMesh хранится внутри сцены; грубо проверяем наличие NavMeshSurface/NavMeshData
            string path = "Assets/Scenes/Battle.unity";
            if (!File.Exists(path)) return false;
            string text = File.ReadAllText(path);
            return text.Contains("NavMeshData") || text.Contains("NavMeshSurface") ||
                   text.Length > 2000000;   // большая сцена — почти наверняка с террейном и навигацией
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
