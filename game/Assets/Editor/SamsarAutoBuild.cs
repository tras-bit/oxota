// Автосборка проекта: Unity сам создаёт материалы, префабы и обе сцены при первом открытии —
// пункт меню «Samsar → 0» нажимать не нужно. Дальше автосборка молчит; если файлы генерации
// обновились (изменился номер версии ниже) — вежливо спросит и пересоберёт.
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
// using System.Diagnostics (нужен Stopwatch) делает голый Debug двусмысленным
// (System.Diagnostics.Debug или UnityEngine.Debug?) — фиксируем явно:
using Debug = UnityEngine.Debug;

namespace Samsar.EditorTools
{
    [InitializeOnLoad]
    public static class SamsarAutoBuild
    {
        // Поднять Version, чтобы у всех, у кого проект уже открыт, сцены пересобрались заново.
        // v2: камуфляжи спецмашин + сгоревшие танки на карте.
        // v3: объекты и точки спавна ставятся по высоте рельефа (карта больше не тонет),
        //     у миникарты появилась подложка с дорогами и городом.
        const string Version = "3";
        const string MarkerPath = "Assets/Samsar.autobuild";
        const string MainScene = "Assets/Scenes/Main.unity";
        const string BattleScene = "Assets/Scenes/Battle.unity";

        static SamsarAutoBuild()
        {
            // delayCall: ждём, пока редактор закончит компиляцию и импорт — сцены можно строить
            EditorApplication.delayCall += WaitForEditorReady;
        }

        static void WaitForEditorReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += WaitForEditorReady;
                return;
            }
            TryRun();
        }

        static void TryRun()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            bool scenesReady = File.Exists(MainScene) && File.Exists(BattleScene) &&
                               Directory.Exists("Assets/Prefabs/Tanks");
            string marker = File.Exists(MarkerPath) ? File.ReadAllText(MarkerPath).Trim() : "";

            if (scenesReady && marker == Version) return;                 // собрано этой версией — тишина
            if (scenesReady && marker == "") { WriteMarker(); return; }   // собрали руками — просто отметим

            // строить не из чего: моделей или текстур нет в проекте
            if (Count("Assets/Models/Tanks", "*.fbx") < 4 || Count("Assets/Textures", "*.*") < 45)
            {
                Debug.LogWarning("Samsar: автосборка пропущена — в Assets/Models или Assets/Textures " +
                                 "не хватает файлов. Положи их и перезапусти Unity (или собери руками: " +
                                 "Samsar → 0. СОБРАТЬ ВСЁ).");
                return;
            }

            // сцены уже есть, но генераторы обновились — спросим, прежде чем пересобирать
            if (scenesReady)
            {
                if (!EditorUtility.DisplayDialog("Samsar: проект обновился",
                    "Файлы генерации изменились — пересобрать сцены и материалы?\n" +
                    "Это занимает 5–20 минут; несохранённые изменения в сценах будут потеряны.",
                    "Пересобрать", "Позже"))
                    return;
            }

            Build();
        }

        static void Build()
        {
            var sw = Stopwatch.StartNew();
            Debug.Log("Samsar: автосборка запущена — материалы, префабы и обе сцены. Не закрывай Unity.");
            try
            {
                AssetDatabase.Refresh();

                EditorUtility.DisplayProgressBar("Samsar: автосборка", "Материалы и префабы техники", 0.1f);
                TankAssetBuilder.BuildMaterials();
                TankAssetBuilder.BuildTankPrefabs();
                TankAssetBuilder.BuildPropPrefabs();

                EditorUtility.DisplayProgressBar("Samsar: автосборка",
                    "Боевая сцена: карта 3×3 км, город, лес, станция", 0.35f);
                BattleSceneBuilder.Build();

                EditorUtility.DisplayProgressBar("Samsar: автосборка", "Главная сцена: 3D-ангар", 0.8f);
                MainSceneBuilder.Build();

                SamsarSetup.ApplyProjectSettings();
                WriteMarker();
                AssetDatabase.SaveAssets();

                EditorSceneManager.OpenScene(MainScene);
                Debug.Log("Samsar: автосборка готова за " + sw.Elapsed.TotalMinutes.ToString("0.0") + " мин.");
                EditorUtility.DisplayDialog("Samsar: всё собрано",
                    "Сцены и материалы созданы автоматически.\n\n" +
                    "Нажми Play — откроется ангар.\nВыбери машину и жми «В БОЙ».",
                    "Играть");
            }
            catch (System.Exception e)
            {
                Debug.LogError("Samsar: автосборка не удалась: " + e.Message + "\n" + e.StackTrace +
                               "\nСобери вручную: меню Samsar → 0. СОБРАТЬ ВСЁ.");
                EditorUtility.DisplayDialog("Samsar: автосборка не удалась",
                    "Ошибка: " + e.Message + "\n\n" +
                    "Собери вручную: меню Samsar → 0. СОБРАТЬ ВСЁ.\n" +
                    "Подробности — в консоли (Window → General → Console).",
                    "Понятно");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static void WriteMarker()
        {
            File.WriteAllText(MarkerPath, Version);
            AssetDatabase.Refresh();
        }

        static int Count(string dir, string pattern)
        {
            return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern).Length : 0;
        }
    }
}
