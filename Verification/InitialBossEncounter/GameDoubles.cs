// Isolated policy tests only: no Unity execution, real network transport or world IO.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
    public class Transform { public Vector3 position; }
    public class GameObject { public string name = ""; }
    public static class Time { public static float time; }
    public static class Mathf { public static float Abs(float value) => Math.Abs(value); }
}
public static class StableHash
{
    public static int GetStableHashCode(this string text)
    {
        unchecked
        {
            int a = 5381, b = a;
            for (int i = 0; i < text.Length; i += 2)
            {
                a = ((a << 5) + a) ^ text[i];
                if (i == text.Length - 1) break;
                b = ((b << 5) + b) ^ text[i + 1];
            }
            return a + b * 1566083941;
        }
    }
}
public static class Utils { public static string GetPrefabName(UnityEngine.GameObject obj) => obj.name; }
public class ZoneSystem { public enum SpawnMode { Full, Ghost, Client } }
public class ZDO
{
    public readonly Dictionary<int, int> Values = new();
    public string m_uid = Guid.NewGuid().ToString();
    public bool Owner = true;
    public bool Altar;
    public bool IsOwner() => Owner;
    public int GetInt(int key) => Values.TryGetValue(key, out int value) ? value : 0;
    public void Set(int key, int value) => Values[key] = value;
    public ZDO Reload()
    {
        ZDO copy = new() { m_uid = m_uid, Owner = Owner, Altar = Altar };
        foreach (var entry in Values) copy.Values.Add(entry.Key, entry.Value);
        return copy;
    }
}
public class ZNetView
{
    public ZDO Data = new();
    public bool Valid = true;
    public bool IsValid() => Valid;
    public bool IsOwner() => Data.Owner;
    public ZDO GetZDO() => Data;
}
public class Character
{
    public ZNetView View = new();
    public float Health = 100;
    public UnityEngine.GameObject gameObject = new();
    public UnityEngine.Transform transform = new();
    public T? GetComponent<T>() where T : class => View as T;
    public float GetHealth() => Health;
}
public class Player : Character
{
    public static readonly List<Player> Players = new();
    public long ID = 1;
    public bool IsDead() => Health <= 0;
    public long GetPlayerID() => ID;
    public static List<Player> GetAllPlayers() => Players;
}
namespace BossRules
{
    internal static class BossRulesPlugin { internal const string ModName = "BossRules"; }
    internal static class AltarRuntime { internal static bool IsAltarSummoned(ZDO zdo) => zdo.Altar; }
    internal static class BossRulesRuntime
    {
        internal static float Range = 64;
        internal static bool Ready = true;
        internal static bool IsDespawnTrackingRuleLookupReady() => Ready;
        internal static float GetInitialBossEncounterRange(ZDO zdo) => Range;
    }
}
