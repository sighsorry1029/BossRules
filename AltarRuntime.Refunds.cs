using System;
using System.Collections.Generic;
using HarmonyLib;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace BossRules;

internal static partial class AltarRuntime
{
    private static readonly AccessTools.FieldRef<ZNetScene, Dictionary<ZDO, ZNetView>> SceneInstancesRef =
        AccessTools.FieldRefAccess<ZNetScene, Dictionary<ZDO, ZNetView>>("m_instances");
    private static readonly int AltarSummonKey = $"{BossRulesPlugin.ModName}.altar_summon".GetStableHashCode();
    private static readonly int AltarRefundsKey = $"{BossRulesPlugin.ModName}.altar_refunds".GetStableHashCode();
    private static readonly int AltarRefundPointKey = $"{BossRulesPlugin.ModName}.altar_refund_point".GetStableHashCode();
    private const float AltarSpawnMarkerMaxDistance = 128f;
    private const float AltarSpawnMarkerMaxDistanceSquared = AltarSpawnMarkerMaxDistance * AltarSpawnMarkerMaxDistance;
    private const float AltarSpawnMarkerRetrySeconds = 5f;
    private const float AltarSpawnMarkerRetryIntervalSeconds = 0.25f;
    private static readonly List<PendingAltarBossSpawn> PendingAltarBossSpawns = new();
    private static readonly List<PendingAltarBossSpawn> PendingAltarBossSpawnRemovals = new();
    private static float _nextAltarSpawnMarkerRetryAt;

    private sealed class PendingAltarBossSpawn
    {
        public string BossPrefabName { get; set; } = "";
        public int BossPrefabHash { get; set; }
        public Vector3 SpawnPoint { get; set; }
        public Vector3 RefundPoint { get; set; }
        public string RefundPayload { get; set; } = "";
        public float ExpiresAt { get; set; }
    }

    internal static void BeginOfferingBowlBossSpawnAttempt(OfferingBowl? offeringBowl, Vector3 spawnPoint)
    {
        if (ZNet.instance == null || offeringBowl?.m_bossPrefab == null)
        {
            return;
        }

        lock (Sync)
        {
            string refundPayload = ConsumePreparedOfferingRefundPayload(offeringBowl);
            QueueOfferingBowlBossSpawnAttemptLocked(offeringBowl, spawnPoint, refundPayload, 0f);
        }
    }

    internal static void PrepareOfferingBowlRefundPayload(OfferingBowl? offeringBowl)
    {
        if (ZNet.instance == null || offeringBowl == null)
        {
            return;
        }

        lock (Sync)
        {
            string refundPayload = BuildOfferingRefundPayload(offeringBowl);
            OfferingBowlRuntimeState state = GetOrAddOfferingBowlRuntimeState(offeringBowl);
            state.PendingRefundPayload = refundPayload;
        }
    }

    internal static void PrepareAndQueueOfferingBowlRefundPayload(OfferingBowl? offeringBowl, Vector3 spawnPoint)
    {
        if (ZNet.instance == null || offeringBowl?.m_bossPrefab == null)
        {
            return;
        }

        lock (Sync)
        {
            string refundPayload = BuildOfferingRefundPayload(offeringBowl);
            OfferingBowlRuntimeState state = GetOrAddOfferingBowlRuntimeState(offeringBowl);
            state.PendingRefundPayload = refundPayload;
            QueueOfferingBowlBossSpawnAttemptLocked(
                offeringBowl,
                spawnPoint,
                refundPayload,
                Math.Max(0f, offeringBowl.m_spawnBossDelay));
        }
    }

    internal static void FinalizeOfferingBowlBossSpawnAttempt()
    {
        if (ZNet.instance == null)
        {
            return;
        }

        lock (Sync)
        {
            TryMarkNearbyPendingAltarSummonsLocked();
        }
    }

    internal static void TryMarkAltarSummonedCharacter(Character? character)
    {
        if (ZNet.instance == null || character?.gameObject == null)
        {
            return;
        }

        ZNetView? nview = character.GetComponent<ZNetView>();
        if (nview == null || !nview.IsValid())
        {
            return;
        }

        lock (Sync)
        {
            TryMarkAltarSummonedCharacterLocked(character, nview.GetZDO());
        }
    }

