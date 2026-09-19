using System.Collections.Generic;
using UnityEngine;

namespace Samsar
{
    public enum LootKind { Ammo, Xp, Ability, Repair, Respawn, Shield, AirDrop }

    public class LootItem
    {
        public LootKind Kind;
        public int Ammo;
        public float Xp;
        public float RepairHp;
        public float ShieldSeconds;
        public string Label;
    }

    /// <summary>Таблица лута: ящики на карте, трофеи с техники, воздушный груз.</summary>
    public static class LootTable
    {
        public static LootItem Crate(int playerLevel)
        {
            float r = Random.value;
            if (r < 0.34f) return Ammo(Random.Range(6, 11), "Снаряды");
            if (r < 0.58f) return Xp(Random.Range(50f, 110f) * (1f + playerLevel * 0.08f), "Опыт");
            if (r < 0.76f) return Repair(300f, "Ремонт");
            if (r < 0.94f) return Ability("заряд умения");
            return Shield(12f, "Экран");
        }

        public static LootItem Trophy(int victimLevel, float victimMaxHp)
        {
            var list = new List<LootItem>();
            list.Add(Ammo(Mathf.RoundToInt(victimLevel * 2.2f) + Random.Range(4, 9), "Снаряды с трофея"));
            list.Add(Xp(70f + victimLevel * 55f, "Опыт с трофея"));
            list.Add(Repair(victimMaxHp * 0.3f, "Ремонт с трофея"));
            var pick = list[Random.Range(0, list.Count)];
            if (Random.value < 0.55f && victimLevel >= 4) pick = Ability("заряд умения с трофея");
            if (Random.value < 0.22f) pick = Respawn("Возрождение");
            return pick;
        }

        public static LootItem AirDrop()
        {
            float r = Random.value;
            if (r < 0.34f) return Respawn("Возрождение (+ добыча)");
            if (r < 0.6f) return Shield(20f, "Экран (+ добыча)");
            if (r < 0.82f) return Ability("заряд всех умений");
            return Xp(400f, "Крупный опыт");
        }

        public static LootItem Ammo(int count, string label)
        {
            return new LootItem { Kind = LootKind.Ammo, Ammo = count, Label = label };
        }

        public static LootItem Xp(float amount, string label)
        {
            return new LootItem { Kind = LootKind.Xp, Xp = amount, Label = label };
        }

        public static LootItem Repair(float hp, string label)
        {
            return new LootItem { Kind = LootKind.Repair, RepairHp = hp, Label = label };
        }

        public static LootItem Ability(string label)
        {
            return new LootItem { Kind = LootKind.Ability, Label = label };
        }

        public static LootItem Shield(float seconds, string label)
        {
            return new LootItem { Kind = LootKind.Shield, ShieldSeconds = seconds, Label = label };
        }

        public static LootItem Respawn(string label)
        {
            return new LootItem { Kind = LootKind.Respawn, Label = label };
        }

        public static Color ColorOf(LootKind kind)
        {
            switch (kind)
            {
                case LootKind.Ammo: return new Color(1f, 0.85f, 0.35f);
                case LootKind.Xp: return new Color(0.5f, 0.85f, 1f);
                case LootKind.Ability: return new Color(0.75f, 0.55f, 1f);
                case LootKind.Repair: return new Color(0.45f, 1f, 0.55f);
                case LootKind.Respawn: return new Color(1f, 0.45f, 0.45f);
                case LootKind.Shield: return new Color(0.4f, 0.75f, 1f);
                default: return new Color(0.4f, 1f, 0.7f);
            }
        }
    }
}
