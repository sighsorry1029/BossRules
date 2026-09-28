using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BossRules;

// Free attempts until first personal victory. No currency/voucher ledger or key writes.
// A dedicated server need not have the remote dungeon's Unity objects loaded:
// the current object owner resolves the bowl, the server checks the character,
// and only that same owner can consume its short-lived approval.
internal static class PersonalSummonRuntime
{
    private const string BeginRpc = "BossRules PersonalSummon Begin v1";
    private const string AuthorizeRpc = "BossRules PersonalSummon Authorize v1";
    private const string DecisionRpc = "BossRules PersonalSummon Decision v1";
    private const string StatusRpc = "BossRules PersonalSummon Status v1";
    private const float TimeoutSeconds = 10f;
    private const float InteractionDistance = 8f;
    private static readonly AccessTools.FieldRef<ZRoutedRpc, long> RoutedId =
        AccessTools.FieldRefAccess<ZRoutedRpc, long>("m_id");
    private static readonly AccessTools.FieldRef<OfferingBowl, ZNetView> BowlView =
        AccessTools.FieldRefAccess<OfferingBowl, ZNetView>("m_nview");
    private delegate bool CanSpawn(OfferingBowl bowl, Vector3 point, out Vector3 result);
    private static readonly Func<OfferingBowl, Vector3> GetSpawnPosition =
        AccessTools.MethodDelegate<Func<OfferingBowl, Vector3>>(AccessTools.Method(typeof(OfferingBowl), "GetSpawnPosition"));
    private static readonly CanSpawn CanSpawnBoss =
        AccessTools.MethodDelegate<CanSpawn>(AccessTools.Method(typeof(OfferingBowl), "CanSpawnBoss"));
    private static readonly Action<OfferingBowl, Vector3> SpawnBoss =
        AccessTools.MethodDelegate<Action<OfferingBowl, Vector3>>(AccessTools.Method(typeof(OfferingBowl), "SpawnBoss"));
    private static readonly Dictionary<string, Pending> Requests = new();
    private static readonly List<string> Expired = new();
    private static ZRoutedRpc? _rpc;
    private static float _nextCleanup;
    private static float _nextLocalRequest;
    [ThreadStatic] private static OfferingBowl? _freeSpawn;

    private sealed class Pending
    {
        internal OfferingBowl Bowl = null!;
        internal ZNetView View = null!;
        internal ZDOID ViewId;
        internal ZDOID CharacterId;
        internal long Requester;
        internal int Kind;
        internal string Item = "";
        internal float Expires;
    }

    internal static bool Enabled => BossRulesConfig.IsPersonalProgressionSummonsEnabled() && YouAreNotWorthyBridge.Available;
    internal static bool IsFreeSpawn(OfferingBowl bowl) => ReferenceEquals(_freeSpawn, bowl);

    internal static void Update()
    {
        ZRoutedRpc? rpc = ZRoutedRpc.instance;
        if (!ReferenceEquals(_rpc, rpc))
        {
            Shutdown();
            _rpc = rpc;
            if (rpc != null)
            {
                rpc.Register<ZPackage>(BeginRpc, OnBegin);
                rpc.Register<ZPackage>(AuthorizeRpc, OnAuthorize);
                rpc.Register<string, bool>(DecisionRpc, OnDecision);
                rpc.Register<string>(StatusRpc, OnStatus);
            }
        }
        if (Requests.Count == 0 || Time.realtimeSinceStartup < _nextCleanup) return;
        _nextCleanup = Time.realtimeSinceStartup + 1f;
        Expired.Clear();
        foreach (var entry in Requests)
            if (entry.Value.Expires <= Time.realtimeSinceStartup || entry.Value.Bowl == null || entry.Value.View == null)
                Expired.Add(entry.Key);
        foreach (string token in Expired) Requests.Remove(token);
        Expired.Clear();
    }

    internal static void Shutdown()
    {
        Requests.Clear();
        Expired.Clear();
        _rpc = null;
        _nextCleanup = _nextLocalRequest = 0f;
        _freeSpawn = null;
    }

    private static int Kind(OfferingBowl bowl)
    {
        if (bowl == null || bowl.m_useItemStands || bowl.m_bossPrefab == null || bowl.m_bossItem == null
            || bowl.m_itemPrefab != null || !string.IsNullOrEmpty(bowl.m_setGlobalKey)) return 0;
        string boss = bowl.m_bossPrefab.name;
        if (boss == "SeekerQueen" && QueenDungeonAltarSupport.IsTargetOfferingBowl(bowl)
            && QueenDungeonAltarSupport.IsTargetGenerator(bowl.GetComponentInParent<DungeonGenerator>())) return 1;
        if (boss == "FrozenKing" && bowl.gameObject.name.Replace("(Clone)", "").Trim() == "offeraltar_FrozenKing_bossroom")
            return 2;
        return 0;
    }

    internal enum Eligibility { Paid, Free, Unavailable }

