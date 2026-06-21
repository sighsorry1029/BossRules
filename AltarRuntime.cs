using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BossRules;

internal static partial class AltarRuntime
{
    private static readonly object Sync = new();
    private static readonly int OfferingBowlLastUseTicksKey = $"{BossRulesPlugin.ModName}.offering_bowl_last_use_ticks".GetStableHashCode();
    private static readonly Dictionary<string, List<AltarConfigurationEntry>> ActiveEntriesByPrefab = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<Location, string> RegisteredLocationPrefabs = new();
    private static readonly Dictionary<string, List<AuthoredItemStandSlotTemplate>> AuthoredItemStandSlotsByPrefab = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<ItemStand, string> LooseItemStandAuthoredPathsByInstance = new();
    private static bool _pendingGameDataReapply;
    private static bool _loggedPendingGameDataWait;

    private sealed class AuthoredItemStandSlotTemplate
    {
        public string Path { get; set; } = "";
        public Vector3 OfferingBowlLocalOffset { get; set; }
    }

    internal enum OfferingBowlBlockReason
    {
        None = 0,
        SameBossNearby,
        RespawnCooldownActive,
    }

    internal readonly struct OfferingBowlBlockResult
    {
        internal static OfferingBowlBlockResult None => default;

        public OfferingBowlBlockResult(bool blocked, OfferingBowlBlockReason reason)
        {
            Blocked = blocked;
            Reason = reason;
        }

        public bool Blocked { get; }
        public OfferingBowlBlockReason Reason { get; }
    }

    internal static void Reload(IReadOnlyList<AltarConfigurationEntry> entries)
    {
        lock (Sync)
        {
            ActiveEntriesByPrefab.Clear();
            AuthoredItemStandSlotsByPrefab.Clear();
            LooseItemStandAuthoredPathsByInstance.Clear();
            AltarItemStandHoverInfoFormatter.ClearRuntimeCaches();
            _pendingGameDataReapply = true;
            _loggedPendingGameDataWait = false;
            foreach (AltarConfigurationEntry entry in entries)
            {
                if (!entry.Enabled || !HasOverride(entry))
                {
                    continue;
                }

                if (!ActiveEntriesByPrefab.TryGetValue(entry.Prefab, out List<AltarConfigurationEntry>? bucket))
                {
                    bucket = new List<AltarConfigurationEntry>();
                    ActiveEntriesByPrefab[entry.Prefab] = bucket;
                }

                bucket.Add(entry);
            }

            BossRulesDebugLog.Client(
                $"Altar reload entries={entries.Count} activePrefabs={ActiveEntriesByPrefab.Count} gameDataReady={IsGameDataReady()} registeredLocations={RegisteredLocationPrefabs.Count}.");
            ReapplyRegisteredLocationsLocked();
        }
    }

    internal static void Shutdown()
    {
        lock (Sync)
        {
            foreach (KeyValuePair<Location, string> pair in RegisteredLocationPrefabs.ToList())
            {
                Location location = pair.Key;
                if (location != null)
                {
                    RestoreRoot(location.transform);
                }
            }

            RegisteredLocationPrefabs.Clear();
            ActiveEntriesByPrefab.Clear();
            AuthoredItemStandSlotsByPrefab.Clear();
            LooseItemStandAuthoredPathsByInstance.Clear();
            AltarItemStandHoverInfoFormatter.ClearRuntimeCaches();
            _pendingGameDataReapply = false;
            _loggedPendingGameDataWait = false;
        }
    }

    internal static void ProcessDeferredReapply()
    {
        lock (Sync)
        {
            if (!_pendingGameDataReapply)
            {
                return;
            }

            if (!IsGameDataReady())
            {
                if (!_loggedPendingGameDataWait)
                {
                    _loggedPendingGameDataWait = true;
                    BossRulesDebugLog.Client($"Altar deferred reapply waiting for game data. {DescribeGameDataState()}");
                }

                return;
            }

            _pendingGameDataReapply = false;
            _loggedPendingGameDataWait = false;
            BossRulesDebugLog.Client(
                $"Altar deferred reapply running. {DescribeGameDataState()} registeredLocations={RegisteredLocationPrefabs.Count} activePrefabs={ActiveEntriesByPrefab.Count}.");
            ReapplyRegisteredLocationsLocked();
            ReapplyLoadedLooseOfferingBowlsLocked();
            ReapplyLoadedLooseItemStandsLocked();
        }
    }

    internal static bool HasConfiguredPrefab(string prefabName)
    {
        lock (Sync)
        {
            return ActiveEntriesByPrefab.ContainsKey((prefabName ?? "").Trim());
        }
    }

    // Location and loose component reconciliation.
    internal static void RegisterLocation(Location? location)
    {
        if (location == null)
        {
            return;
        }

        lock (Sync)
        {
            if (!AltarLocationResolver.TryResolveLocationPrefabName(location, out string prefabName))
            {
                BossRulesDebugLog.Client($"Altar register location skipped: prefab unresolved location={location.name}.");
                return;
            }

            RegisteredLocationPrefabs[location] = prefabName;
            BossRulesDebugLog.Client($"Altar registered location prefab={prefabName} location={location.name}.");
            ReconcileRootLocked(location.transform, prefabName);
        }
    }

    internal static void UnregisterLocation(Location? location)
    {
        if (location == null)
        {
            return;
        }

        lock (Sync)
        {
            RegisteredLocationPrefabs.Remove(location);
        }
    }

    internal static void ReconcileSpawnedLocationRoot(GameObject? rootObject, string prefabName)
    {
        if (rootObject == null)
        {
            return;
        }

        string normalizedPrefab = (prefabName ?? "").Trim();
        if (normalizedPrefab.Length == 0)
        {
            return;
        }

        lock (Sync)
        {
            RefreshRegisteredLocationPrefabsLocked(rootObject.transform, normalizedPrefab);
            ReconcileRootLocked(rootObject.transform, normalizedPrefab);
        }
    }

    private static void RefreshRegisteredLocationPrefabsLocked(Transform root, string prefabName)
    {
        Location[] locations = root.GetComponentsInChildren<Location>(true);
        foreach (Location location in locations)
        {
            if (location != null)
            {
                RegisteredLocationPrefabs[location] = prefabName;
            }
        }

        if (locations.Length > 0)
        {
            BossRulesDebugLog.Client(
                $"Altar refreshed spawned location prefab cache prefab={prefabName} root={root.name} locations={locations.Length}.");
        }
    }

