using UnityEngine;
using UnityEngine.SceneManagement;

namespace Samsar
{
    /// <summary>
    /// Единый загрузчик сцен: по имени сцены создаёт нужные системы.
    /// Сцены: MainMenu, Hangar, Battle, Results, Loading — все содержат только объект с этим компонентом.
    /// </summary>
    public class SceneBootstrap : MonoBehaviour
    {
        void Awake()
        {
            string scene = SceneManager.GetActiveScene().name;
            EnsureCamera();
            EnsureLighting();

            switch (scene)
            {
                case "Hangar":
                    gameObject.AddComponent<HangarUI>();
                    break;
                case "Battle":
                    gameObject.AddComponent<BattleManager>();
                    gameObject.AddComponent<BattleHud>();
                    break;
                case "Results":
                    gameObject.AddComponent<ResultsUI>();
                    break;
                case "Loading":
                    gameObject.AddComponent<LoadingScreen>();
                    break;
                default:
                    gameObject.AddComponent<MainMenuUI>();
                    break;
            }
        }

        void EnsureCamera()
        {
            if (Camera.main != null) return;
            var go = new GameObject("MainCamera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 5000f;
            cam.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
            go.AddComponent<AudioListener>();
            go.transform.position = new Vector3(0f, 12f, -18f);
            go.transform.rotation = Quaternion.Euler(12f, 0f, 0f);
        }

        void EnsureLighting()
        {
            if (FindObjectOfType<Light>() != null) return;
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.15f;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(48f, 35f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.45f, 0.5f, 0.55f);
            RenderSettings.ambientEquatorColor = new Color(0.3f, 0.33f, 0.3f);
            RenderSettings.ambientGroundColor = new Color(0.15f, 0.14f, 0.12f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 400f;
            RenderSettings.fogEndDistance = 2400f;
            RenderSettings.fogColor = new Color(0.55f, 0.58f, 0.6f);
        }
    }
}