    // Only an explicit PersonalMissing qualifies. Unavailable is not absence and
    // must not accidentally charge a player while their snapshot is still syncing.
    internal static Eligibility Evaluate(int kind, Func<string, int> query)
    {
        if (kind != 1 && kind != 2) return Eligibility.Paid;
        int completion = query(kind == 1 ? "defeated_queen" : "defeated_frozenking_p3");
        if (completion == 0 || completion == 1) return Eligibility.Unavailable;
        if (completion != 2) return Eligibility.Paid;
        if (kind == 1) return Eligibility.Free;
        int gate = query("LastBossGate_Open");
        return gate == 3 ? Eligibility.Free : gate == 0 || gate == 1 ? Eligibility.Unavailable : Eligibility.Paid;
    }

    internal static string AppendHover(string text, OfferingBowl bowl)
    {
        if (!Enabled || Evaluate(Kind(bowl), key => YouAreNotWorthyBridge.Key(key)) != Eligibility.Free) return text;
        return text + "\n" + Localization.instance.Localize("[<color=yellow><b>$KEY_Use</b></color>] ")
               + BossRulesLocalization.Text("bossrules_personal_summon_free");
    }

    // true means this interaction was handled, including fail-closed pending/error states.
    internal static bool TryInteract(OfferingBowl bowl, Humanoid user)
    {
        if (!Enabled || user != Player.m_localPlayer) return false;
        AltarRuntime.ReconcileLooseOfferingBowl(bowl);
        int kind = Kind(bowl);
        Eligibility eligibility = Evaluate(kind, key => YouAreNotWorthyBridge.Key(key));
        if (eligibility == Eligibility.Paid) return false;
        if (eligibility == Eligibility.Unavailable)
        {
            ShowStatus("bossrules_personal_summon_unavailable");
            return true;
        }
        if (!YouAreNotWorthyBridge.CanUseItem(AltarRuntime.GetPrefabName(bowl.m_bossItem.gameObject), out string missing))
        {
            if (missing.Length > 0) YouAreNotWorthyBridge.ShowMissing(missing);
            else ShowStatus("bossrules_personal_summon_unavailable");
            return true;
        }
        Update();
        ZNetView? view = BowlView(bowl);
        if (_rpc == null || view == null || !view.IsValid() || !CanProceed(bowl))
        {
            ShowStatus("bossrules_personal_summon_unavailable");
            return true;
        }
        if (Time.realtimeSinceStartup < _nextLocalRequest) return true;
        _nextLocalRequest = Time.realtimeSinceStartup + 1f;
        ZPackage package = new();
        package.Write(view.GetZDO().m_uid);
        package.Write(AltarRuntime.GetRelativePath(view.transform, bowl.transform));
        package.Write(Player.m_localPlayer.GetZDOID());
        _rpc.InvokeRoutedRPC(view.GetZDO().GetOwner(), BeginRpc, package);
        ShowStatus("bossrules_personal_summon_request");
        return true;
    }

    private static bool CanProceed(OfferingBowl bowl) => bowl != null && !bowl.IsInvoking("DelayedSpawnBoss")
        && (!BossRulesConfig.IsAltarRulesEnabled() || !AltarRuntime.EvaluateOfferingBowlBlock(bowl));

    private static bool NearCharacter(long requester, ZDOID characterId, Vector3 position, out ZDO character)
    {
        character = ZDOMan.instance?.GetZDO(characterId)!;
        return character != null && character.IsValid() && character.GetPrefab() == "Player".GetStableHashCode()
            && character.GetOwner() == requester && !character.GetBool(ZDOVars.s_dead)
            && (character.GetPosition() - position).sqrMagnitude <= InteractionDistance * InteractionDistance;
    }

