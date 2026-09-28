using System;
using System.Collections.Generic;
using System.Linq;

#pragma warning disable CS0649, CS0414
namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude => x * x + y * y + z * z;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
    }
    public class Transform { public Vector3 position; public string Path = "."; }
    public class GameObject
    {
        public string name = "";
        public Transform transform = new();
        public List<Component> Components = new();
        public T? GetComponent<T>() where T : class => Components.OfType<T>().FirstOrDefault();
        public T[] GetComponentsInChildren<T>(bool inactive) => Components.OfType<T>().ToArray();
        public T Add<T>(T component) where T : Component { component.gameObject = this; Components.Add(component); return component; }
    }
    public class Component
    {
        public GameObject gameObject = new();
        public Transform transform => gameObject.transform;
        public T? GetComponentInParent<T>() where T : class => gameObject.GetComponent<T>();
    }
    public static class Time { public static float realtimeSinceStartup; }
}
public struct ZDOID : IEquatable<ZDOID>
{
    public long UserID;
    public uint ID;
    public bool Equals(ZDOID other) => UserID == other.UserID && ID == other.ID;
    public override bool Equals(object? value) => value is ZDOID id && Equals(id);
    public override int GetHashCode() => HashCode.Combine(UserID, ID);
    public static bool operator ==(ZDOID a, ZDOID b) => a.Equals(b);
    public static bool operator !=(ZDOID a, ZDOID b) => !a.Equals(b);
}
public class ZDO
{
    public ZDOID m_uid;
    public long Owner;
    public int Prefab;
    public bool Dead;
    public UnityEngine.Vector3 Position;
    public bool IsValid() => true;
    public int GetPrefab() => Prefab;
    public long GetOwner() => Owner;
    public UnityEngine.Vector3 GetPosition() => Position;
    public bool GetBool(int key) => Dead;
}
public static class ZDOVars { public static int s_dead = 1; }
public static class StringHash { public static int GetStableHashCode(this string s) => s.GetHashCode(); }
public class ZNetView : UnityEngine.Component
{
    public ZDO Data = new();
    public bool Valid = true;
    public bool IsValid() => Valid;
    public bool IsOwner() => Data.Owner == ZRoutedRpc.instance.Id;
    public ZDO GetZDO() => Data;
}
public class DungeonGenerator : UnityEngine.Component { }
public class Humanoid : UnityEngine.Component { }
public class Player : Humanoid
{
    public static Player? m_localPlayer;
    public ZDOID Id;
    public ZDOID GetZDOID() => Id;
    public List<string> Messages = new();
    public void Message(MessageHud.MessageType type, string text) => Messages.Add(text);
}
public class ItemDrop : UnityEngine.Component { }
public class OfferingBowl : UnityEngine.Component
{
    private ZNetView m_nview;
    public bool m_useItemStands;
    public UnityEngine.GameObject m_bossPrefab = new();
    public ItemDrop m_bossItem = new();
    public ItemDrop? m_itemPrefab;
    public string m_setGlobalKey = "";
    public bool Queued, SpawnPossible = true, FreeContextAtSpawn;
    public int Spawns;
    public OfferingBowl(ZNetView view) { m_nview = view; }
    public bool IsInvoking(string method) => Queued;
    private UnityEngine.Vector3 GetSpawnPosition() => transform.position;
    private bool CanSpawnBoss(UnityEngine.Vector3 point, out UnityEngine.Vector3 result) { result = point; return SpawnPossible; }
    private void SpawnBoss(UnityEngine.Vector3 point)
    {
        FreeContextAtSpawn = BossRules.PersonalSummonRuntime.IsFreeSpawn(this);
        Spawns++;
        Queued = true;
    }
}
public class MessageHud { public enum MessageType { Center } }
public class Localization
{
    public static Localization instance = new();
    public string Localize(string value) => value;
}
public class ZPackage
{
    private readonly List<object> _data = new();
    private int _offset;
    public int Size() => _data.Count * 32;
    public void Write(object data) => _data.Add(data);
    public ZDOID ReadZDOID() => (ZDOID)_data[_offset++];
    public string ReadString() => (string)_data[_offset++];
    public long ReadLong() => (long)_data[_offset++];
    public int ReadInt() => (int)_data[_offset++];
    public UnityEngine.Vector3 ReadVector3() => (UnityEngine.Vector3)_data[_offset++];
}
public class ZRoutedRpc
{
    private long m_id = 10;
    public long Id { get => m_id; set => m_id = value; }
    public static ZRoutedRpc instance = new();
    public List<(long Target, string Method, object[] Args)> Sent = new();
    public void Register<T>(string name, Action<long, T> callback) { }
    public void Register<T, U>(string name, Action<long, T, U> callback) { }
    public void InvokeRoutedRPC(string name, params object[] args) => InvokeRoutedRPC(1, name, args);
    public void InvokeRoutedRPC(long target, string name, params object[] args) => Sent.Add((target, name, args));
}
public class ZNetPeer
{
    public long m_uid;
    public ZDOID m_characterID;
    public bool Authenticated = true;
}
public class ZNet
{
    public static ZNet instance = new();
    public bool Server;
    public Dictionary<long, ZNetPeer> Peers = new();
    public bool IsServer() => Server;
    public ZNetPeer? GetPeer(long id) => Peers.GetValueOrDefault(id);
    public ZNetPeer GetServerPeer() => new() { m_uid = 1 };
}
public class ZDOMan
{
    public static ZDOMan instance = new();
    public Dictionary<ZDOID, ZDO> Objects = new();
    public ZDO? GetZDO(ZDOID id) => Objects.GetValueOrDefault(id);
}
public class ZNetScene
{
    public static ZNetScene instance = new();
    public Dictionary<ZDOID, UnityEngine.GameObject> Objects = new();
    public UnityEngine.GameObject? FindInstance(ZDOID id) => Objects.GetValueOrDefault(id);
}
namespace BossRules
{
    public static class BossRulesConfig
    {
        public static bool Enabled = true;
        public static bool IsPersonalProgressionSummonsEnabled() => Enabled;
        public static bool IsAltarRulesEnabled() => true;
    }
    public static class BossRulesPlugin
    {
        public static bool IsRuntimeServer() => ZNet.instance.Server;
        public static Logger BossRulesLogger = new();
    }
    public class Logger { public void LogWarning(string text) { } }
    public static class BossRulesLocalization { public static string Text(string key) => key; }
    public static class AltarRuntime
    {
        public static bool Block;
        public static bool EvaluateOfferingBowlBlock(OfferingBowl bowl) => Block;
        public static void ReconcileLooseOfferingBowl(OfferingBowl bowl) { }
        public static string GetPrefabName(UnityEngine.GameObject obj) => obj.name;
        public static string GetRelativePath(UnityEngine.Transform root, UnityEngine.Transform target) => target.Path;
    }
    public static class QueenDungeonAltarSupport
    {
        public const string GeneratorPrefabName = "DG_DvergrBoss";
        public static bool IsTargetOfferingBowl(OfferingBowl bowl) => bowl.gameObject.name == "offeraltar_queen";
        public static bool IsTargetGenerator(DungeonGenerator? generator) => generator != null;
    }
}
namespace YouAreNotWorthy
{
    public enum KeyQueryResult : byte { Invalid, Unavailable, PersonalMissing, PersonalPresent, SharedMissing, SharedPresent }
    public enum ItemUseQueryResult : byte { Invalid, Unavailable, Allowed, MissingRequirement }
    public static class YouAreNotWorthyApi
    {
        public static Dictionary<string, int> Keys = new();
        public static ItemUseQueryResult ItemResult = ItemUseQueryResult.Allowed;
        public static string ItemRequirement = "";
        public static KeyQueryResult QueryLocal(string key) => (KeyQueryResult)Keys.GetValueOrDefault(key, 2);
        public static KeyQueryResult QueryPeer(ZNetPeer peer, string key) => peer.Authenticated ? QueryLocal(key) : KeyQueryResult.Unavailable;
        public static ItemUseQueryResult QueryLocalItemUse(string item, out string key) { key = ItemRequirement; return ItemResult; }
        public static ItemUseQueryResult QueryPeerItemUse(ZNetPeer peer, string item, out string key)
        { key = ItemRequirement; return peer.Authenticated ? ItemResult : ItemUseQueryResult.Unavailable; }
        public static bool TryShowLocalMissingRequirement(string key) => true;
    }
}
