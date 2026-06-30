using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRules;

internal sealed class DespawnRefundDrop
{
    internal DespawnRefundDrop(GameObject prefab, int amount, Vector3? dropPointOverride = null)
    {
        Prefab = prefab;
        Amount = amount;
        DropPointOverride = dropPointOverride;
    }

    internal GameObject Prefab { get; }
    internal int Amount { get; }
    internal Vector3? DropPointOverride { get; }
}

/// <summary>
/// Owns the tracked despawn state machine, including observation intake, detach persistence, and scheduled countdown evaluation.
/// ExecuteServerTick is the only path allowed to mutate tracked despawn state.
/// </summary>
internal static partial class DespawnRulesManager
{
    private const float DespawnCountdownCheckIntervalSeconds = 0.5f;
    private const float DespawnIdleCheckIntervalSeconds = 1f;
    private const float DespawnTrackingRefreshIntervalSeconds = 1f;
    private const int DespawnFinalCountdownSeconds = 5;
    private const int DespawnReminderIntervalSeconds = 5;
    private static readonly Dictionary<ZDOID, TrackedDespawnState> TrackedDespawnTargets = new();
    private static readonly SortedDictionary<long, List<ZDOID>> ScheduledDespawnChecks = new();
    private static readonly Dictionary<ZDOID, PendingDespawnDetachPersist> PendingDespawnDetachPersists = new();
    private static readonly List<ZDOID> PendingDespawnRemovals = new();
    private static readonly List<ZDOID> PendingDespawnDetachPersistRemovals = new();
    private static readonly System.Diagnostics.Stopwatch DespawnClock = System.Diagnostics.Stopwatch.StartNew();
    private static float _nextDespawnTrackingRefreshAt;
    private static float _defaultDespawnRange = 64f;
    private static float _defaultDespawnDelaySeconds = 90f;

    private readonly struct PendingDespawnDetachPersist
    {
        internal PendingDespawnDetachPersist(
            ZDOID zdoId,
            Vector3 probePoint,
            int prefabHashHint,
            string prefabNameHint)
        {
            ZdoId = zdoId;
            ProbePoint = probePoint;
            PrefabHashHint = prefabHashHint;
            PrefabNameHint = prefabNameHint ?? "";
        }

        internal ZDOID ZdoId { get; }
        internal Vector3 ProbePoint { get; }
        internal int PrefabHashHint { get; }
        internal string PrefabNameHint { get; }
    }

    private sealed class TrackedDespawnState
    {
        internal string DisplayName { get; set; } = "Target";
        internal string PrefabName { get; set; } = "";
        internal float? RangeOverride { get; set; }
        internal float? DelayOverride { get; set; }
        internal readonly List<DespawnRefundDrop> Refunds = new();
        internal double NoPlayerSince { get; set; } = -1d;
        internal int LastAnnouncedRemainingSeconds { get; set; } = -1;
        internal long LastInterestedPlayerId { get; set; }
        internal long CountdownRecipientPlayerId { get; set; }
        internal long ScheduledCheckAtBucket { get; set; } = long.MinValue;

        internal void UpdateFromCharacter(
            Character character,
            float? rangeOverride,
            float? delayOverride,
            IReadOnlyCollection<DespawnRefundDrop> refunds)
        {
            DisplayName = GetDisplayName(character);
            PrefabName = Utils.GetPrefabName(character.gameObject);
            RangeOverride = rangeOverride;
            DelayOverride = delayOverride;
            Refunds.Clear();
            if (refunds == null)
            {
                return;
            }

            Refunds.AddRange(refunds);
        }

        internal void UpdateFromZdoPrefab(
            string prefabName,
            float? rangeOverride,
            float? delayOverride,
            IReadOnlyCollection<DespawnRefundDrop> refunds)
        {
            DisplayName = string.IsNullOrWhiteSpace(prefabName) ? "Target" : prefabName;
            PrefabName = prefabName ?? "";
            RangeOverride = rangeOverride;
            DelayOverride = delayOverride;
            Refunds.Clear();
            if (refunds == null)
            {
                return;
            }

            Refunds.AddRange(refunds);
        }

        internal void ResetCountdown()
        {
            NoPlayerSince = -1d;
            LastAnnouncedRemainingSeconds = -1;
            CountdownRecipientPlayerId = 0L;
        }

        internal float GetEffectiveRange()
        {
            return Mathf.Clamp(RangeOverride ?? _defaultDespawnRange, 0f, 128f);
        }

