// Сборка Windows-версии одним пунктом меню: Samsar → 6. Собрать Windows-версию.
// Нужна, чтобы получить играбельный .exe без ручной настройки Build Settings.
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Samsar.EditorTools
{
    public static class BuildWindows
    {
        const string MenuPath = "Samsar/6. Собрать Windows-версию";

        [MenuItem(MenuPath, false, 60)]
        public static void Build()
        {
            // сцены берём из настроек сборки: их заполняет «Samsar → Настройки проекта»
            var scenes = new System.Collections.Generic.List<string>();
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled && File.Exists(s.path))
                    scenes.Add(s.path);

            if (scenes.Count < 2)
            {
                if (!SamsarSetup_menu()) return;
                foreach (var s in EditorBuildSettings.scenes)
                    if (s.enabled && File.Exists(s.path) && !scenes.Contains(s.path))
                        scenes.Add(s.path);
            }
            if (scenes.Count == 0)
            {
                Debug.LogError("Нечего собирать: сначала «Samsar → 0. СОБРАТЬ ВСЁ».");
                return;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outDir = Path.Combine(Directory.GetParent(projectRoot).FullName, "Build", "Windows");
            Directory.CreateDirectory(outDir);
            string exe = Path.Combine(outDir, "Samsar.exe");

            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            Debug.Log("Сборка Windows: " + string.Join(", ", scenes) + " → " + exe);
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                double mb = summary.totalSize / 1024.0 / 1024.0;
                Debug.Log(string.Format("✅ Готово: {0} ({1:0} МБ, {2:0} с). Запускай Samsar.exe — Unity больше не нужен.",
                                        exe, mb, summary.totalTime.TotalSeconds));
                EditorUtility.RevealInFinder(exe);
            }
            else
            {
                Debug.LogError(string.Format("Сборка не удалась: {0}, ошибок {1}.",
                                             summary.result, summary.totalErrors));
            }
        }

        /// <summary>Если сцены ещё не в сборке — сначала прогоняем «Собрать всё».</summary>
        static bool SamsarSetup_menu()
        {
            Debug.LogWarning("В настройках сборки нет сцен — запускаю «Samsar → 0. СОБРАТЬ ВСЁ».");
            SamsarSetup.BuildEverything();
            AssetDatabase.SaveAssets();
            return EditorBuildSettings.scenes.Length >= 1;
        }

        [MenuItem("Samsar/7. Открыть папку сборки", false, 61)]
        public static void OpenBuildFolder()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outDir = Path.Combine(Directory.GetParent(projectRoot).FullName, "Build", "Windows");
            if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
            EditorUtility.RevealInFinder(outDir);
        }
    }
}