    internal static void ReconcileLooseOfferingBowl(OfferingBowl? offeringBowl)
    {
        if (offeringBowl == null || offeringBowl.GetComponentInParent<Location>(true) != null)
        {
            return;
        }

        lock (Sync)
        {
            if (!AltarItemStandHoverInfoFormatter.TryResolveOfferingBowlContext(offeringBowl, out string prefabName, out Transform root))
            {
                BossRulesDebugLog.Client($"Altar loose offering bowl skipped: context unresolved bowl={offeringBowl.name}.");
                return;
            }

            BossRulesDebugLog.Client($"Altar loose offering bowl context prefab={prefabName} root={root.name} bowl={offeringBowl.name}.");
            ReconcileRootLocked(root, prefabName);
        }
    }

    internal static void ReconcileLooseItemStand(ItemStand? itemStand)
    {
        lock (Sync)
        {
            ReconcileLooseItemStandLocked(itemStand);
        }
    }

    internal static OfferingBowlBlockResult EvaluateOfferingBowlBlock(OfferingBowl? offeringBowl)
    {
        lock (Sync)
        {
            if (offeringBowl == null || ZNet.instance == null)
            {
                return OfferingBowlBlockResult.None;
            }

            if (BossRulesManager.ShouldBlockConfiguredSameBossSpawn(offeringBowl.m_bossPrefab, offeringBowl.transform.position))
            {
                return new OfferingBowlBlockResult(true, OfferingBowlBlockReason.SameBossNearby);
            }

            OfferingBowlRuntimeState? state = offeringBowl.GetComponent<OfferingBowlRuntimeState>();
            if (state == null || state.RespawnMinutes <= 0f)
            {
                return OfferingBowlBlockResult.None;
            }

            long lastUseTicks = GetOfferingBowlLastUseTicks(offeringBowl, state);
            if (lastUseTicks <= 0L)
            {
                return OfferingBowlBlockResult.None;
            }

            TimeSpan elapsed = ZNet.instance.GetTime() - new DateTime(lastUseTicks);
            return elapsed.TotalMinutes >= state.RespawnMinutes
                ? OfferingBowlBlockResult.None
                : new OfferingBowlBlockResult(true, OfferingBowlBlockReason.RespawnCooldownActive);
        }
    }

    internal static void NotifyOfferingBowlBlocked(OfferingBowl offeringBowl, Humanoid? user, OfferingBowlBlockResult result)
    {
        if (offeringBowl == null || user == null || !result.Blocked)
        {
            return;
        }

        user.Message(MessageHud.MessageType.Center, Localization.instance.Localize(offeringBowl.m_cantOfferText));
    }

