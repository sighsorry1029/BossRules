using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace BossRules;

internal static partial class DespawnRulesManager
{
    private const float CreatedZdoObservationRetryIntervalSeconds = 0.25f;
    private const float CreatedZdoObservationRetryTimeoutSeconds = 1f;
    private static readonly Dictionary<ZDOID, PendingDespawnObservation> PendingDespawnObservations = new();
    private static readonly List<ZDOID> PendingDespawnObservationRemovals = new();
    private static readonly List<PendingDespawnObservation> PendingDespawnObservationUpdates = new();
    private static readonly List<ZDO> BootstrapScanBuffer = new();
    private static bool _pendingBootstrapScan = true;
    private static int _lastObservedDespawnLookupVersion = -1;

    private enum DespawnObservationSource
    {
        BootstrapScan = 0,
        CreatedZdo = 1,
        LoadedCharacter = 2
    }

    private readonly struct PendingDespawnObservation
    {
        internal PendingDespawnObservation(
            ZDOID zdoId,
            int prefabHashHint,
            string prefabNameHint,
            DespawnObservationSource source,
            float nextAttemptAt = 0f,
            float expireAt = 0f)
        {
            ZdoId = zdoId;
            PrefabHashHint = prefabHashHint;
            PrefabNameHint = prefabNameHint ?? "";
            Source = source;
            NextAttemptAt = nextAttemptAt;
            ExpireAt = expireAt;
        }

        internal ZDOID ZdoId { get; }
        internal int PrefabHashHint { get; }
        internal string PrefabNameHint { get; }
        internal DespawnObservationSource Source { get; }
        internal float NextAttemptAt { get; }
        internal float ExpireAt { get; }
    }

    internal static void MarkBootstrapScanDirty()
    {
        _pendingBootstrapScan = true;
    }

    private static void ObserveDespawnLookupVersion()
    {
        int version = BossRulesRuntime.GetDespawnLookupVersion();
        if (version == _lastObservedDespawnLookupVersion)
        {
            return;
        }

        _lastObservedDespawnLookupVersion = version;
        _pendingBootstrapScan = true;
    }

    private static bool RunPendingBootstrapScan()
    {
        if (ZDOMan.instance == null)
        {
            return false;
        }

        IReadOnlyList<string> prefabs = BossRulesRuntime.GetDespawnBootstrapPrefabOrder();
        if (prefabs.Count == 0)
        {
            return true;
        }

        foreach (string prefabName in prefabs)
        {
            QueueBootstrapScanDespawnObservations(prefabName);
        }
        return true;
    }

    internal static void QueueCreatedDespawnTarget(int prefabHashHint, ZDO? zdo)
    {
        if (!BossRulesPlugin.IsRuntimeServer() ||
            !BossRulesConfig.IsDespawnRulesEnabled() ||
            zdo == null ||
            zdo.m_uid.IsNone())
        {
            return;
        }

        int prefabHash = zdo.GetPrefab();
        if (prefabHash == 0)
        {
            prefabHash = prefabHashHint;
        }

        if (BossRulesRuntime.TryGetCachedDespawnTrackingPrefabHashEligibility(prefabHash, out bool eligible) &&
            !eligible)
        {
            return;
        }

        EnqueueDespawnObservation(
            new PendingDespawnObservation(
                zdo.m_uid,
                prefabHash,
                "",
                DespawnObservationSource.CreatedZdo));
    }

    // Pending ZDO observations are merged here before they become tracked despawn targets.
    private static void ApplyPendingDespawnObservations()
    {
        if (PendingDespawnObservations.Count == 0 ||
            ZNetScene.instance == null ||
            ObjectDB.instance == null ||
            ZDOMan.instance == null)
        {
            return;
        }

        float nowRealtime = Time.time;
        PendingDespawnObservationRemovals.Clear();
        PendingDespawnObservationUpdates.Clear();
        foreach (PendingDespawnObservation observation in PendingDespawnObservations.Values)
        {
            if (observation.NextAttemptAt > nowRealtime)
            {
                continue;
            }

            ZDO? zdo = ZDOMan.instance.GetZDO(observation.ZdoId);
            if (zdo == null || IsDeadZdo(observation.ZdoId))
            {
                PendingDespawnObservationRemovals.Add(observation.ZdoId);
                continue;
            }

            if (ApplyObservation(observation, zdo) != null)
            {
                PendingDespawnObservationRemovals.Add(observation.ZdoId);
                continue;
            }

            if (TryDeferCreatedZdoObservation(
                    observation,
                    zdo,
                    nowRealtime,
                    out PendingDespawnObservation deferredObservation))
            {
                PendingDespawnObservationUpdates.Add(deferredObservation);
                continue;
            }

            PendingDespawnObservationRemovals.Add(observation.ZdoId);
        }

        foreach (PendingDespawnObservation observation in PendingDespawnObservationUpdates)
        {
            PendingDespawnObservations[observation.ZdoId] = observation;
        }

        foreach (ZDOID zdoId in PendingDespawnObservationRemovals)
        {
            PendingDespawnObservations.Remove(zdoId);
        }

        PendingDespawnObservationRemovals.Clear();
        PendingDespawnObservationUpdates.Clear();
    }