        internal float GetEffectiveDelaySeconds()
        {
            return Mathf.Clamp(DelayOverride ?? _defaultDespawnDelaySeconds, 0f, 300f);
        }
    }

    internal static void ConfigureDefaults(float despawnRange, float despawnDelaySeconds)
    {
        _defaultDespawnRange = Mathf.Clamp(despawnRange, 0f, 128f);
        _defaultDespawnDelaySeconds = Mathf.Clamp(despawnDelaySeconds, 0f, 300f);
    }

    internal static void ExecuteServerTick()
    {
        if (!BossRulesPlugin.IsRuntimeServer())
        {
            return;
        }

        if (!BossRulesConfig.IsDespawnRulesEnabled())
        {
            if (TrackedDespawnTargets.Count > 0)
            {
                TrackedDespawnTargets.Clear();
                PendingDespawnRemovals.Clear();
            }

            if (ScheduledDespawnChecks.Count > 0)
            {
                ScheduledDespawnChecks.Clear();
            }

            if (PendingDespawnObservations.Count > 0)
            {
                PendingDespawnObservations.Clear();
                PendingDespawnObservationRemovals.Clear();
            }

            if (PendingDespawnObservationUpdates.Count > 0)
            {
                PendingDespawnObservationUpdates.Clear();
            }

            if (PendingDespawnDetachPersists.Count > 0)
            {
                PendingDespawnDetachPersists.Clear();
                PendingDespawnDetachPersistRemovals.Clear();
            }

            _nextDespawnTrackingRefreshAt = 0f;
            _pendingBootstrapScan = true;
            _lastObservedDespawnLookupVersion = -1;
            return;
        }

        ObserveDespawnLookupVersion();
        ApplyPendingDespawnObservations();

        float nowRealtime = Time.time;
        if (_nextDespawnTrackingRefreshAt <= nowRealtime)
        {
            PruneTrackedDespawnTargetsAgainstCurrentConfig();
            if (_pendingBootstrapScan && RunPendingBootstrapScan())
            {
                _pendingBootstrapScan = false;
                ApplyPendingDespawnObservations();
            }
            _nextDespawnTrackingRefreshAt = nowRealtime + DespawnTrackingRefreshIntervalSeconds;
        }

        double nowSeconds = GetCurrentDespawnClockSeconds();
        ApplyPendingDespawnDetaches(nowSeconds);

        if (TrackedDespawnTargets.Count == 0)
        {
            if (ScheduledDespawnChecks.Count > 0)
            {
                ScheduledDespawnChecks.Clear();
            }

            return;
        }

        if (ScheduledDespawnChecks.Count == 0)
        {
            return;
        }

        ProcessScheduledDespawnChecks(nowSeconds);
    }

    private static void ProcessScheduledDespawnChecks(double nowSeconds)
    {
        if (ScheduledDespawnChecks.Count == 0)
        {
            return;
        }

        long nowBucket = QuantizeScheduledCheck(nowSeconds);
        PendingDespawnRemovals.Clear();
        while (TryDequeueDueScheduledCheck(nowBucket, out long bucket, out List<ZDOID>? dueTargets))
        {
            foreach (ZDOID zdoId in dueTargets)
            {
                if (!TrackedDespawnTargets.TryGetValue(zdoId, out TrackedDespawnState? state) ||
                    state.ScheduledCheckAtBucket != bucket)
                {
                    continue;
                }

                state.ScheduledCheckAtBucket = long.MinValue;
                ZDO? zdo = ZDOMan.instance?.GetZDO(zdoId);
                if (zdo == null || IsDeadZdo(zdoId))
                {
                    PendingDespawnRemovals.Add(zdoId);
                    continue;
                }

                ProcessTrackedDespawnTarget(zdoId, zdo, state, nowSeconds);
            }
        }

        FlushPendingDespawnRemovals();
    }

    private static bool TryDequeueDueScheduledCheck(long nowBucket, out long bucket, out List<ZDOID> dueTargets)
    {
        if (ScheduledDespawnChecks.Count == 0)
        {
            bucket = long.MinValue;
            dueTargets = null!;
            return false;
        }

        using IEnumerator<KeyValuePair<long, List<ZDOID>>> enumerator = ScheduledDespawnChecks.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            bucket = long.MinValue;
            dueTargets = null!;
            return false;
        }

        KeyValuePair<long, List<ZDOID>> entry = enumerator.Current;
        if (entry.Key > nowBucket)
        {
            bucket = long.MinValue;
            dueTargets = null!;
            return false;
        }

