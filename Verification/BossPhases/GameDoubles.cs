// Managed test doubles: actual production routing, configuration and refund code
// is source-linked. These types do not run Unity, transport packets or save worlds.
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x, a.y-b.y, a.z-b.z);
        public static float SqrMagnitude(Vector3 v) => v.x*v.x + v.y*v.y + v.z*v.z;
    }
    public sealed class Transform { public Vector3 position; }
    public class GameObject
    {
        public string name = "";
        public Transform transform = new();
        public readonly List<Component> Components = new();
        public T GetComponent<T>() where T : class => Components.OfType<T>().FirstOrDefault()!;
        public bool TryGetComponent<T>(out T component) where T : class => (component = GetComponent<T>()) != null;
        public T Add<T>(T component) where T : Component { component.gameObject = this; Components.Add(component); return component; }
        public int GetInstanceID() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public class Component
    {
        public GameObject gameObject = null!;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        public bool TryGetComponent<T>(out T component) where T : class => gameObject.TryGetComponent(out component);
    }
    public static class Time { public static float time; }
    public static class Mathf { public static float Clamp(float v, float min, float max) => Math.Clamp(v, min, max); }
}
public static class Utils { public static string GetPrefabName(UnityEngine.GameObject go) => go.name.Replace("(Clone)", ""); }
public static class StableHash
{
    public static int GetStableHashCode(this string value)
    {
        unchecked
        {
            int a = 5381, b = a;
            for (int i = 0; i < value.Length; i += 2)
            {
                a = ((a << 5) + a) ^ value[i];
                if (i == value.Length - 1) break;
                b = ((b << 5) + b) ^ value[i + 1];
            }
            return a + b * 1566083941;
        }
    }
}
public readonly record struct ZDOID(long UserID, uint ID)
{
    public bool IsNone() => UserID == 0 || ID == 0;
}
public class ZDO
{
    public ZDOID m_uid;
    public bool Owner = true;
    public bool Persistent = true;
    public int Prefab;
    public UnityEngine.Vector3 Position;
    public Dictionary<int, object> Values = new();
    public bool IsOwner() => Owner;
    public int GetPrefab() => Prefab;
    public UnityEngine.Vector3 GetPosition() => Position;
    public void Set(int key, bool value) => Values[key] = value;
    public void Set(int key, int value) => Values[key] = value;
    public void Set(int key, string value) => Values[key] = value;
    public bool GetBool(int key) => Values.TryGetValue(key, out object? value) && value is true;
    public int GetInt(int key, int fallback = 0) => Values.TryGetValue(key, out object? value) ? (int)value : fallback;
    public string GetString(int key, string fallback = "") => Values.TryGetValue(key, out object? value) ? (string)value : fallback;
    public static KeyValuePair<int,int> GetHashZDOID(string name) => new((name+"_u").GetStableHashCode(), (name+"_i").GetStableHashCode());
    public void Set(KeyValuePair<int,int> key, ZDOID value) { Values[key.Key] = value.UserID; Values[key.Value] = value.ID; }
    public ZDOID GetZDOID(KeyValuePair<int,int> key) => Values.TryGetValue(key.Key, out object? user) && Values.TryGetValue(key.Value, out object? id) ? new((long)user, (uint)id) : default;
    public ZDO Reload() => new() { m_uid = m_uid, Owner = Owner, Persistent = Persistent, Prefab = Prefab, Position = Position, Values = new(Values) };
}
public class ZNetView : UnityEngine.Component
{
    public ZDO Data = new();
    public bool m_persistent = true;
    public bool Valid = true;
    public bool IsValid() => Valid;
    public bool IsOwner() => Data.IsOwner();
    public ZDO GetZDO() => Data;
}
public class Character : UnityEngine.Component
{
    public bool Boss;
    public EffectList m_deathEffects = new();
    public bool IsBoss() => Boss;
    public UnityEngine.Vector3 GetCenterPoint() => transform.position;
    public virtual void OnDeath() { }
}
public class Ragdoll : UnityEngine.Component { public EffectList m_removeEffect = new(); private void DestroyNow() { } }
public class EffectList
{
    public class EffectData { public UnityEngine.GameObject m_prefab = null!; public bool m_enabled = true; }
    public EffectData[] m_effectPrefabs = Array.Empty<EffectData>();
    public UnityEngine.GameObject[] Create() => Array.Empty<UnityEngine.GameObject>();
}
public class ZNet { public static ZNet instance = new(); }
public class ZNetScene
{
    public static ZNetScene instance = new();
    public List<UnityEngine.GameObject> m_prefabs = new();
    public Dictionary<ZDO, ZNetView> m_instances = new();
    public UnityEngine.GameObject? GetPrefab(int hash) => m_prefabs.FirstOrDefault(p => p.name.GetStableHashCode() == hash);
    public UnityEngine.GameObject? GetPrefab(string name) => GetPrefab(name.GetStableHashCode());
    public int GetInstanceID() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
}
public class ObjectDB
{
    public static ObjectDB instance = new();
    public List<UnityEngine.GameObject> m_items = new();
    public UnityEngine.GameObject? GetItemPrefab(int hash) => m_items.FirstOrDefault(p => p.name.GetStableHashCode() == hash);
    public int GetInstanceID() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
}
public class ItemDrop : UnityEngine.Component { }
public class ItemStand : UnityEngine.Component { }
public static class ZDOVars { public static int s_item = "item".GetStableHashCode(); }
public class OfferingBowl : UnityEngine.Component
{
    public UnityEngine.GameObject m_bossPrefab = null!;
    public ItemDrop m_bossItem = null!;
    public int m_bossItems = 3;
    public float m_spawnBossDelay;
    public bool m_useItemStands;
}
namespace BossRules
{
    internal static class BossRulesPlugin
    {
        internal const string ModName = "BossRules";
        internal static string RulesYamlFilePath = "unused";
        internal static TestLogger BossRulesLogger = new();
    }
    internal sealed class TestLogger
    {
        internal readonly List<string> Warnings = new();
        internal void LogInfo(string value) { }
        internal void LogError(string value) => Warnings.Add(value);
        internal void LogWarning(string value) => Warnings.Add(value);
    }
    internal sealed class OfferingBowlRuntimeState : UnityEngine.Component
    {
        internal bool FreeSummonQueued;
        internal string? PendingRefundPayload;
    }
    internal static partial class AltarRuntime
    {
        private static readonly object Sync = new();
        private static string GetPrefabName(UnityEngine.GameObject go) => Utils.GetPrefabName(go);
        private static string? NormalizeReferencePrefabName(UnityEngine.GameObject? go) => go?.name;
        private static OfferingBowlRuntimeState GetOrAddOfferingBowlRuntimeState(OfferingBowl bowl) => bowl.GetComponent<OfferingBowlRuntimeState>() ?? bowl.gameObject.Add(new OfferingBowlRuntimeState());
        private static UnityEngine.GameObject? ResolveItemPrefab(string name, string source) => ObjectDB.instance.GetItemPrefab(name.GetStableHashCode());
    }
    internal static class AltarItemStandHoverInfoFormatter
    {
        internal static IEnumerable<ItemStand> FindRelevantItemStands(OfferingBowl bowl) => Array.Empty<ItemStand>();
    }
    internal sealed record DespawnRefundDrop(UnityEngine.GameObject Prefab, int Amount, UnityEngine.Vector3? DropPointOverride);
    internal static class DespawnRulesManager
    {
        internal static readonly List<Character> Tracked = new();
        internal static void TryTrackLoadedDespawnTarget(Character? character) { if (character != null) Tracked.Add(character); }
        internal static void ConfigureDefaults(float range, float delay) { }
        internal static void MarkBootstrapScanDirty() { }
        internal static void ResetRuntimeState() { Tracked.Clear(); }
    }
    internal static class BossTamedPressureRuntime
    {
        internal static void Configure(BossTamedPressureDefinition? rule) { }
        internal static void ResetRuntimeState() { }
    }
    internal static class InitialBossEncounter { internal static bool IsWaitingForPlayer(ZDO zdo) => false; }
}