    internal static void TryTrackLoadedDespawnTarget(Character? character)
    {
        if (!BossRulesPlugin.IsRuntimeServer() ||
            !BossRulesConfig.IsDespawnRulesEnabled())
        {
            return;
        }

        if (character == null || character.gameObject == null)
        {
            return;
        }

        if (character.IsDead())
        {
            return;
        }

        ZNetView? nview = character.GetComponent<ZNetView>();
        string prefabName = Utils.GetPrefabName(character.gameObject);
        if (nview == null)
        {
            return;
        }

        if (!nview.IsValid())
        {
            return;
        }

        ZDO? zdo = nview.GetZDO();
        if (zdo == null)
        {
            return;
        }

        if (IsDeadZdo(zdo.m_uid))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(prefabName))
        {
            return;
        }

        if (!ShouldQueueDespawnObservation(zdo.GetPrefab(), prefabName))
        {
            return;
        }

        EnqueueDespawnObservation(
            new PendingDespawnObservation(
                zdo.m_uid,
                zdo.GetPrefab(),
                prefabName,
                DespawnObservationSource.LoadedCharacter));
    }

    private static void QueueBootstrapScanDespawnObservations(string prefabName)
    {
        if (string.IsNullOrWhiteSpace(prefabName) || ZDOMan.instance == null)
        {
            return;
        }

        BootstrapScanBuffer.Clear();
        int index = 0;
        while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefabName, BootstrapScanBuffer, ref index))
        {
        }

        foreach (ZDO zdo in BootstrapScanBuffer)
        {
            if (zdo == null || zdo.m_uid.IsNone())
            {
                continue;
            }

            EnqueueDespawnObservation(
                new PendingDespawnObservation(
                    zdo.m_uid,
                    zdo.GetPrefab(),
                    prefabName,
                    DespawnObservationSource.BootstrapScan));
        }
    }

    private static void EnqueueDespawnObservation(PendingDespawnObservation observation)
    {
        if (observation.ZdoId.IsNone())
        {
            return;
        }

        if (!PendingDespawnObservations.TryGetValue(observation.ZdoId, out PendingDespawnObservation existing))
        {
            PendingDespawnObservations[observation.ZdoId] = observation;
            return;
        }

        PendingDespawnObservations[observation.ZdoId] = MergeDespawnObservation(existing, observation);
    }

    private static PendingDespawnObservation MergeDespawnObservation(
        PendingDespawnObservation current,
        PendingDespawnObservation incoming)
    {
        PendingDespawnObservation preferred =
            GetObservationPriority(incoming.Source) >= GetObservationPriority(current.Source)
                ? incoming
                : current;
        int prefabHashHint = incoming.PrefabHashHint != 0 ? incoming.PrefabHashHint : current.PrefabHashHint;
        string prefabNameHint =
            !string.IsNullOrWhiteSpace(incoming.PrefabNameHint)
                ? incoming.PrefabNameHint
                : current.PrefabNameHint;
        float nextAttemptAt = preferred.Source == DespawnObservationSource.CreatedZdo
            ? Mathf.Max(current.NextAttemptAt, incoming.NextAttemptAt)
            : 0f;
        float expireAt = preferred.Source == DespawnObservationSource.CreatedZdo
            ? Mathf.Max(current.ExpireAt, incoming.ExpireAt)
            : 0f;
        return new PendingDespawnObservation(
            preferred.ZdoId,
            prefabHashHint,
            prefabNameHint,
            preferred.Source,
            nextAttemptAt,
            expireAt);
    }

    private static int GetObservationPriority(DespawnObservationSource source)
    {
        return source switch
        {
            DespawnObservationSource.LoadedCharacter => 3,
            DespawnObservationSource.CreatedZdo => 2,
            _ => 1
        };
    }

    private static TrackedDespawnState? ApplyObservation(PendingDespawnObservation observation, ZDO zdo)
    {
        return ApplyObservation(
            zdo,
            observation.PrefabHashHint,
            observation.PrefabNameHint);
    }

    private static TrackedDespawnState? ApplyObservation(
        ZDO zdo,
        int prefabHashHint,
        string prefabNameHint)
    {
        string prefabName = string.IsNullOrWhiteSpace(prefabNameHint)
            ? ""
            : prefabNameHint;
        int prefabHash = zdo.GetPrefab();
        if (prefabHash == 0)
        {
            prefabHash = prefabHashHint;
        }

        if (!BossRulesRuntime.TryResolveDespawnTrackingRule(
                zdo,
                prefabHash,
                prefabName,
                out prefabName,
                out float? rangeOverride,
                out float? delayOverride,
                out IReadOnlyCollection<DespawnRefundDrop> refunds))
        {
            return null;
        }

        return ApplyObservationResolved(
            prefabName,
            zdo,
            rangeOverride,
            delayOverride,
            refunds);
    }

    private static TrackedDespawnState ApplyObservationResolved(
        string prefabName,
        ZDO zdo,
        float? rangeOverride,
        float? delayOverride,
        IReadOnlyCollection<DespawnRefundDrop> refunds)
    {
        TrackedDespawnState state = GetOrCreateTrackedDespawnState(zdo.m_uid);
        Character? loadedCharacter = TryGetLoadedTrackedCharacter(zdo);
        Vector3 interestPoint;
        if (loadedCharacter != null)
        {
            state.NameLocalizationKey = loadedCharacter.m_name?.Trim() ?? "";
            state.PrefabName = Utils.GetPrefabName(loadedCharacter.gameObject);
            interestPoint = loadedCharacter.GetCenterPoint();
        }
        else
        {
            state.PrefabName = prefabName ?? "";
            GameObject? prefab = ZNetScene.instance?.GetPrefab(state.PrefabName);
            state.NameLocalizationKey =
                prefab?.GetComponent<Character>()?.m_name?.Trim() ?? "";
            interestPoint = zdo.GetPosition();
        }

        state.RangeOverride = rangeOverride;
        state.DelayOverride = delayOverride;
        state.Refunds.Clear();
        if (refunds != null)
        {
            state.Refunds.AddRange(refunds);
        }

        PrimeTrackedDespawnInterestIfNeeded(state, interestPoint);
        BossRulesDebugLog.Client(
            $"Despawn tracking resolved prefab={prefabName} zdo={zdo.m_uid} loaded={loadedCharacter != null} range={(rangeOverride.HasValue ? rangeOverride.Value.ToString("0.##", CultureInfo.InvariantCulture) : "<default>")} delay={(delayOverride.HasValue ? delayOverride.Value.ToString("0.##", CultureInfo.InvariantCulture) : "<default>")} refunds={BossRulesDebugLog.FormatRefunds(refunds)}.");
        ScheduleTrackedDespawnCheck(zdo.m_uid, state, GetCurrentDespawnClockSeconds());
        return state;
    }

    private static bool ShouldQueueDespawnObservation(int prefabHashHint, string prefabNameHint)
    {
        if (!BossRulesRuntime.IsDespawnTrackingRuleLookupReady())
        {
            return true;
        }

        if (prefabHashHint != 0 &&
            BossRulesRuntime.IsEligibleDespawnTrackingPrefabHash(prefabHashHint))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(prefabNameHint) &&
               BossRulesRuntime.IsEligibleDespawnTrackingPrefabName(prefabNameHint);
    }

    private static bool TryDeferCreatedZdoObservation(
        PendingDespawnObservation observation,
        ZDO zdo,
        float nowRealtime,
        out PendingDespawnObservation deferredObservation)
    {
        deferredObservation = observation;
        if (observation.Source != DespawnObservationSource.CreatedZdo ||
            zdo.GetPrefab() != 0)
        {
            return false;
        }

        float expireAt = observation.ExpireAt > 0f
            ? observation.ExpireAt
            : nowRealtime + CreatedZdoObservationRetryTimeoutSeconds;
        if (nowRealtime >= expireAt)
        {
            return false;
        }

        deferredObservation = new PendingDespawnObservation(
            observation.ZdoId,
            observation.PrefabHashHint,
            observation.PrefabNameHint,
            observation.Source,
            nowRealtime + CreatedZdoObservationRetryIntervalSeconds,
            expireAt);
        return true;
    }
}