        bucket = entry.Key;
        dueTargets = entry.Value;
        ScheduledDespawnChecks.Remove(entry.Key);
        return true;
    }

    private static void ApplyPendingDespawnDetaches(double nowSeconds)
    {
        if (PendingDespawnDetachPersists.Count == 0 ||
            ZNetScene.instance == null ||
            ObjectDB.instance == null ||
            ZDOMan.instance == null)
        {
            return;
        }

        PendingDespawnDetachPersistRemovals.Clear();
        foreach (PendingDespawnDetachPersist persist in PendingDespawnDetachPersists.Values)
        {
            ZDO? zdo = ZDOMan.instance.GetZDO(persist.ZdoId);
            if (zdo == null || IsDeadZdo(persist.ZdoId))
            {
                PendingDespawnDetachPersistRemovals.Add(persist.ZdoId);
                continue;
            }

            PendingDespawnDetachPersistRemovals.Add(persist.ZdoId);
            ApplyDetachPersist(persist, zdo, nowSeconds);
        }

        foreach (ZDOID zdoId in PendingDespawnDetachPersistRemovals)
        {
            PendingDespawnDetachPersists.Remove(zdoId);
        }

        PendingDespawnDetachPersistRemovals.Clear();
    }

    internal static void TryPersistDespawnCountdownBeforeResetZdo(ZNetView? nview)
    {
        if (!BossRulesPlugin.IsRuntimeServer() ||
            !BossRulesConfig.IsDespawnRulesEnabled() ||
            nview == null ||
            !nview.IsValid() ||
            !nview.TryGetComponent(out Character character) ||
            character.GetHealth() <= 0f)
        {
            return;
        }

        ZDO? zdo = nview.GetZDO();
        if (zdo == null)
        {
            return;
        }

        if (!ShouldQueueDespawnObservation(zdo.GetPrefab(), Utils.GetPrefabName(character.gameObject)))
        {
            return;
        }

        PendingDespawnDetachPersists[zdo.m_uid] = new PendingDespawnDetachPersist(
            zdo.m_uid,
            character.GetCenterPoint(),
            zdo.GetPrefab(),
            Utils.GetPrefabName(character.gameObject));
    }

    private static void ProcessTrackedDespawnTarget(
        ZDOID zdoId,
        ZDO zdo,
        TrackedDespawnState state,
        double nowSeconds)
    {
        Character? loadedCharacter = TryGetLoadedTrackedCharacter(zdo);
        if (loadedCharacter != null && loadedCharacter.GetHealth() <= 0f)
        {
            PendingDespawnRemovals.Add(zdoId);
            return;
        }

        float despawnRange = state.GetEffectiveRange();
        float despawnDelaySeconds = state.GetEffectiveDelaySeconds();
        if (despawnRange <= 0f)
        {
            state.ResetCountdown();
            ScheduleTrackedDespawnCheck(zdoId, state, nowSeconds + GetIdleCheckIntervalSeconds());
            return;
        }

        Vector3 probePoint = loadedCharacter != null ? loadedCharacter.GetCenterPoint() : zdo.GetPosition();
        bool hasPlayerInRange = SceneProximityQueries.TryFindAnyLivingPlayerInRangeXZ(probePoint, despawnRange, out long interestedPlayerId);
        if (hasPlayerInRange)
        {
            if (interestedPlayerId != 0L)
            {
                state.LastInterestedPlayerId = interestedPlayerId;
            }

            if (state.NoPlayerSince >= 0d)
            {
                long cancelRecipientId = interestedPlayerId;
                if (SceneProximityQueries.TryFindNearestLivingPlayerInRangeXZ(probePoint, despawnRange, out long nearestPlayerId))
                {
                    cancelRecipientId = nearestPlayerId;
                }

                SendDespawnMessage(cancelRecipientId, BuildDespawnCanceledMessage(state.DisplayName));
            }

            state.ResetCountdown();
            ScheduleTrackedDespawnCheck(zdoId, state, nowSeconds + GetIdleCheckIntervalSeconds());
            return;
        }

        if (state.NoPlayerSince < 0d)
        {
            StartDespawnCountdown(state, nowSeconds, despawnDelaySeconds);
        }

        double elapsedSeconds = nowSeconds - state.NoPlayerSince;
        if (elapsedSeconds >= despawnDelaySeconds)
        {
            IReadOnlyCollection<DespawnRefundDrop> refunds = ResolveRefundsForExecution(zdo, state);
            BossRulesDebugLog.Client(
                $"Despawn executing prefab={state.PrefabName} zdo={zdoId} refunds={BossRulesDebugLog.FormatRefunds(refunds)} position={BossRulesDebugLog.FormatVector3(probePoint)}.");
            _ = DespawnRefundExecutor.TryExecuteRefunds(probePoint, refunds);
            ApplyDespawnCleanupBeforeDestroy(zdo);
            zdo.SetOwner(ZDOMan.instance.m_sessionID);
            ZDOMan.instance.DestroyZDO(zdo);
            PendingDespawnRemovals.Add(zdoId);
            return;
        }

        int remainingSeconds = GetRemainingSeconds(despawnDelaySeconds, elapsedSeconds);
        if (state.CountdownRecipientPlayerId != 0L &&
            remainingSeconds != state.LastAnnouncedRemainingSeconds &&
            ShouldAnnounceDespawnRemaining(remainingSeconds))
        {
            SendDespawnMessage(state.CountdownRecipientPlayerId, BuildDespawnReminderMessage(state.DisplayName, remainingSeconds));
            state.LastAnnouncedRemainingSeconds = remainingSeconds;
        }

        ScheduleTrackedDespawnCheck(zdoId, state, nowSeconds + GetCountdownCheckIntervalSeconds());
    }

    private static IReadOnlyCollection<DespawnRefundDrop> ResolveRefundsForExecution(ZDO zdo, TrackedDespawnState state)
    {
        if (BossRulesRuntime.TryResolveDespawnTrackingRule(
                zdo,
                zdo.GetPrefab(),
                state.PrefabName,
                out string prefabName,
                out float? rangeOverride,
                out float? delayOverride,
                out IReadOnlyCollection<DespawnRefundDrop> refunds))
        {
            state.PrefabName = string.IsNullOrWhiteSpace(prefabName) ? state.PrefabName : prefabName;
            state.RangeOverride = rangeOverride;
            state.DelayOverride = delayOverride;
            state.Refunds.Clear();
            if (refunds != null)
            {
                state.Refunds.AddRange(refunds);
            }
        }

        return state.Refunds;
    }

    private static void ApplyDespawnCleanupBeforeDestroy(ZDO zdo)
    {
        if (ZoneSystem.instance == null || !zdo.GetBool("bosscount"))
        {
            return;
        }

        ZoneSystem.instance.GetGlobalKey(GlobalKeys.activeBosses, out float activeBossCount);
        ZoneSystem.instance.SetGlobalKey(GlobalKeys.activeBosses, Mathf.Max(0f, activeBossCount - 1f));
        zdo.Set("bosscount", value: false);
    }

    private static void PruneTrackedDespawnTargetsAgainstCurrentConfig()
    {
        if (TrackedDespawnTargets.Count == 0)
        {
            return;
        }

        foreach (KeyValuePair<ZDOID, TrackedDespawnState> pair in TrackedDespawnTargets)
        {
            ZDOID zdoId = pair.Key;
            TrackedDespawnState state = pair.Value;
            if (string.IsNullOrWhiteSpace(state.PrefabName))
            {
                continue;
            }

            if (BossRulesRuntime.TryResolveDespawnTrackingRule(
                    state.PrefabName,
                    out _,
                    out _,
                    out _))
            {
                continue;
            }

            PendingDespawnRemovals.Add(zdoId);
        }

        FlushPendingDespawnRemovals();
    }

    private static void ApplyDetachPersist(PendingDespawnDetachPersist persist, ZDO zdo, double nowSeconds)
    {
        PendingDespawnObservation observation = new(
            persist.ZdoId,
            persist.PrefabHashHint,
            persist.PrefabNameHint,
            DespawnObservationSource.LoadedCharacter);
        TrackedDespawnState? state = ApplyObservation(observation, zdo);
        if (state == null)
        {
            return;
        }

        float despawnRange = state.GetEffectiveRange();
        if (despawnRange <= 0f)
        {
            return;
        }

        if (SceneProximityQueries.TryFindAnyLivingPlayerInRangeXZ(persist.ProbePoint, despawnRange, out long interestedPlayerId))
        {
            state.LastInterestedPlayerId = interestedPlayerId;
            return;
        }

        if (state.NoPlayerSince >= 0d)
        {
            return;
        }

        float despawnDelaySeconds = state.GetEffectiveDelaySeconds();
        StartDespawnCountdown(state, nowSeconds, despawnDelaySeconds);
        ScheduleTrackedDespawnCheck(persist.ZdoId, state, nowSeconds);
    }

    private static void StartDespawnCountdown(TrackedDespawnState state, double nowSeconds, float despawnDelaySeconds)
    {
        state.NoPlayerSince = nowSeconds;
        state.CountdownRecipientPlayerId = GetCountdownRecipientPlayerId(state);
        state.LastAnnouncedRemainingSeconds = GetRemainingSeconds(despawnDelaySeconds, 0d);

        if (despawnDelaySeconds > 0f && state.CountdownRecipientPlayerId != 0L)
        {
            SendDespawnMessage(state.CountdownRecipientPlayerId, BuildDespawnStartMessage(state.DisplayName, state.LastAnnouncedRemainingSeconds));
        }
    }

    private static void PrimeTrackedDespawnInterestIfNeeded(TrackedDespawnState state, Vector3 point)
    {
        if (state.NoPlayerSince >= 0d || state.LastInterestedPlayerId != 0L)
        {
            return;
        }

        float despawnRange = state.GetEffectiveRange();
        if (despawnRange <= 0f)
        {
            return;
        }

        if (!SceneProximityQueries.TryFindAnyLivingPlayerInRangeXZ(point, despawnRange, out long interestedPlayerId))
        {
            return;
        }

        state.LastInterestedPlayerId = interestedPlayerId;
    }

    private static long GetCountdownRecipientPlayerId(TrackedDespawnState state)
    {
        long recipientPlayerId = state.LastInterestedPlayerId;
        return IsDespawnMessageRecipientAvailable(recipientPlayerId)
            ? recipientPlayerId
            : 0L;
    }

    private static void FlushPendingDespawnRemovals()
    {
        if (PendingDespawnRemovals.Count == 0)
        {
            return;
        }

        foreach (ZDOID zdoId in PendingDespawnRemovals)
        {
            if (TrackedDespawnTargets.TryGetValue(zdoId, out TrackedDespawnState? state))
            {
                state.ScheduledCheckAtBucket = long.MinValue;
            }

            TrackedDespawnTargets.Remove(zdoId);
        }

        PendingDespawnRemovals.Clear();
    }

    private static TrackedDespawnState GetOrCreateTrackedDespawnState(ZDOID zdoId)
    {
        if (!TrackedDespawnTargets.TryGetValue(zdoId, out TrackedDespawnState? state))
        {
            state = new TrackedDespawnState();
            TrackedDespawnTargets[zdoId] = state;
        }

        return state;
    }

    private static Character? TryGetLoadedTrackedCharacter(ZDO zdo)
    {
        if (ZNetScene.instance == null ||
            !ZNetScene.instance.m_instances.TryGetValue(zdo, out ZNetView nview) ||
            nview == null ||
            nview.gameObject == null)
        {
            return null;
        }

        return nview.GetComponent<Character>();
    }

    private static bool IsDeadZdo(ZDOID zdoId)
    {
        return ZDOMan.instance != null && ZDOMan.instance.m_deadZDOs.ContainsKey(zdoId);
    }

    private static int GetRemainingSeconds(float despawnDelaySeconds, double elapsedSeconds)
    {
        float remainingSeconds = Mathf.Max(0f, despawnDelaySeconds - (float)elapsedSeconds);
        return Mathf.Max(0, Mathf.CeilToInt(remainingSeconds));
    }

    private static void ScheduleTrackedDespawnCheck(ZDOID zdoId, TrackedDespawnState state, double scheduledTime)
    {
        long bucket = QuantizeScheduledCheck(scheduledTime);
        state.ScheduledCheckAtBucket = bucket;
        if (!ScheduledDespawnChecks.TryGetValue(bucket, out List<ZDOID>? scheduledTargets))
        {
            scheduledTargets = new List<ZDOID>();
            ScheduledDespawnChecks[bucket] = scheduledTargets;
        }

        scheduledTargets.Add(zdoId);
    }

    private static long QuantizeScheduledCheck(double scheduledTime)
    {
        return Math.Max(0L, (long)Math.Ceiling(scheduledTime * 1000d));
    }

    private static double GetIdleCheckIntervalSeconds()
    {
        return DespawnIdleCheckIntervalSeconds;
    }

    private static double GetCountdownCheckIntervalSeconds()
    {
        return DespawnCountdownCheckIntervalSeconds;
    }

    private static double GetCurrentDespawnClockSeconds()
    {
        return DespawnClock.Elapsed.TotalSeconds;
    }

    private static bool ShouldAnnounceDespawnRemaining(int remainingSeconds)
    {
        if (remainingSeconds <= 0)
        {
            return false;
        }

        if (remainingSeconds <= DespawnFinalCountdownSeconds)
        {
            return true;
        }

        return remainingSeconds % DespawnReminderIntervalSeconds == 0;
    }

}