    internal static void MarkOfferingBowlUsed(OfferingBowl? offeringBowl)
    {
        lock (Sync)
        {
            if (offeringBowl == null || ZNet.instance == null)
            {
                return;
            }

            OfferingBowlRuntimeState? state = offeringBowl.GetComponent<OfferingBowlRuntimeState>();
            if (state == null || state.RespawnMinutes <= 0f)
            {
                return;
            }

            long nowTicks = ZNet.instance.GetTime().Ticks;
            state.LocalLastUseTicks = nowTicks;

            ZNetView? view = offeringBowl.GetComponentInParent<ZNetView>();
            if (view == null || !view.IsValid())
            {
                return;
            }

            if (!view.IsOwner())
            {
                view.ClaimOwnership();
            }

            if (view.IsOwner())
            {
                view.GetZDO().Set(OfferingBowlLastUseTicksKey, nowTicks);
            }
        }
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
            QueueOfferingBowlBossSpawnAttemptLocked(offeringBowl, spawnPoint, refundPayload, 0f, "started");
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
            BossRulesDebugLog.Client(
                $"Altar refund prepared altar='{offeringBowl.name}' useItemStands={offeringBowl.m_useItemStands} payload='{FormatRefundPayloadForLog(refundPayload)}'.");
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
            BossRulesDebugLog.Client(
                $"Altar refund prepared altar='{offeringBowl.name}' useItemStands={offeringBowl.m_useItemStands} payload='{FormatRefundPayloadForLog(refundPayload)}'.");
            QueueOfferingBowlBossSpawnAttemptLocked(
                offeringBowl,
                spawnPoint,
                refundPayload,
                Math.Max(0f, offeringBowl.m_spawnBossDelay),
                "queued");
        }
    }

    internal static void FinalizeOfferingBowlBossSpawnAttempt(OfferingBowl? offeringBowl, Vector3 spawnPoint)
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

    internal static string GetPrefabName(GameObject? gameObject)
    {
        if (gameObject == null)
        {
            return "";
        }

        ZNetView? nview = gameObject.GetComponent<ZNetView>();
        ZDO? zdo = nview?.GetZDO();
        if (zdo != null && ZNetScene.instance != null)
        {
            GameObject? prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            if (prefab != null)
            {
                return prefab.name;
            }
        }

        string prefabName = Utils.GetPrefabName(gameObject);
        if (!string.IsNullOrWhiteSpace(prefabName))
        {
            return prefabName;
        }

        return TrimCloneSuffix(gameObject.name);
    }

    private static void ReapplyRegisteredLocationsLocked()
    {
        BossRulesDebugLog.Client($"Altar reapply registered locations count={RegisteredLocationPrefabs.Count}.");
        foreach (KeyValuePair<Location, string> pair in RegisteredLocationPrefabs.ToList())
        {
            Location location = pair.Key;
            string prefabName = pair.Value;
            if (location == null)
            {
                RegisteredLocationPrefabs.Remove(pair.Key);
                continue;
            }

            ReconcileRootLocked(location.transform, prefabName);
        }
    }

    private static void ReapplyLoadedLooseOfferingBowlsLocked()
    {
        int scanned = 0;
        int applied = 0;
        int unresolved = 0;
        foreach (OfferingBowl offeringBowl in UnityEngine.Object.FindObjectsByType<OfferingBowl>(FindObjectsSortMode.None))
        {
            scanned++;
            if (offeringBowl == null || offeringBowl.GetComponentInParent<Location>(true) != null)
            {
                continue;
            }

            if (AltarItemStandHoverInfoFormatter.TryResolveOfferingBowlContext(offeringBowl, out string prefabName, out Transform root))
            {
                applied++;
                ReconcileRootLocked(root, prefabName);
            }
            else
            {
                unresolved++;
            }
        }

        BossRulesDebugLog.Client($"Altar reapply loose offering bowls scanned={scanned} applied={applied} unresolved={unresolved}.");
    }

    private static void ReapplyLoadedLooseItemStandsLocked()
    {
        int scanned = 0;
        int applied = 0;
        foreach (ItemStand itemStand in UnityEngine.Object.FindObjectsByType<ItemStand>(FindObjectsSortMode.None))
        {
            scanned++;
            if (ReconcileLooseItemStandLocked(itemStand))
            {
                applied++;
            }
        }

        BossRulesDebugLog.Client($"Altar reapply loose item stands scanned={scanned} applied={applied}.");
    }

    private static bool ReconcileLooseItemStandLocked(ItemStand? itemStand)
    {
        if (itemStand == null || itemStand.GetComponentInParent<Location>(true) != null)
        {
            return false;
        }

        if (!AltarItemStandHoverInfoFormatter.TryGetRelevantOfferingBowl(itemStand, out OfferingBowl? offeringBowl) ||
            offeringBowl == null)
        {
            BossRulesDebugLog.Client($"Altar loose itemStand skipped: offeringBowl unresolved stand={itemStand.name}.");
            return false;
        }

        Location? location = offeringBowl.GetComponentInParent<Location>(true);
        if (location != null)
        {
            if (!RegisteredLocationPrefabs.TryGetValue(location, out string? prefabName) &&
                AltarLocationResolver.TryResolveLocationPrefabName(location, out prefabName))
            {
                RegisteredLocationPrefabs[location] = prefabName;
            }

            if (string.IsNullOrWhiteSpace(prefabName))
            {
                BossRulesDebugLog.Client($"Altar loose itemStand skipped: location prefab unresolved stand={itemStand.name} bowl={offeringBowl.name}.");
                return false;
            }

            BossRulesDebugLog.Client($"Altar loose itemStand reapplying location prefab={prefabName} stand={itemStand.name} bowl={offeringBowl.name}.");
            ReconcileRootLocked(location.transform, prefabName);
            return true;
        }

        if (AltarItemStandHoverInfoFormatter.TryResolveOfferingBowlContext(offeringBowl, out string loosePrefabName, out Transform root))
        {
            BossRulesDebugLog.Client($"Altar loose itemStand reapplying loose root prefab={loosePrefabName} stand={itemStand.name} bowl={offeringBowl.name}.");
            ReconcileRootLocked(root, loosePrefabName);
            return true;
        }

        BossRulesDebugLog.Client($"Altar loose itemStand skipped: context unresolved stand={itemStand.name} bowl={offeringBowl.name}.");
        return false;
    }

    private static void ReconcileRootLocked(Transform? root, string prefabName)
    {
        if (root == null)
        {
            return;
        }

        string normalizedPrefab = (prefabName ?? "").Trim();
        if (normalizedPrefab.Length == 0)
        {
            return;
        }

        if (!IsGameDataReady())
        {
            _pendingGameDataReapply = true;
            BossRulesDebugLog.Client($"Altar reconcile deferred prefab={normalizedPrefab} root={root.name}. {DescribeGameDataState()}");
            return;
        }

        OfferingBowl[] offeringBowls = root.GetComponentsInChildren<OfferingBowl>(true);
        ItemStand[] childItemStands = root.GetComponentsInChildren<ItemStand>(true);
        CaptureSnapshots(offeringBowls, childItemStands);
        RestoreComponents(offeringBowls, childItemStands);

        if (!BossRulesConfig.IsAltarRulesEnabled() ||
            !ActiveEntriesByPrefab.TryGetValue(normalizedPrefab, out List<AltarConfigurationEntry>? entries))
        {
            BossRulesDebugLog.Client(
                $"Altar reconcile restore-only prefab={normalizedPrefab} root={root.name} enabled={BossRulesConfig.IsAltarRulesEnabled()} configured={ActiveEntriesByPrefab.ContainsKey(normalizedPrefab)} bowls={offeringBowls.Length} childItemStands={childItemStands.Length}.");
            return;
        }

        OfferingBowl? offeringBowl = offeringBowls.FirstOrDefault(bowl => bowl != null);
        Dictionary<string, ItemStand> childItemStandsByPath = BuildItemStandLookup(root, childItemStands);
        BossRulesDebugLog.Client(
            $"Altar reconcile applying prefab={normalizedPrefab} root={root.name} entries={entries.Count} bowls={offeringBowls.Length} childItemStands={childItemStands.Length} childPaths={childItemStandsByPath.Count} offeringBowl={(offeringBowl != null ? offeringBowl.name : "<none>")}.");

        foreach (AltarConfigurationEntry entry in entries)
        {
            if (entry.OfferingBowl != null && offeringBowl != null)
            {
                ApplyOfferingBowl(offeringBowl, entry.OfferingBowl, normalizedPrefab);
            }

            if (entry.ItemStands is { Count: > 0 })
            {
                List<ItemStand> relevantItemStands = GetRelevantItemStands(offeringBowl, childItemStands);
                ApplyConfiguredItemStands(entry.ItemStands, relevantItemStands, childItemStandsByPath, normalizedPrefab, root, offeringBowl);
            }
        }
    }

    private static bool IsGameDataReady()
    {
        return ZoneSystem.instance != null &&
               ObjectDB.instance?.m_items is { Count: > 0 } &&
               ZNetScene.instance?.m_prefabs is { Count: > 0 };
    }

    private static string DescribeGameDataState()
    {
        string zoneSystemState = ZoneSystem.instance != null ? "ready" : "null";
        int objectDbItems = ObjectDB.instance?.m_items?.Count ?? -1;
        int znetScenePrefabs = ZNetScene.instance?.m_prefabs?.Count ?? -1;
        return $"ZoneSystem={zoneSystemState} ObjectDB.items={objectDbItems} ZNetScene.prefabs={znetScenePrefabs}";
    }

    // Snapshot/restore keeps live altar edits reversible across reloads.
    private static void CaptureSnapshots(IEnumerable<OfferingBowl> offeringBowls, IEnumerable<ItemStand> itemStands)
    {
        foreach (OfferingBowl offeringBowl in offeringBowls)
        {
            if (offeringBowl == null)
            {
                continue;
            }

            OfferingBowlRuntimeState state = GetOrAddOfferingBowlRuntimeState(offeringBowl);
            state.Snapshot ??= CaptureOfferingBowlSnapshot(offeringBowl);
        }

        foreach (ItemStand itemStand in itemStands)
        {
            if (itemStand == null)
            {
                continue;
            }

            ItemStandRuntimeState state = GetOrAddItemStandRuntimeState(itemStand);
            state.Snapshot ??= CaptureItemStandSnapshot(itemStand);
        }
    }

    private static void RestoreRoot(Transform root)
    {
        RestoreComponents(
            root.GetComponentsInChildren<OfferingBowl>(true),
            root.GetComponentsInChildren<ItemStand>(true));
    }

    private static void RestoreComponents(IEnumerable<OfferingBowl> offeringBowls, IEnumerable<ItemStand> itemStands)
    {
        foreach (OfferingBowl offeringBowl in offeringBowls)
        {
            OfferingBowlRuntimeState? state = offeringBowl != null ? offeringBowl.GetComponent<OfferingBowlRuntimeState>() : null;
            if (state?.Snapshot == null || !state.Applied)
            {
                continue;
            }

            RestoreOfferingBowl(offeringBowl!, state.Snapshot);
            state.Applied = false;
            state.RespawnMinutes = 0f;
        }

        foreach (ItemStand itemStand in itemStands)
        {
            ItemStandRuntimeState? state = itemStand != null ? itemStand.GetComponent<ItemStandRuntimeState>() : null;
            if (state?.Snapshot == null || !state.Applied)
            {
                continue;
            }

            RestoreItemStand(itemStand!, state.Snapshot);
            state.Applied = false;
        }
    }

    // Apply configured OfferingBowl and ItemStand overrides.
    private static void ApplyOfferingBowl(OfferingBowl offeringBowl, AltarOfferingBowlDefinition entry, string prefabName)
    {
        string context = $"{prefabName}@offeringBowl";

        if (entry.BossItem != null)
        {
            offeringBowl.m_bossItem = ResolveItemDrop(entry.BossItem, $"{context}/bossItem");
        }

        if (entry.BossItems.HasValue)
        {
            offeringBowl.m_bossItems = Math.Max(1, entry.BossItems.Value);
        }

        if (entry.BossPrefab != null)
        {
            offeringBowl.m_bossPrefab = ResolveSpawnPrefab(entry.BossPrefab, $"{context}/bossPrefab");
        }

        if (entry.ItemPrefab != null)
        {
            offeringBowl.m_itemPrefab = ResolveItemDrop(entry.ItemPrefab, $"{context}/itemPrefab");
        }

        if (entry.SetGlobalKey != null)
        {
            offeringBowl.m_setGlobalKey = entry.SetGlobalKey;
        }

        if (entry.RenderSpawnAreaGizmos.HasValue)
        {
            offeringBowl.m_renderSpawnAreaGizmos = entry.RenderSpawnAreaGizmos.Value;
        }

        if (entry.AlertOnSpawn.HasValue)
        {
            offeringBowl.m_alertOnSpawn = entry.AlertOnSpawn.Value;
        }

        if (entry.SpawnBossDelay.HasValue)
        {
            offeringBowl.m_spawnBossDelay = Mathf.Max(0f, entry.SpawnBossDelay.Value);
        }

        if (entry.SpawnBossDistance?.Min is float minDistance)
        {
            offeringBowl.m_spawnBossMinDistance = Mathf.Max(0f, minDistance);
        }

        if (entry.SpawnBossDistance?.Max is float maxDistance)
        {
            offeringBowl.m_spawnBossMaxDistance = Mathf.Max(0f, maxDistance);
        }

        if (entry.SpawnBossMaxYDistance.HasValue)
        {
            offeringBowl.m_spawnBossMaxYDistance = Mathf.Max(0f, entry.SpawnBossMaxYDistance.Value);
        }

        if (entry.GetSolidHeightMargin.HasValue)
        {
            offeringBowl.m_getSolidHeightMargin = Math.Max(0, entry.GetSolidHeightMargin.Value);
        }

        if (entry.EnableSolidHeightCheck.HasValue)
        {
            offeringBowl.m_enableSolidHeightCheck = entry.EnableSolidHeightCheck.Value;
        }

        if (entry.SpawnPointClearingRadius.HasValue)
        {
            offeringBowl.m_spawnPointClearingRadius = Mathf.Max(0f, entry.SpawnPointClearingRadius.Value);
        }

        if (entry.SpawnYOffset.HasValue)
        {
            offeringBowl.m_spawnYOffset = entry.SpawnYOffset.Value;
        }

        if (entry.UseItemStands.HasValue)
        {
            offeringBowl.m_useItemStands = entry.UseItemStands.Value;
        }

        if (entry.ItemStandPrefix != null)
        {
            offeringBowl.m_itemStandPrefix = entry.ItemStandPrefix;
        }

        if (entry.ItemStandMaxRange.HasValue)
        {
            offeringBowl.m_itemstandMaxRange = Mathf.Max(0f, entry.ItemStandMaxRange.Value);
        }

        OfferingBowlRuntimeState state = GetOrAddOfferingBowlRuntimeState(offeringBowl);
        state.Applied = true;
        state.RespawnMinutes = entry.RespawnMinutes.HasValue ? Mathf.Max(0f, entry.RespawnMinutes.Value) : 0f;
        BossRulesDebugLog.Client(
            $"Altar offeringBowl applied context={context} bossPrefab={(offeringBowl.m_bossPrefab != null ? GetPrefabName(offeringBowl.m_bossPrefab) : "<null>")} useItemStands={offeringBowl.m_useItemStands} prefix='{offeringBowl.m_itemStandPrefix}' maxRange={offeringBowl.m_itemstandMaxRange:0.##} respawnMinutes={state.RespawnMinutes:0.##}.");
    }

    private static void ApplyConfiguredItemStands(
        IReadOnlyList<AltarItemStandDefinition> definitions,
        IReadOnlyList<ItemStand> relevantItemStands,
        Dictionary<string, ItemStand> childItemStandsByPath,
        string prefabName,
        Transform root,
        OfferingBowl? offeringBowl)
    {
        HashSet<int> exactMatchedItemStandIds = new();
        List<AltarItemStandDefinition> pathDefinitions = new();
        HashSet<string> unresolvedPaths = new(StringComparer.Ordinal);

        foreach (AltarItemStandDefinition definition in definitions)
        {
            string path = (definition.Path ?? "").Trim();
            if (path.Length == 0)
            {
                foreach (ItemStand relevantItemStand in relevantItemStands)
                {
                    ApplyItemStand(relevantItemStand, definition, prefabName, root);
                }

                continue;
            }

            pathDefinitions.Add(definition);
            if (childItemStandsByPath.TryGetValue(path, out ItemStand? matchedItemStand))
            {
                exactMatchedItemStandIds.Add(matchedItemStand.GetInstanceID());
                CaptureAuthoredItemStandSlot(prefabName, path, matchedItemStand, offeringBowl);
                BossRulesDebugLog.Client($"Altar itemStand exact path match prefab={prefabName} path='{path}' stand={matchedItemStand.name}.");
                ApplyItemStand(matchedItemStand, definition, prefabName, root);
                continue;
            }

            unresolvedPaths.Add(path);
        }

        if (offeringBowl == null || pathDefinitions.Count == 0)
        {
            foreach (string unresolvedPath in unresolvedPaths)
            {
                WarnInvalidEntry($"Entry '{prefabName}@itemStands[{unresolvedPath}]' references a missing ItemStand path.");
            }

            return;
        }

        List<ItemStand> unmatchedRelevantItemStands = relevantItemStands
            .Where(itemStand => itemStand != null && !exactMatchedItemStandIds.Contains(itemStand.GetInstanceID()))
            .ToList();
        if (unmatchedRelevantItemStands.Count == 0)
        {
            foreach (string unresolvedPath in unresolvedPaths)
            {
                WarnInvalidEntry($"Entry '{prefabName}@itemStands[{unresolvedPath}]' references a missing ItemStand path.");
            }

            return;
        }

        foreach (ItemStand itemStand in unmatchedRelevantItemStands)
        {
            LooseItemStandAuthoredPathsByInstance.Remove(itemStand);
        }

        TryStampLooseItemStandAuthoredPaths(offeringBowl, prefabName, unmatchedRelevantItemStands);
        foreach (AltarItemStandDefinition definition in pathDefinitions)
        {
            string path = (definition.Path ?? "").Trim();
            ItemStand? mappedItemStand = unmatchedRelevantItemStands.FirstOrDefault(itemStand =>
                LooseItemStandAuthoredPathsByInstance.TryGetValue(itemStand, out string? authoredPath) &&
                string.Equals(authoredPath, path, StringComparison.Ordinal));
            if (mappedItemStand == null)
            {
                if (unresolvedPaths.Contains(path))
                {
                    WarnInvalidEntry($"Entry '{prefabName}@itemStands[{path}]' references a missing ItemStand path.");
                }

                continue;
            }

            unresolvedPaths.Remove(path);
            BossRulesDebugLog.Client($"Altar itemStand authored path remap prefab={prefabName} path='{path}' stand={mappedItemStand.name}.");
            ApplyItemStand(mappedItemStand, definition, prefabName, root);
        }
    }

    private static void ApplyItemStand(ItemStand itemStand, AltarItemStandDefinition entry, string prefabName, Transform root)
    {
        string context = string.IsNullOrWhiteSpace(entry.Path)
            ? $"{prefabName}@itemStands"
            : $"{prefabName}@itemStands[{entry.Path}]";
        List<ItemDrop>? resolvedSupportedItems = null;
        ItemStandRuntimeState state = GetOrAddItemStandRuntimeState(itemStand);
        state.Snapshot ??= CaptureItemStandSnapshot(itemStand);

        if (entry.CanBeRemoved.HasValue)
        {
            itemStand.m_canBeRemoved = entry.CanBeRemoved.Value;
        }

        if (entry.AutoAttach.HasValue)
        {
            itemStand.m_autoAttach = entry.AutoAttach.Value;
        }

        if (entry.OrientationType != null &&
            Enum.TryParse(entry.OrientationType, true, out ItemStand.Orientation orientation))
        {
            itemStand.m_orientationType = orientation;
        }

        if (entry.SupportedTypes != null)
        {
            itemStand.m_supportedTypes = ResolveItemStandTypes(entry.SupportedTypes, $"{context}/supportedTypes");
        }

        if (entry.SupportedItems != null)
        {
            resolvedSupportedItems = ResolveItemDropList(entry.SupportedItems, $"{context}/supportedItems");
            itemStand.m_supportedItems = resolvedSupportedItems;
            BossRulesDebugLog.Client(
                $"Altar itemStand supportedItems resolved context={context} requested=[{FormatNames(entry.SupportedItems)}] resolved=[{FormatItemDrops(resolvedSupportedItems)}].");
        }

        if (entry.UnsupportedItems != null)
        {
            itemStand.m_unsupportedItems = ResolveItemDropList(entry.UnsupportedItems, $"{context}/unsupportedItems");
            BossRulesDebugLog.Client(
                $"Altar itemStand unsupportedItems resolved context={context} requested=[{FormatNames(entry.UnsupportedItems)}] resolved=[{FormatItemDrops(itemStand.m_unsupportedItems)}].");
        }
        else if (resolvedSupportedItems != null)
        {
            RemoveSupportedItemsFromUnsupportedList(itemStand, resolvedSupportedItems);
        }

        if (entry.PowerActivationDelay.HasValue)
        {
            itemStand.m_powerActivationDelay = Mathf.Max(0f, entry.PowerActivationDelay.Value);
        }

        if (entry.GuardianPower != null)
        {
            itemStand.m_guardianPower = ResolveStatusEffect(entry.GuardianPower, $"{context}/guardianPower");
        }

        state.Applied = true;
        BossRulesDebugLog.Client(
            $"Altar itemStand applied context={context} stand={itemStand.name} path='{GetRelativePath(root, itemStand.transform)}' autoAttach={itemStand.m_autoAttach} supported=[{FormatItemDrops(itemStand.m_supportedItems)}] unsupported=[{FormatItemDrops(itemStand.m_unsupportedItems)}] applied={state.Applied}.");
    }

    private static List<ItemStand> GetRelevantItemStands(OfferingBowl? offeringBowl, IEnumerable<ItemStand> childItemStands)
    {
        List<ItemStand> relevant = new();
        HashSet<int> seen = new();
        foreach (ItemStand itemStand in childItemStands)
        {
            if (itemStand != null && seen.Add(itemStand.GetInstanceID()))
            {
                relevant.Add(itemStand);
            }
        }

        if (offeringBowl == null || !offeringBowl.m_useItemStands)
        {
            return relevant;
        }

        foreach (ItemStand itemStand in AltarItemStandHoverInfoFormatter.FindRelevantItemStands(offeringBowl))
        {
            if (itemStand != null && seen.Add(itemStand.GetInstanceID()))
            {
                relevant.Add(itemStand);
            }
        }

        return relevant;
    }

    private static Dictionary<string, ItemStand> BuildItemStandLookup(Transform root, IEnumerable<ItemStand> itemStands)
    {
        Dictionary<string, ItemStand> lookup = new(StringComparer.Ordinal);
        foreach (ItemStand itemStand in itemStands)
        {
            if (itemStand != null)
            {
                lookup[GetRelativePath(root, itemStand.transform)] = itemStand;
            }
        }

        return lookup;
    }

    private static void CaptureAuthoredItemStandSlot(
        string prefabName,
        string configuredPath,
        ItemStand itemStand,
        OfferingBowl? offeringBowl)
    {
        if (offeringBowl == null || itemStand == null)
        {
            return;
        }

        string normalizedPrefab = (prefabName ?? "").Trim();
        string path = (configuredPath ?? "").Trim();
        if (normalizedPrefab.Length == 0 || path.Length == 0)
        {
            return;
        }

        if (!AuthoredItemStandSlotsByPrefab.TryGetValue(normalizedPrefab, out List<AuthoredItemStandSlotTemplate>? slots))
        {
            slots = new List<AuthoredItemStandSlotTemplate>();
            AuthoredItemStandSlotsByPrefab[normalizedPrefab] = slots;
        }

        Vector3 offset = offeringBowl.transform.InverseTransformPoint(itemStand.transform.position);
        int existingIndex = slots.FindIndex(slot => string.Equals(slot.Path, path, StringComparison.Ordinal));
        AuthoredItemStandSlotTemplate slotTemplate = new()
        {
            Path = path,
            OfferingBowlLocalOffset = offset
        };
        if (existingIndex >= 0)
        {
            slots[existingIndex] = slotTemplate;
        }
        else
        {
            slots.Add(slotTemplate);
        }
    }

    // Authored path remapping lets loose ItemStand instances keep reference paths.
    private static void TryStampLooseItemStandAuthoredPaths(
        OfferingBowl offeringBowl,
        string prefabName,
        IReadOnlyList<ItemStand> relevantItemStands)
    {
        CleanupLooseItemStandAuthoredPaths();
        string normalizedPrefab = (prefabName ?? "").Trim();
        if (offeringBowl == null ||
            normalizedPrefab.Length == 0 ||
            !AuthoredItemStandSlotsByPrefab.TryGetValue(normalizedPrefab, out List<AuthoredItemStandSlotTemplate>? templates) ||
            templates.Count == 0 ||
            relevantItemStands.Count == 0)
        {
            BossRulesDebugLog.Client(
                $"Altar authored path remap skipped prefab={normalizedPrefab} templates={(AuthoredItemStandSlotsByPrefab.TryGetValue(normalizedPrefab, out List<AuthoredItemStandSlotTemplate>? existingTemplates) ? existingTemplates.Count : 0)} relevant={relevantItemStands.Count}.");
            return;
        }

        HashSet<int> assignedItemStandIds = new();
        HashSet<string> assignedPaths = new(StringComparer.Ordinal);
        foreach (ItemStand itemStand in relevantItemStands)
        {
            if (itemStand == null)
            {
                continue;
            }

            if (!LooseItemStandAuthoredPathsByInstance.TryGetValue(itemStand, out string? assignedPath) ||
                string.IsNullOrWhiteSpace(assignedPath) ||
                !templates.Any(template => string.Equals(template.Path, assignedPath, StringComparison.Ordinal)))
            {
                continue;
            }

            assignedItemStandIds.Add(itemStand.GetInstanceID());
            assignedPaths.Add(assignedPath);
        }

        List<(float Distance, ItemStand ItemStand, AuthoredItemStandSlotTemplate Template)> candidates = new();
        foreach (ItemStand itemStand in relevantItemStands)
        {
            if (itemStand == null || assignedItemStandIds.Contains(itemStand.GetInstanceID()))
            {
                continue;
            }

            Vector3 itemStandOffset = offeringBowl.transform.InverseTransformPoint(itemStand.transform.position);
            foreach (AuthoredItemStandSlotTemplate template in templates)
            {
                if (assignedPaths.Contains(template.Path))
                {
                    continue;
                }

                float distance = Vector3.SqrMagnitude(itemStandOffset - template.OfferingBowlLocalOffset);
                candidates.Add((distance, itemStand, template));
            }
        }

        BossRulesDebugLog.Client(
            $"Altar authored path remap candidates prefab={normalizedPrefab} templates={templates.Count} relevant={relevantItemStands.Count} candidates={candidates.Count}.");
        candidates.Sort((left, right) => left.Distance.CompareTo(right.Distance));
        foreach ((float _, ItemStand itemStand, AuthoredItemStandSlotTemplate template) in candidates)
        {
            int itemStandId = itemStand.GetInstanceID();
            if (assignedItemStandIds.Contains(itemStandId) || assignedPaths.Contains(template.Path))
            {
                continue;
            }

            LooseItemStandAuthoredPathsByInstance[itemStand] = template.Path;
            assignedItemStandIds.Add(itemStandId);
            assignedPaths.Add(template.Path);
            BossRulesDebugLog.Client($"Altar authored path assigned prefab={normalizedPrefab} path='{template.Path}' stand={itemStand.name}.");
        }
    }

    private static void CleanupLooseItemStandAuthoredPaths()
    {
        List<ItemStand>? destroyed = null;
        foreach (ItemStand itemStand in LooseItemStandAuthoredPathsByInstance.Keys)
        {
            if (itemStand != null && itemStand.gameObject != null)
            {
                continue;
            }

            destroyed ??= new List<ItemStand>();
            destroyed.Add(itemStand!);
        }

        if (destroyed == null)
        {
            return;
        }

        foreach (ItemStand itemStand in destroyed)
        {
            LooseItemStandAuthoredPathsByInstance.Remove(itemStand);
        }
    }

    internal static OfferingBowlSnapshot CaptureOfferingBowlSnapshot(OfferingBowl offeringBowl)
    {
        return new OfferingBowlSnapshot
        {
            BossItem = NormalizeReferencePrefabName(offeringBowl.m_bossItem != null ? offeringBowl.m_bossItem.gameObject : null) ?? "",
            BossItems = offeringBowl.m_bossItems,
            BossPrefab = NormalizeReferencePrefabName(offeringBowl.m_bossPrefab) ?? "",
            ItemPrefab = NormalizeReferencePrefabName(offeringBowl.m_itemPrefab != null ? offeringBowl.m_itemPrefab.gameObject : null) ?? "",
            SetGlobalKey = offeringBowl.m_setGlobalKey,
            RenderSpawnAreaGizmos = offeringBowl.m_renderSpawnAreaGizmos,
            AlertOnSpawn = offeringBowl.m_alertOnSpawn,
            SpawnBossDelay = offeringBowl.m_spawnBossDelay,
            SpawnBossMaxDistance = offeringBowl.m_spawnBossMaxDistance,
            SpawnBossMinDistance = offeringBowl.m_spawnBossMinDistance,
            SpawnBossMaxYDistance = offeringBowl.m_spawnBossMaxYDistance,
            GetSolidHeightMargin = offeringBowl.m_getSolidHeightMargin,
            EnableSolidHeightCheck = offeringBowl.m_enableSolidHeightCheck,
            SpawnPointClearingRadius = offeringBowl.m_spawnPointClearingRadius,
            SpawnYOffset = offeringBowl.m_spawnYOffset,
            UseItemStands = offeringBowl.m_useItemStands,
            ItemStandPrefix = offeringBowl.m_itemStandPrefix,
            ItemStandMaxRange = offeringBowl.m_itemstandMaxRange
        };
    }

    internal static ItemStandSnapshot CaptureItemStandSnapshot(ItemStand itemStand)
    {
        return new ItemStandSnapshot
        {
            CanBeRemoved = itemStand.m_canBeRemoved,
            AutoAttach = itemStand.m_autoAttach,
            OrientationType = itemStand.m_orientationType.ToString(),
            SupportedTypes = itemStand.m_supportedTypes.Select(type => type.ToString()).ToList(),
            SupportedItems = itemStand.m_supportedItems
                .Where(item => item != null)
                .Select(item => NormalizeReferencePrefabName(item.gameObject) ?? "")
                .Where(name => name.Length > 0)
                .ToList(),
            UnsupportedItems = itemStand.m_unsupportedItems
                .Where(item => item != null)
                .Select(item => NormalizeReferencePrefabName(item.gameObject) ?? "")
                .Where(name => name.Length > 0)
                .ToList(),
            PowerActivationDelay = itemStand.m_powerActivationDelay,
            GuardianPower = itemStand.m_guardianPower != null ? itemStand.m_guardianPower.name : ""
        };
    }

    private static void RestoreOfferingBowl(OfferingBowl offeringBowl, OfferingBowlSnapshot snapshot)
    {
        offeringBowl.m_bossItem = ResolveItemDrop(snapshot.BossItem, null);
        offeringBowl.m_bossItems = snapshot.BossItems;
        offeringBowl.m_bossPrefab = ResolveSpawnPrefab(snapshot.BossPrefab, null);
        offeringBowl.m_itemPrefab = ResolveItemDrop(snapshot.ItemPrefab, null);
        offeringBowl.m_setGlobalKey = snapshot.SetGlobalKey;
        offeringBowl.m_renderSpawnAreaGizmos = snapshot.RenderSpawnAreaGizmos;
        offeringBowl.m_alertOnSpawn = snapshot.AlertOnSpawn;
        offeringBowl.m_spawnBossDelay = snapshot.SpawnBossDelay;
        offeringBowl.m_spawnBossMaxDistance = snapshot.SpawnBossMaxDistance;
        offeringBowl.m_spawnBossMinDistance = snapshot.SpawnBossMinDistance;
        offeringBowl.m_spawnBossMaxYDistance = snapshot.SpawnBossMaxYDistance;
        offeringBowl.m_getSolidHeightMargin = snapshot.GetSolidHeightMargin;
        offeringBowl.m_enableSolidHeightCheck = snapshot.EnableSolidHeightCheck;
        offeringBowl.m_spawnPointClearingRadius = snapshot.SpawnPointClearingRadius;
        offeringBowl.m_spawnYOffset = snapshot.SpawnYOffset;
        offeringBowl.m_useItemStands = snapshot.UseItemStands;
        offeringBowl.m_itemStandPrefix = snapshot.ItemStandPrefix;
        offeringBowl.m_itemstandMaxRange = snapshot.ItemStandMaxRange;
    }

    private static void RestoreItemStand(ItemStand itemStand, ItemStandSnapshot snapshot)
    {
        itemStand.m_canBeRemoved = snapshot.CanBeRemoved;
        itemStand.m_autoAttach = snapshot.AutoAttach;
        if (Enum.TryParse(snapshot.OrientationType, true, out ItemStand.Orientation orientation))
        {
            itemStand.m_orientationType = orientation;
        }

        itemStand.m_supportedTypes = snapshot.SupportedTypes
            .Select(ParseItemStandType)
            .Where(type => type.HasValue)
            .Select(type => type!.Value)
            .ToList();
        itemStand.m_supportedItems = ResolveItemDropList(snapshot.SupportedItems, null);
        itemStand.m_unsupportedItems = ResolveItemDropList(snapshot.UnsupportedItems, null);
        itemStand.m_powerActivationDelay = snapshot.PowerActivationDelay;
        itemStand.m_guardianPower = ResolveStatusEffect(snapshot.GuardianPower, null);
    }

    private static OfferingBowlRuntimeState GetOrAddOfferingBowlRuntimeState(OfferingBowl offeringBowl)
    {
        return offeringBowl.GetComponent<OfferingBowlRuntimeState>() ??
               offeringBowl.gameObject.AddComponent<OfferingBowlRuntimeState>();
    }

    private static ItemStandRuntimeState GetOrAddItemStandRuntimeState(ItemStand itemStand)
    {
        return itemStand.GetComponent<ItemStandRuntimeState>() ??
               itemStand.gameObject.AddComponent<ItemStandRuntimeState>();
    }

    private static long GetOfferingBowlLastUseTicks(OfferingBowl offeringBowl, OfferingBowlRuntimeState state)
    {
        if (state.LocalLastUseTicks > 0L)
        {
            return state.LocalLastUseTicks;
        }

        ZNetView? view = offeringBowl.GetComponentInParent<ZNetView>();
        ZDO? zdo = view?.IsValid() == true ? view.GetZDO() : null;
        return zdo?.GetLong(OfferingBowlLastUseTicksKey, 0L) ?? 0L;
    }

    private static string FormatNames(IEnumerable<string>? names)
    {
        return names == null ? "" : string.Join(",", names.Where(name => !string.IsNullOrWhiteSpace(name)));
    }

    private static string FormatItemDrops(IEnumerable<ItemDrop>? itemDrops)
    {
        if (itemDrops == null)
        {
            return "";
        }

        return string.Join(
            ",",
            itemDrops
                .Where(itemDrop => itemDrop != null)
                .Select(itemDrop => NormalizeReferencePrefabName(itemDrop.gameObject) ?? itemDrop.name ?? "")
                .Where(name => name.Length > 0));
    }

    private static bool HasOverride(AltarConfigurationEntry entry)
    {
        return entry.OfferingBowl != null || entry.ItemStands is { Count: > 0 };
    }

    // Prefab and ItemStand value resolution.
    private static List<ItemDrop.ItemData.ItemType> ResolveItemStandTypes(List<string> typeNames, string warnContext)
    {
        List<ItemDrop.ItemData.ItemType> types = new();
        foreach (string typeName in typeNames)
        {
            ItemDrop.ItemData.ItemType? itemType = ParseItemStandType(typeName);
            if (!itemType.HasValue)
            {
                WarnInvalidEntry($"Entry '{warnContext}' uses unknown ItemDrop.ItemData.ItemType '{typeName}'.");
                continue;
            }

            types.Add(itemType.Value);
        }

        return types;
    }

    private static ItemDrop.ItemData.ItemType? ParseItemStandType(string? typeName)
    {
        string trimmed = (typeName ?? "").Trim();
        return trimmed.Length > 0 && Enum.TryParse(trimmed, true, out ItemDrop.ItemData.ItemType itemType) ? itemType : null;
    }

    private static ItemDrop? ResolveItemDrop(string? prefabName, string? warnContext)
    {
        GameObject? prefab = ResolveItemPrefab(prefabName, warnContext);
        return prefab != null ? prefab.GetComponent<ItemDrop>() : null;
    }

    private static List<ItemDrop> ResolveItemDropList(List<string> prefabNames, string? warnContext)
    {
        List<ItemDrop> items = new();
        for (int index = 0; index < prefabNames.Count; index++)
        {
            ItemDrop? itemDrop = ResolveItemDrop(prefabNames[index], warnContext == null ? null : $"{warnContext}[{index}]");
            if (itemDrop != null)
            {
                items.Add(itemDrop);
            }
        }

        return items;
    }

    private static GameObject? ResolveItemPrefab(string? prefabName, string? warnContext)
    {
        string trimmed = (prefabName ?? "").Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        GameObject? prefab = ObjectDB.instance?.GetItemPrefab(trimmed) ?? ZNetScene.instance?.GetPrefab(trimmed);
        if (prefab == null)
        {
            WarnInvalidEntry(warnContext == null ? null : $"Entry '{warnContext}' references unknown item prefab '{trimmed}'.");
            return null;
        }

        if (!prefab.TryGetComponent(out ItemDrop _))
        {
            WarnInvalidEntry(warnContext == null ? null : $"Entry '{warnContext}' references '{trimmed}', but it is not an item prefab.");
            return null;
        }

        return prefab;
    }

    private static GameObject? ResolveSpawnPrefab(string? prefabName, string? warnContext)
    {
        string trimmed = (prefabName ?? "").Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        GameObject? prefab = ZNetScene.instance?.GetPrefab(trimmed) ?? ObjectDB.instance?.GetItemPrefab(trimmed);
        if (prefab == null)
        {
            WarnInvalidEntry(warnContext == null ? null : $"Entry '{warnContext}' references unknown spawn prefab '{trimmed}'.");
        }

        return prefab;
    }

    private static StatusEffect? ResolveStatusEffect(string? statusEffectName, string? warnContext)
    {
        string trimmed = (statusEffectName ?? "").Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        StatusEffect? statusEffect = ObjectDB.instance?.GetStatusEffect(trimmed.GetStableHashCode());
        if (statusEffect != null)
        {
            return statusEffect;
        }

        statusEffect = ObjectDB.instance?.m_StatusEffects.FirstOrDefault(effect =>
            string.Equals(effect.name, trimmed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(effect.m_name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (statusEffect == null)
        {
            WarnInvalidEntry(warnContext == null ? null : $"Entry '{warnContext}' references unknown status effect '{trimmed}'.");
        }

        return statusEffect;
    }

    private static void RemoveSupportedItemsFromUnsupportedList(ItemStand itemStand, List<ItemDrop> supportedItems)
    {
        if (itemStand.m_unsupportedItems == null || itemStand.m_unsupportedItems.Count == 0 || supportedItems.Count == 0)
        {
            return;
        }

        HashSet<string> supportedNames = supportedItems
            .Select(item => item?.m_itemData?.m_shared?.m_name ?? "")
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        itemStand.m_unsupportedItems = itemStand.m_unsupportedItems
            .Where(item => item != null && !supportedNames.Contains(item.m_itemData.m_shared.m_name))
            .ToList();
    }

    internal static string GetRelativePath(Transform root, Transform target)
    {
        if (target == root)
        {
            return ".";
        }

        List<string> segments = new();
        Transform? current = target;
        while (current != null && current != root)
        {
            segments.Add($"{current.name}[{GetSameNameSiblingIndex(current)}]");
            current = current.parent;
        }

        segments.Reverse();
        return string.Join("/", segments);
    }

    private static int GetSameNameSiblingIndex(Transform transform)
    {
        if (transform.parent == null)
        {
            return 0;
        }

        int index = 0;
        foreach (Transform sibling in transform.parent)
        {
            if (ReferenceEquals(sibling, transform))
            {
                return index;
            }

            if (string.Equals(sibling.name, transform.name, StringComparison.Ordinal))
            {
                index++;
            }
        }

        return index;
    }

    private static string? NormalizeReferencePrefabName(GameObject? prefab)
    {
        return prefab != null ? TrimCloneSuffix(prefab.name) : null;
    }

    private static string TrimCloneSuffix(string? name)
    {
        string value = (name ?? "").Trim();
        const string cloneSuffix = "(Clone)";
        return value.EndsWith(cloneSuffix, StringComparison.Ordinal)
            ? value.Substring(0, value.Length - cloneSuffix.Length).TrimEnd()
            : value;
    }

    private static void WarnInvalidEntry(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            BossRulesPlugin.BossRulesLogger.LogWarning(message);
        }
    }
}
