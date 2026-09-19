// Звук: процедурная генерация (выстрел, попадание, рикошет, взрыв, гусеницы, зона).
// Не требует внешних файлов: клипы синтезируются при запуске. Позже можно заменить
// бесплатными библиотеками звуков — точки подключения здесь же (Play*).
using System.Collections.Generic;
using UnityEngine;

namespace Samsar
{
    public static class Sfx
    {
        static AudioSource oneShot;
        static AudioClip shotLight, shotHeavy, impactPen, impactBounce, explosion, zoneBeep, loot, ability;
        static bool ready;
        static readonly Dictionary<TankController, AudioSource> engines = new Dictionary<TankController, AudioSource>();
        static AudioClip engineClip;
        const int Rate = 44100;

        static void Ensure()
        {
            if (ready) return;
            ready = true;
            var go = new GameObject("~Audio");
            Object.DontDestroyOnLoad(go);
            oneShot = go.AddComponent<AudioSource>();
            oneShot.spatialBlend = 0f;
            oneShot.volume = 0.7f;

            shotLight = Synth("shot_light", 0.45f, t => Noise(t) * Mathf.Exp(-t * 16f) * 0.9f + Tone(t, 90f - 40f * t) * 0.5f);
            shotHeavy = Synth("shot_heavy", 0.7f, t => Noise(t) * Mathf.Exp(-t * 8f) + Tone(t, 60f - 25f * t) * 0.8f);
            impactPen = Synth("impact_pen", 0.35f, t => Noise(t) * Mathf.Exp(-t * 28f) * 0.8f + Tone(t, 220f) * 0.2f);
            impactBounce = Synth("impact_bounce", 0.3f, t => Tone(t, 1400f - 600f * t) * Mathf.Exp(-t * 9f) * 0.6f);
            explosion = Synth("explosion", 1.6f, t => (Noise(t) + Tone(t, 45f) * 1.4f) * Mathf.Exp(-t * 3.2f));
            zoneBeep = Synth("zone_beep", 0.5f, t => Tone(t, 900f) * (Mathf.Sin(t * 40f) > 0f ? 1f : 0f) * 0.5f);
            loot = Synth("loot", 0.25f, t => (Tone(t, 700f) + Tone(t, 1050f) * 0.6f) * Mathf.Exp(-t * 12f) * 0.5f);
            ability = Synth("ability", 0.9f, t => Tone(t, 300f + 400f * t) * Mathf.Exp(-t * 3f) * 0.6f);
            engineClip = Synth("engine", 1.0f, t => Tone(t, 55f) * 0.5f + Tone(t, 110f) * 0.2f + Noise(t) * 0.15f, loop: true);
        }

        static float Noise(float t) { return Random.value * 2f - 1f; }
        static float Tone(float t, float freq) { return Mathf.Sin(2f * Mathf.PI * freq * t); }

        static AudioClip Synth(string name, float seconds, System.Func<float, float> generator, bool loop = false)
        {
            int samples = Mathf.Max(64, Mathf.RoundToInt(Rate * seconds));
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
                data[i] = Mathf.Clamp(generator(i / (float)Rate), -1f, 1f) * 0.9f;
            var clip = AudioClip.Create(name, samples, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // ---------- точки подключения (сюда можно поставить файлы библиотек) ----------
        public static void Shot(Vector3 pos, TankClass cls)
        {
            Ensure();
            var clip = (cls == TankClass.LT || cls == TankClass.MT) ? shotLight : shotHeavy;
            PlayAt(pos, clip, 1f);
        }

        public static void Impact(Vector3 pos, bool penetrated)
        {
            Ensure();
            PlayAt(pos, penetrated ? impactPen : impactBounce, penetrated ? 0.9f : 0.7f);
        }

        public static void Explosion(Vector3 pos, float volume = 1f)
        {
            Ensure();
            PlayAt(pos, explosion, volume);
        }

        public static void Loot() { Ensure(); oneShot.PlayOneShot(loot, 0.5f); }
        public static void Ability() { Ensure(); oneShot.PlayOneShot(ability, 0.7f); }
        public static void ZoneWarning() { Ensure(); oneShot.PlayOneShot(zoneBeep, 0.5f); }

        static void PlayAt(Vector3 pos, AudioClip clip, float volume)
        {
            var cam = Camera.main;
            if (cam == null) { oneShot.PlayOneShot(clip, volume); return; }
            float dist = Vector3.Distance(cam.transform.position, pos);
            if (dist > 400f) return;
            float att = Mathf.Clamp01(1f - dist / 400f);
            oneShot.PlayOneShot(clip, volume * att * att);
        }

        /// <summary>Рокот двигателя: тон меняется от скорости.</summary>
        public static void AttachEngine(TankController tank)
        {
            Ensure();
            if (tank == null || engines.ContainsKey(tank)) return;
            var src = tank.gameObject.AddComponent<AudioSource>();
            src.clip = engineClip;
            src.loop = true;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 6f;
            src.maxDistance = 180f;
            src.volume = tank.IsPlayer ? 0.35f : 0.5f;
            src.Play();
            engines[tank] = src;
        }

        public static void UpdateEngine(TankController tank, float speed01)
        {
            AudioSource src;
            if (!engines.TryGetValue(tank, out src) || src == null) return;
            var rb = tank.GetComponent<Rigidbody>();
            float speed = rb != null ? rb.velocity.magnitude : 0f;
            float targetPitch = 0.75f + Mathf.Clamp01(speed / Mathf.Max(1f, tank.spec.maxSpeed)) * 1.1f;
            src.pitch = Mathf.Lerp(src.pitch, targetPitch, Time.deltaTime * 2f);
            src.volume = tank.IsPlayer ? Mathf.Lerp(0.2f, 0.5f, Mathf.Clamp01(speed / 10f))
                                       : src.volume;
            if (tank.Dead) src.volume = Mathf.Lerp(src.volume, 0f, Time.deltaTime * 2f);
        }

        public static void SetMasterVolume(float v) { Ensure(); oneShot.volume = Mathf.Clamp01(v) * 0.7f; }
    }
}
