using System.Collections.Generic;
using UnityEngine;

namespace Samsar
{
    /// <summary>
    /// Звук, синтезируемый кодом: не требует внешних файлов и работает сразу.
    /// Если в Resources/Audio лежат .wav/.ogg с такими же именами — они используются вместо синтеза.
    /// </summary>
    public static class AudioSynth
    {
        const int Rate = 44100;
        static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();

        public static AudioClip Get(string name)
        {
            AudioClip clip;
            if (cache.TryGetValue(name, out clip) && clip != null) return clip;

            var external = Resources.Load<AudioClip>("Audio/" + name);
            if (external != null)
            {
                cache[name] = external;
                return external;
            }

            switch (name)
            {
                case "shot": clip = Shot(0.55f, 320f, 0.9f); break;
                case "shot_heavy": clip = Shot(0.9f, 210f, 1.35f); break;
                case "explosion": clip = Explosion(1.7f); break;
                case "hit": clip = HitMetal(0.35f); break;
                case "ricochet": clip = Ricochet(0.4f); break;
                case "penetration": clip = HitMetal(0.5f, 0.55f); break;
                case "engine": clip = EngineLoop(); break;
                case "pickup": clip = Blip(0.18f, 660f, 990f); break;
                case "levelup": clip = Arpeggio(); break;
                case "zone": clip = Siren(2.2f); break;
                case "track": clip = Crunch(0.5f); break;
                case "click": clip = Blip(0.06f, 1200f, 1200f); break;
                case "aircraft": clip = Aircraft(3.5f); break;
                default: clip = Blip(0.12f, 440f, 440f); break;
            }
            cache[name] = clip;
            return clip;
        }

        static AudioClip Make(string name, float seconds, System.Func<float, float, float> sample)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(seconds * Rate));
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                data[i] = Mathf.Clamp(sample(t, i / (float)n), -1f, 1f);
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Rnd() { return Random.Range(-1f, 1f); }

        static AudioClip Shot(float seconds, float freq, float noiseAmt)
        {
            float phase = 0f;
            return Make("shot", seconds, (t, p) =>
            {
                float env = Mathf.Exp(-t * 9f);
                float sweep = Mathf.Lerp(freq, freq * 0.35f, Mathf.Clamp01(t * 6f));
                phase += sweep / Rate * Mathf.PI * 2f;
                float body = Mathf.Sin(phase) * 0.8f + Mathf.Sin(phase * 2.02f) * 0.25f;
                float noise = Rnd() * noiseAmt * Mathf.Exp(-t * 26f);
                return (body + noise) * env * 0.9f;
            });
        }

        static AudioClip Explosion(float seconds)
        {
            return Make("explosion", seconds, (t, p) =>
            {
                float env = Mathf.Exp(-t * 2.4f);
                float low = Mathf.Sin(t * 42f * Mathf.PI * 2f) * 0.6f;
                float noise = Rnd() * 0.9f * Mathf.Exp(-t * 1.6f);
                float crack = Rnd() * 1.2f * Mathf.Exp(-t * 22f);
                return (low + noise + crack) * env;
            });
        }

        static AudioClip HitMetal(float seconds, float tone = 1f)
        {
            return Make("hit", seconds, (t, p) =>
            {
                float env = Mathf.Exp(-t * 14f);
                float ring = Mathf.Sin(t * 1200f * tone * Mathf.PI * 2f) * 0.35f
                           + Mathf.Sin(t * 1830f * tone * Mathf.PI * 2f) * 0.2f;
                float clank = Rnd() * 0.6f * Mathf.Exp(-t * 40f);
                return (ring + clank) * env;
            });
        }

        static AudioClip Ricochet(float seconds)
        {
            float phase = 0f;
            return Make("ricochet", seconds, (t, p) =>
            {
                float env = Mathf.Exp(-t * 7f);
                float f = Mathf.Lerp(2600f, 700f, Mathf.Clamp01(t * 12f));
                phase += f / Rate * Mathf.PI * 2f;
                return Mathf.Sin(phase) * env * 0.5f;
            });
        }

        static AudioClip EngineLoop()
        {
            float p1 = 0f, p2 = 0f;
            return Make("engine", 1f, (t, p) =>
            {
                p1 += 68f / Rate * Mathf.PI * 2f;
                p2 += 136f / Rate * Mathf.PI * 2f;
                float a = Mathf.Sin(p1) * 0.5f + Mathf.Sin(p2) * 0.22f + Mathf.Sin(p1 * 3f) * 0.12f;
                float rumble = Rnd() * 0.08f;
                float loopFade = Mathf.Min(1f, t * 20f) * Mathf.Min(1f, (1f - t) * 20f);
                return (a + rumble) * 0.5f * Mathf.Lerp(1f, 1f, loopFade);
            });
        }