    private static void OnBegin(long sender, ZPackage package)
    {
        if (_rpc == null || !ReferenceEquals(_rpc, ZRoutedRpc.instance) || !Enabled || Requests.Count >= 64 || package.Size() > 4096) return;
        try
        {
            ZDOID viewId = package.ReadZDOID();
            string path = package.ReadString();
            ZDOID characterId = package.ReadZDOID();
            GameObject? root = ZNetScene.instance?.FindInstance(viewId);
            ZNetView? view = root?.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner() || path.Length > 1024) return;
            foreach (OfferingBowl bowl in root!.GetComponentsInChildren<OfferingBowl>(true))
            {
                if (BowlView(bowl) != view || AltarRuntime.GetRelativePath(view.transform, bowl.transform) != path) continue;
                AltarRuntime.ReconcileLooseOfferingBowl(bowl);
                int kind = Kind(bowl);
                if (kind == 0 || !CanProceed(bowl) || !NearCharacter(sender, characterId, bowl.transform.position, out _)) return;
                foreach (Pending existing in Requests.Values)
                    if (existing.Bowl == bowl && existing.Expires > Time.realtimeSinceStartup) return;
                string token = Guid.NewGuid().ToString("N");
                Pending request = new()
                {
                    Bowl = bowl, View = view, ViewId = viewId, Requester = sender, CharacterId = characterId,
                    Kind = kind, Item = AltarRuntime.GetPrefabName(bowl.m_bossItem.gameObject),
                    Expires = Time.realtimeSinceStartup + TimeoutSeconds
                };
                Requests.Add(token, request);
                ZPackage authorization = new();
                authorization.Write(token);
                authorization.Write(viewId);
                authorization.Write(sender);
                authorization.Write(characterId);
                authorization.Write(kind);
                authorization.Write(request.Item);
                authorization.Write(bowl.transform.position);
                ZRoutedRpc.instance.InvokeRoutedRPC(AuthorizeRpc, authorization);
                return;
            }
        }
        catch (Exception ex) { LogFailure(ex); }
    }

    private static void OnAuthorize(long owner, ZPackage package)
    {
        if (_rpc == null || !ReferenceEquals(_rpc, ZRoutedRpc.instance) || !BossRulesPlugin.IsRuntimeServer() || !Enabled || package.Size() > 4096) return;
        try
        {
            string token = package.ReadString();
            ZDOID viewId = package.ReadZDOID();
            long requester = package.ReadLong();
            ZDOID characterId = package.ReadZDOID();
            int kind = package.ReadInt();
            string item = package.ReadString();
            Vector3 position = package.ReadVector3();
            ZDO? root = ZDOMan.instance?.GetZDO(viewId);
            if (token.Length != 32 || item.Length > 256 || root == null || !root.IsValid() || root.GetOwner() != owner
                || !NearCharacter(requester, characterId, position, out _)) return;
            // The native owner supplies the live bowl position/item (including YAML
            // overrides). It is not trusted to assert the requester's key possession.
            int expectedRoot = kind == 1 ? QueenDungeonAltarSupport.GeneratorPrefabName.GetStableHashCode()
                : kind == 2 ? "offeraltar_FrozenKing_bossroom".GetStableHashCode() : 0;
            float rootDistance = kind == 1 ? 256f : 1f;
            if (expectedRoot == 0 || root.GetPrefab() != expectedRoot
                || !((root.GetPosition() - position).sqrMagnitude <= rootDistance * rootDistance)) return;
            bool local = IsServerSender(requester);
            ZNetPeer? peer = local ? null : ZNet.instance.GetPeer(requester);
            bool authenticated = local ? Player.m_localPlayer != null && Player.m_localPlayer.GetZDOID() == characterId
                : peer != null && peer.m_characterID == characterId;
            bool allowed = authenticated && Evaluate(kind, key => YouAreNotWorthyBridge.Key(key, peer)) == Eligibility.Free
                && YouAreNotWorthyBridge.CanUseItem(item, out _, peer);
            ZRoutedRpc.instance.InvokeRoutedRPC(owner, DecisionRpc, token, allowed);
            if (!allowed) ZRoutedRpc.instance.InvokeRoutedRPC(requester, StatusRpc, "bossrules_personal_summon_unavailable");
        }
        catch (Exception ex) { LogFailure(ex); }
    }

    private static void OnDecision(long sender, string token, bool allowed)
    {
        if (!IsServerSender(sender) || !Requests.TryGetValue(token, out Pending? request)) return;
        Requests.Remove(token); // Consume before any spawn/effect: duplicate replies cannot execute twice.
        if (!allowed || !Enabled || request.Expires <= Time.realtimeSinceStartup || request.View == null
            || !request.View.IsValid() || !request.View.IsOwner() || request.View.GetZDO().m_uid != request.ViewId
            || request.Bowl == null || BowlView(request.Bowl) != request.View
            || !NearCharacter(request.Requester, request.CharacterId, request.Bowl.transform.position, out _)) return;
        OfferingBowl bowl = request.Bowl;
        AltarRuntime.ReconcileLooseOfferingBowl(bowl);
        if (Kind(bowl) != request.Kind || AltarRuntime.GetPrefabName(bowl.m_bossItem.gameObject) != request.Item
            || !CanProceed(bowl)) return;
        try
        {
            if (!CanSpawnBoss(bowl, GetSpawnPosition(bowl), out Vector3 point)) return;
            OfferingBowl? previous = _freeSpawn;
            _freeSpawn = bowl;
            try { SpawnBoss(bowl, point); }
            finally { _freeSpawn = previous; }
        }
        catch (Exception ex) { LogFailure(ex); }
    }

    private static bool IsServerSender(long sender)
    {
        if (_rpc == null || !ReferenceEquals(_rpc, ZRoutedRpc.instance) || ZNet.instance == null) return false;
        return ZNet.instance.IsServer() ? sender == RoutedId(ZRoutedRpc.instance)
            : sender == ZNet.instance.GetServerPeer()?.m_uid;
    }

    private static void OnStatus(long sender, string key)
    {
        if (IsServerSender(sender) && key == "bossrules_personal_summon_unavailable") ShowStatus(key);
    }
    private static void ShowStatus(string key) => Player.m_localPlayer?.Message(MessageHud.MessageType.Center, BossRulesLocalization.Text(key));
    private static void LogFailure(Exception ex) => BossRulesPlugin.BossRulesLogger.LogWarning("Personal summon request failed: " + ex.Message);
}