    private static void QueueOfferingBowlBossSpawnAttemptLocked(
        OfferingBowl offeringBowl,
        Vector3 spawnPoint,
        string refundPayload,
        float extraLifetimeSeconds)
    {
        string bossPrefabName = GetPrefabName(offeringBowl.m_bossPrefab);
        int bossPrefabHash = bossPrefabName.GetStableHashCode();
        for (int index = PendingAltarBossSpawns.Count - 1; index >= 0; index--)
        {
            PendingAltarBossSpawn existing = PendingAltarBossSpawns[index];
            if (existing.BossPrefabHash == bossPrefabHash &&
                string.Equals(existing.RefundPayload, refundPayload, StringComparison.Ordinal) &&
                Vector3.SqrMagnitude(existing.SpawnPoint - spawnPoint) <= 1f)
            {
                PendingAltarBossSpawns.RemoveAt(index);
            }
        }

        PendingAltarBossSpawn pending = new()
        {
            BossPrefabName = bossPrefabName,
            BossPrefabHash = bossPrefabHash,
            SpawnPoint = spawnPoint,
            RefundPoint = offeringBowl.transform != null ? offeringBowl.transform.position : spawnPoint,
            RefundPayload = refundPayload,
            ExpiresAt = Time.time + AltarSpawnMarkerRetrySeconds + Math.Max(0f, extraLifetimeSeconds)
        };
        PendingAltarBossSpawns.Add(pending);
        _nextAltarSpawnMarkerRetryAt = 0f;
    }

    internal static void ProcessPendingAltarSummonMarkers()
    {
        if (ZNet.instance == null ||
            PendingAltarBossSpawns.Count == 0 ||
            Time.time < _nextAltarSpawnMarkerRetryAt)
        {
            return;
        }

        lock (Sync)
        {
            TryMarkNearbyPendingAltarSummonsLocked();
            _nextAltarSpawnMarkerRetryAt = Time.time + AltarSpawnMarkerRetryIntervalSeconds;
        }
    }

    private static void TryMarkNearbyPendingAltarSummonsLocked()
    {
        if (PendingAltarBossSpawns.Count == 0 || ZNetScene.instance == null)
        {
            return;
        }

        float now = Time.time;
        PendingAltarBossSpawnRemovals.Clear();
        foreach (PendingAltarBossSpawn pending in PendingAltarBossSpawns)
        {
            if (now >= pending.ExpiresAt)
            {
                PendingAltarBossSpawnRemovals.Add(pending);
                continue;
            }

            if (TryMarkNearbyPendingAltarSummonLocked(pending))
            {
                PendingAltarBossSpawnRemovals.Add(pending);
            }
        }

        foreach (PendingAltarBossSpawn pending in PendingAltarBossSpawnRemovals)
        {
            PendingAltarBossSpawns.Remove(pending);
        }

        PendingAltarBossSpawnRemovals.Clear();
    }