        static AudioClip Blip(float seconds, float f0, float f1)
        {
            float phase = 0f;
            return Make("blip", seconds, (t, p) =>
            {
                float f = Mathf.Lerp(f0, f1, Mathf.Clamp01(t / seconds));
                phase += f / Rate * Mathf.PI * 2f;
                float env = Mathf.Min(1f, t * 60f) * Mathf.Exp(-t * 8f);
                return Mathf.Sin(phase) * env * 0.5f;
            });
        }

        static AudioClip Arpeggio()
        {
            float[] notes = { 523f, 659f, 784f, 1047f };
            float phase = 0f;
            return Make("levelup", 0.7f, (t, p) =>
            {
                int idx = Mathf.Clamp(Mathf.FloorToInt(t * 8f), 0, notes.Length - 1);
                phase += notes[idx] / Rate * Mathf.PI * 2f;
                float env = Mathf.Exp(-(t % 0.125f) * 12f) * Mathf.Exp(-t * 1.6f);
                return Mathf.Sin(phase) * env * 0.45f;
            });
        }

        static AudioClip Siren(float seconds)
        {
            float phase = 0f;
            return Make("zone", seconds, (t, p) =>
            {
                float f = 440f + Mathf.Sin(t * 3.2f * Mathf.PI * 2f) * 180f;
                phase += f / Rate * Mathf.PI * 2f;
                float env = Mathf.Min(1f, t * 4f) * Mathf.Min(1f, (seconds - t) * 3f);
                return Mathf.Sin(phase) * 0.35f * env;
            });
        }

        static AudioClip Crunch(float seconds)
        {
            return Make("crunch", seconds, (t, p) =>
            {
                float env = Mathf.Exp(-t * 8f);
                return (Rnd() * 0.8f + Mathf.Sin(t * 90f * Mathf.PI * 2f) * 0.5f) * env;
            });
        }

        static AudioClip Aircraft(float seconds)
        {
            return Make("aircraft", seconds, (t, p) =>
            {
                float env = Mathf.Min(1f, t * 2f) * Mathf.Min(1f, (seconds - t) * 1.2f);
                float prop = Mathf.Sin(t * 55f * Mathf.PI * 2f) * 0.35f;
                return (Rnd() * 0.35f + prop) * env * 0.7f;
            });
        }

        /// <summary>Проиграть звук в точке мира (2D, с ослаблением по расстоянию).</summary>
        public static void PlayAt(string clip, Vector3 pos, float volume = 1f, float pitch = 1f)
        {
            var cam = Camera.main;
            float dist = cam != null ? Vector3.Distance(cam.transform.position, pos) : 0f;
            if (dist > 400f) return;
            float vol = volume * Mathf.Clamp01(1f - dist / 400f);
            if (vol <= 0.01f) return;
            if (AudioBus.Instance != null) AudioBus.Instance.Play(clip, vol, pitch, dist);
        }

        public static void PlayUi(string clip, float volume = 1f)
        {
            if (AudioBus.Instance != null) AudioBus.Instance.Play(clip, volume, 1f, 0f);
        }
    }

    /// <summary>Пул AudioSource, чтобы не создавать объекты на каждый выстрел.</summary>
    public class AudioBus : MonoBehaviour
    {
        public static AudioBus Instance;
        readonly List<AudioSource> sources = new List<AudioSource>();
        int next;

        public static AudioBus Ensure()
        {
            if (Instance == null)
            {
                var go = new GameObject("~audio");
                Instance = go.AddComponent<AudioBus>();
                DontDestroyOnLoad(go);
            }
            return Instance;
        }

        AudioSource Take()
        {
            if (sources.Count < 24)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                sources.Add(s);
                return s;
            }
            next = (next + 1) % sources.Count;
            return sources[next];
        }

        public void Play(string clipName, float volume, float pitch, float distance)
        {
            var clip = AudioSynth.Get(clipName);
            if (clip == null) return;
            var src = Take();
            src.pitch = pitch * Random.Range(0.96f, 1.04f);
            src.volume = Mathf.Clamp01(volume);
            src.clip = clip;
            src.Play();
        }

        AudioSource engine;

        public void Engine(float load01)
        {
            if (engine == null)
            {
                engine = gameObject.AddComponent<AudioSource>();
                engine.playOnAwake = false;
                engine.loop = true;
                engine.clip = AudioSynth.Get("engine");
                engine.volume = 0f;
                engine.Play();
            }
            engine.volume = Mathf.Lerp(engine.volume, Mathf.Clamp01(load01) * 0.5f, Time.deltaTime * 4f);
            engine.pitch = Mathf.Lerp(0.75f, 1.5f, Mathf.Clamp01(load01));
        }
    }
}