    private static bool TryMarkNearbyPendingAltarSummonLocked(PendingAltarBossSpawn pending)
    {
        foreach (KeyValuePair<ZDO, ZNetView> pair in SceneInstancesRef(ZNetScene.instance))
        {
            ZDO zdo = pair.Key;
            ZNetView nview = pair.Value;
            if (zdo == null || nview == null || nview.gameObject == null)
            {
                continue;
            }

            int prefabHash = zdo.GetPrefab();
            if (prefabHash != 0 && prefabHash != pending.BossPrefabHash)
            {
                continue;
            }

            if (Vector3.SqrMagnitude(zdo.GetPosition() - pending.SpawnPoint) > AltarSpawnMarkerMaxDistanceSquared)
            {
                continue;
            }

            if (!nview.TryGetComponent(out Character character))
            {
                continue;
            }

            if (prefabHash == 0 &&
                !string.Equals(GetPrefabName(character.gameObject), pending.BossPrefabName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryMarkAltarSummonedCharacterLocked(character, zdo, pending))
            {
                return true;
            }
        }

        return false;
    }

    internal static void TryMarkCreatedAltarSummonZdo(int prefabHashHint, ZDO? zdo)
    {
        if (ZNet.instance == null ||
            zdo == null ||
            zdo.m_uid.IsNone() ||
            zdo.GetBool(AltarSummonKey) ||
            PendingAltarBossSpawns.Count == 0)
        {
            return;
        }

        lock (Sync)
        {
            for (int index = PendingAltarBossSpawns.Count - 1; index >= 0; index--)
            {
                PendingAltarBossSpawn pending = PendingAltarBossSpawns[index];
                if (!TryMarkCreatedAltarSummonZdoLocked(prefabHashHint, zdo, pending))
                {
                    continue;
                }

                PendingAltarBossSpawns.RemoveAt(index);
                return;
            }
        }
    }

    private static bool TryMarkCreatedAltarSummonZdoLocked(int prefabHashHint, ZDO zdo, PendingAltarBossSpawn pending)
    {
        if (zdo.GetBool(AltarSummonKey))
        {
            return false;
        }

        int prefabHash = zdo.GetPrefab();
        if (prefabHash == 0)
        {
            prefabHash = prefabHashHint;
        }

        if (prefabHash == 0 || prefabHash != pending.BossPrefabHash)
        {
            return false;
        }

        Vector3 position = zdo.GetPosition();
        if (Vector3.SqrMagnitude(position - pending.SpawnPoint) > AltarSpawnMarkerMaxDistanceSquared)
        {
            return false;
        }

        zdo.Set(AltarSummonKey, true);
        zdo.Set(AltarRefundsKey, pending.RefundPayload);
        zdo.Set(AltarRefundPointKey, SerializeVector3(pending.RefundPoint));
        return true;
    }

    private static bool TryMarkAltarSummonedCharacterLocked(Character character, ZDO? zdo)
    {
        if (zdo == null || zdo.GetBool(AltarSummonKey))
        {
            return false;
        }

        for (int index = PendingAltarBossSpawns.Count - 1; index >= 0; index--)
        {
            PendingAltarBossSpawn pending = PendingAltarBossSpawns[index];
            if (!TryMarkAltarSummonedCharacterLocked(character, zdo, pending))
            {
                continue;
            }

            PendingAltarBossSpawns.RemoveAt(index);
            return true;
        }

        return false;
    }

    private static bool TryMarkAltarSummonedCharacterLocked(Character character, ZDO zdo, PendingAltarBossSpawn pending)
    {
        if (zdo.GetBool(AltarSummonKey))
        {
            return false;
        }

        if (zdo.GetPrefab() != pending.BossPrefabHash)
        {
            string characterPrefabName = GetPrefabName(character.gameObject);
            if (!string.Equals(characterPrefabName, pending.BossPrefabName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        Vector3 position = character.GetCenterPoint();
        if (Vector3.SqrMagnitude(position - pending.SpawnPoint) > AltarSpawnMarkerMaxDistanceSquared)
        {
            return false;
        }

        zdo.Set(AltarSummonKey, true);
        zdo.Set(AltarRefundsKey, pending.RefundPayload);
        zdo.Set(AltarRefundPointKey, SerializeVector3(pending.RefundPoint));
        DespawnRulesManager.TryTrackLoadedDespawnTarget(character);
        return true;
    }

    internal static bool IsAltarSummoned(ZDO zdo) => zdo.GetBool(AltarSummonKey);

    internal static bool TryResolveAltarSummonRefunds(ZDO? zdo, out IReadOnlyCollection<DespawnRefundDrop> refunds)
    {
        refunds = Array.Empty<DespawnRefundDrop>();
        if (zdo == null || !zdo.GetBool(AltarSummonKey))
        {
            return false;
        }

        string payload = zdo.GetString(AltarRefundsKey, "");
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        Vector3? refundPoint = null;
        string refundPointPayload = zdo.GetString(AltarRefundPointKey, "");
        if (!string.IsNullOrWhiteSpace(refundPointPayload))
        {
            if (TryDeserializeVector3(refundPointPayload, out Vector3 parsedRefundPoint))
            {
                refundPoint = parsedRefundPoint;
            }
        }

        List<DespawnRefundDrop> resolvedRefunds = new();
        foreach (string part in payload.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pieces = part.Split(new[] { ':' }, 2);
            string itemName = pieces.Length > 0 ? pieces[0].Trim() : "";
            if (itemName.Length == 0)
            {
                continue;
            }

            int amount = 1;
            if (pieces.Length > 1 && int.TryParse(pieces[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedAmount))
            {
                amount = Math.Max(1, parsedAmount);
            }

            GameObject? itemPrefab = ResolveItemPrefab(itemName, "altar auto refund");
            if (itemPrefab != null)
            {
                resolvedRefunds.Add(new DespawnRefundDrop(itemPrefab, amount, refundPoint));
            }
        }

        refunds = resolvedRefunds;
        return resolvedRefunds.Count > 0;
    }

    private static string BuildOfferingRefundPayload(OfferingBowl offeringBowl)
    {
        Dictionary<string, int> refunds = new(StringComparer.OrdinalIgnoreCase);
        if (offeringBowl.m_useItemStands)
        {
            foreach (ItemStand itemStand in AltarItemStandHoverInfoFormatter.FindRelevantItemStands(offeringBowl))
            {
                if (itemStand == null || !TryGetAttachedItemName(itemStand, out string attachedItem))
                {
                    continue;
                }

                if (attachedItem.Length > 0)
                {
                    AddRefund(refunds, attachedItem, 1);
                }
            }
        }
        else if (offeringBowl.m_bossItem != null)
        {
            string itemName = NormalizeReferencePrefabName(offeringBowl.m_bossItem.gameObject) ?? "";
            if (itemName.Length > 0)
            {
                AddRefund(refunds, itemName, Math.Max(1, offeringBowl.m_bossItems));
            }
        }

        return SerializeRefundPayload(refunds);
    }

    private static string ConsumePreparedOfferingRefundPayload(OfferingBowl offeringBowl)
    {
        OfferingBowlRuntimeState? state = offeringBowl.GetComponent<OfferingBowlRuntimeState>();
        string? preparedPayload = state?.PendingRefundPayload;
        if (preparedPayload != null)
        {
            state!.PendingRefundPayload = null;
            return preparedPayload;
        }

        return BuildOfferingRefundPayload(offeringBowl);
    }

    private static string SerializeRefundPayload(Dictionary<string, int> refunds)
    {
        return string.Join(
            ";",
            refunds
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"{pair.Key}:{pair.Value.ToString(CultureInfo.InvariantCulture)}"));
    }

    private static string SerializeVector3(Vector3 value)
    {
        return string.Join(
            ",",
            value.x.ToString("R", CultureInfo.InvariantCulture),
            value.y.ToString("R", CultureInfo.InvariantCulture),
            value.z.ToString("R", CultureInfo.InvariantCulture));
    }

    private static bool TryDeserializeVector3(string raw, out Vector3 value)
    {
        value = default;
        string[] parts = (raw ?? "").Split(',');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!float.TryParse(parts[0].Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(parts[1].Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float y) ||
            !float.TryParse(parts[2].Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float z))
        {
            return false;
        }

        value = new Vector3(x, y, z);
        return true;
    }

    private static bool TryGetAttachedItemName(ItemStand itemStand, out string itemName)
    {
        itemName = "";
        ZNetView? nview = itemStand != null ? itemStand.GetComponent<ZNetView>() : null;
        ZDO? zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
        if (zdo == null)
        {
            return false;
        }

        // Valheim 1.0 stores the attachment as a prefab hash. Keep our refund payload name-based.
        int itemHash = zdo.GetInt(ZDOVars.s_item, 0);
        if (itemHash == 0)
        {
            return false;
        }

        GameObject? prefab = ObjectDB.instance?.GetItemPrefab(itemHash)
            ?? ZNetScene.instance?.GetPrefab(itemHash);
        itemName = NormalizeReferencePrefabName(prefab) ?? "";
        return itemName.Length > 0;
    }

    private static void AddRefund(Dictionary<string, int> refunds, string itemName, int amount)
    {
        if (string.IsNullOrWhiteSpace(itemName) || amount <= 0)
        {
            return;
        }

        refunds[itemName] = refunds.TryGetValue(itemName, out int existing)
            ? existing + amount
            : amount;
    }
}
