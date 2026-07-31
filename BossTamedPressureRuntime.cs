using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRules;

internal static class BossTamedPressureRuntime
{
    private const float DefaultRange = 24f;
    private const float ScanInterval = 5f;
    private const float DamageInterval = 1f;
    private const int DefaultMaxTargetsPerBoss = 6;
    private const float DefaultPercentMaxHealthPerSecond = 0.007f;
    private const float DefaultIncomingDamageMultiplier = 1f;
    private const float DefaultOutgoingDamageMultiplier = 1f;
    private const float MessageInterval = 5f;
    private const string LocalizedMessage =
        "$" + BossRulesLocalization.MessageBossTamedPressureKey;

    private static readonly int ActiveUntilKey = "BossRules_BossTamedPressure_Until".GetStableHashCode();
    private static readonly int IncomingMultiplierKey = "BossRules_BossTamedPressure_Incoming".GetStableHashCode();
    private static readonly int OutgoingMultiplierKey = "BossRules_BossTamedPressure_Outgoing".GetStableHashCode();
    private static readonly List<BossCandidate> BossCandidateBuffer = new();
    private static readonly List<TargetCandidate> TargetCandidateBuffer = new();
    private static readonly List<ZDO> BossZdoBuffer = new();
    private static readonly List<ZDO> SectorZdoBuffer = new();
    private static readonly List<ZDOID> TargetIdBuffer = new();
    private static Rule? _rule;
    private static CharacterPrefabCatalog _characterPrefabCatalog = CharacterPrefabCatalog.Empty;

    private sealed class Rule
    {
        public HashSet<int> BossPrefabHashes { get; } = new();
        public HashSet<int> ExcludedBossPrefabHashes { get; } = new();
        public HashSet<int> ExcludedTamedPrefabHashes { get; } = new();
        public HashSet<int> ExtraPressuredPrefabHashes { get; } = new();
        public float Range { get; set; }
        public int MaxTargetsPerBoss { get; set; }
        public float PercentMaxHealthPerSecond { get; set; }
        public float IncomingDamageMultiplier { get; set; }
        public float OutgoingDamageMultiplier { get; set; }
        public double NextScanAt { get; set; }
        public double NextDamageAt { get; set; }
        public int CachedBossPrefabHashSignature { get; set; } = -1;
        public List<int> CachedBossPrefabHashes { get; } = new();
        public Dictionary<ZDOID, TrackedTarget> Targets { get; } = new();
        public Dictionary<long, double> NextMessageByPlayer { get; } = new();
    }

    private sealed class CharacterPrefabCatalog
    {
        public static CharacterPrefabCatalog Empty { get; } = new();

        public int GameDataSignature { get; set; } = -1;
        public HashSet<int> CharacterPrefabHashes { get; } = new();
        public HashSet<int> MonsterAiCharacterPrefabHashes { get; } = new();
        public HashSet<int> PlayerPrefabHashes { get; } = new();
        public Dictionary<int, string> PrefabNamesByHash { get; } = new();
        public Dictionary<int, float> BaseHealthByHash { get; } = new();
    }

    private readonly struct BossCandidate
    {
        internal BossCandidate(ZDO zdo, Vector3 position)
        {
            Zdo = zdo;
            Position = position;
        }

        internal ZDO Zdo { get; }
        internal Vector3 Position { get; }
    }

    private readonly struct TargetCandidate
    {
        internal TargetCandidate(ZDO zdo, Vector3 position, float distanceSqr, int order)
        {
            Zdo = zdo;
            Position = position;
            DistanceSqr = distanceSqr;
            Order = order;
        }

        internal ZDO Zdo { get; }
        internal Vector3 Position { get; }
        internal float DistanceSqr { get; }
        internal int Order { get; }
    }

    private sealed class TrackedTarget
    {
        public int PrefabHash { get; set; }
        public Vector3 LastKnownPosition { get; set; }
        public double ExpiresAt { get; set; }
    }

    internal static void Configure(BossTamedPressureDefinition? definition)
    {
        _rule = null;
        ClearTransientBuffers();
        if (definition != null)
        {
            _rule = CompileRule(definition);
        }
    }

    internal static void ResetRuntimeState()
    {
        _rule = null;
        ClearTransientBuffers();
        _characterPrefabCatalog = CharacterPrefabCatalog.Empty;
    }

    internal static void ExecuteServerTick()
    {
        double now = GetTimeSeconds();
        if (ZNet.instance == null)
        {
            return;
        }

        if (!BossRulesPlugin.IsRuntimeServer())
        {
            return;
        }

        if (!BossRulesConfig.IsBossTamedPressureEnabled())
        {
            return;
        }

        Rule? rule = _rule;
        if (rule == null)
        {
            return;
        }

        if (now >= rule.NextScanAt)
        {
            ScanRule(rule, now);
            rule.NextScanAt = now + ScanInterval;
        }

        if (now >= rule.NextDamageAt)
        {
            ApplyPeriodicDamage(rule, now);
            rule.NextDamageAt = now + DamageInterval;
        }
    }

    internal static void ApplyDamageMultipliers(Character? victim, HitData? hit)
    {
        if (victim == null ||
            hit == null ||
            !hit.HaveAttacker() ||
            !BossRulesConfig.IsBossTamedPressureEnabled())
        {
            return;
        }

        double now = GetTimeSeconds();
        float multiplier = 1f;

        float incomingMultiplier = 1f;
        bool incomingActive = TryGetCharacterZdo(victim, out ZDO? victimZdo) &&
                              victimZdo != null &&
                              TryGetActiveMultiplier(victimZdo, IncomingMultiplierKey, now, out incomingMultiplier);
        if (incomingActive)
        {
            multiplier *= incomingMultiplier;
        }

        ZDO? attackerZdo = ResolveAttackerZdo(hit);
        float outgoingMultiplier = 1f;
        bool outgoingActive = attackerZdo != null &&
                              TryGetActiveMultiplier(attackerZdo, OutgoingMultiplierKey, now, out outgoingMultiplier);
        if (outgoingActive)
        {
            multiplier *= outgoingMultiplier;
        }

        if (Mathf.Approximately(multiplier, 1f))
        {
            return;
        }

        float appliedMultiplier = Mathf.Max(0f, multiplier);
        hit.ApplyModifier(appliedMultiplier);
    }

    private static Rule CompileRule(BossTamedPressureDefinition definition)
    {
        BossTamedPressureTargetsDefinition? targets = definition.Targets;
        BossTamedPressurePressureDefinition? pressure = definition.Pressure;
        Rule rule = new()
        {
            Range = Mathf.Clamp(targets?.Range ?? DefaultRange, 0f, 128f),
            MaxTargetsPerBoss = Mathf.Clamp(targets?.MaxPerBoss ?? DefaultMaxTargetsPerBoss, 1, 128),
            PercentMaxHealthPerSecond = Mathf.Clamp01(pressure?.DamagePercentPerSecond ?? DefaultPercentMaxHealthPerSecond),
            IncomingDamageMultiplier = Mathf.Clamp(pressure?.IncomingDamageMultiplier ?? DefaultIncomingDamageMultiplier, 0f, 10f),
            OutgoingDamageMultiplier = Mathf.Clamp(pressure?.OutgoingDamageMultiplier ?? DefaultOutgoingDamageMultiplier, 0f, 10f)
        };

        AddHashes(rule.BossPrefabHashes, definition.BossPrefabs);
        AddHashes(rule.ExcludedBossPrefabHashes, definition.ExcludedBossPrefabs);
        AddHashes(rule.ExcludedTamedPrefabHashes, targets?.ExcludedTamedPrefabs);
        AddHashes(rule.ExtraPressuredPrefabHashes, targets?.ExtraPressuredPrefabs);
        return rule;
    }

    private static void ScanRule(Rule rule, double now)
    {
        CharacterPrefabCatalog catalog = EnsureCharacterPrefabCatalog();
        List<BossCandidate> bosses = BossCandidateBuffer;
        bosses.Clear();
        BuildBossCandidates(rule, bosses, catalog, BossZdoBuffer);
        if (bosses.Count == 0)
        {
            return;
        }

        float rangeSqr = rule.Range * rule.Range;
        List<TargetCandidate> nearbyTargets = TargetCandidateBuffer;
        foreach (BossCandidate boss in bosses)
        {
            CollectTargetsNearBoss(rule, catalog, boss, rangeSqr, nearbyTargets, SectorZdoBuffer);
            if (nearbyTargets.Count == 0)
            {
                continue;
            }

            if (nearbyTargets.Count > 1)
            {
                nearbyTargets.Sort(static (left, right) =>
                {
                    int distanceComparison = left.DistanceSqr.CompareTo(right.DistanceSqr);
                    return distanceComparison != 0 ? distanceComparison : left.Order.CompareTo(right.Order);
                });
            }

            int appliedCount = 0;
            foreach (TargetCandidate candidate in nearbyTargets)
            {
                if (candidate.Zdo.m_uid == boss.Zdo.m_uid)
                {
                    continue;
                }

                if (TrackTarget(rule, candidate.Zdo, candidate.Position, now))
                {
                    appliedCount++;
                }

                if (appliedCount >= rule.MaxTargetsPerBoss)
                {
                    break;
                }
            }
        }

        bosses.Clear();
        nearbyTargets.Clear();
    }

    private static void BuildBossCandidates(
        Rule rule,
        List<BossCandidate> bosses,
        CharacterPrefabCatalog catalog,
        List<ZDO> bossZdos)
    {
        bossZdos.Clear();
        if (ZDOMan.instance == null)
        {
            return;
        }

        foreach (int bossPrefabHash in GetBossPrefabHashes(rule, catalog))
        {
            if (!catalog.PrefabNamesByHash.TryGetValue(bossPrefabHash, out string prefabName) ||
                string.IsNullOrWhiteSpace(prefabName))
            {
                continue;
            }

            bossZdos.Clear();
            int index = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefabName, bossZdos, ref index))
            {
            }

            foreach (ZDO bossZdo in bossZdos)
            {
                if (IsValidLiveZdo(bossZdo, GetBaseHealth(catalog, bossPrefabHash)))
                {
                    bosses.Add(new BossCandidate(bossZdo, bossZdo.GetPosition()));
                }
            }
        }

        bossZdos.Clear();
    }

    private static void CollectTargetsNearBoss(
        Rule rule,
        CharacterPrefabCatalog catalog,
        BossCandidate boss,
        float rangeSqr,
        List<TargetCandidate> nearbyTargets,
        List<ZDO> sectorObjects)
    {
        nearbyTargets.Clear();
        sectorObjects.Clear();
        if (ZDOMan.instance == null || ZoneSystem.instance == null)
        {
            return;
        }

        int sectorRange = Mathf.Max(0, Mathf.CeilToInt(rule.Range / ZoneSystem.c_ZoneSize) + 1);
        ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(boss.Position), sectorRange, 0, sectorObjects);

        int order = 0;
        foreach (ZDO candidate in sectorObjects)
        {
            if (candidate == null ||
                candidate.m_uid == boss.Zdo.m_uid ||
                !IsEligiblePressureTarget(rule, candidate, catalog))
            {
                continue;
            }

            Vector3 position = candidate.GetPosition();
            float distanceSqr = GetHorizontalDistanceSqr(boss.Position, position);
            if (distanceSqr > rangeSqr)
            {
                continue;
            }

            nearbyTargets.Add(new TargetCandidate(candidate, position, distanceSqr, order++));
        }

        sectorObjects.Clear();
    }

    private static bool TrackTarget(
        Rule rule,
        ZDO zdo,
        Vector3 position,
        double now)
    {
        if (zdo == null || !zdo.IsValid())
        {
            return false;
        }

        ZDOID targetId = zdo.m_uid;
        int prefabHash = zdo.GetPrefab();
        double expiresAt = now + ScanInterval + 0.5d;
        rule.Targets[targetId] = new TrackedTarget
        {
            PrefabHash = prefabHash,
            LastKnownPosition = position,
            ExpiresAt = expiresAt
        };

        float existingUntil = zdo.GetFloat(ActiveUntilKey, 0f);
        float newUntil = (float)Math.Max(existingUntil, expiresAt);
        zdo.Set(ActiveUntilKey, newUntil);

        zdo.Set(IncomingMultiplierKey, Mathf.Clamp(rule.IncomingDamageMultiplier, 0f, 10f));
        zdo.Set(OutgoingMultiplierKey, Mathf.Clamp(rule.OutgoingDamageMultiplier, 0f, 10f));

        // Damage multipliers are evaluated by the damage owner, which can be a client on dedicated servers.
        ZDOMan.instance?.ForceSendZDO(targetId);
        return true;
    }

    private static void ApplyPeriodicDamage(Rule rule, double now)
    {
        if (rule.PercentMaxHealthPerSecond <= 0f)
        {
            RemoveExpiredTargets(rule, now);
            return;
        }

        CharacterPrefabCatalog catalog = EnsureCharacterPrefabCatalog();
        CopyTargetIds(rule, TargetIdBuffer);
        foreach (ZDOID targetId in TargetIdBuffer)
        {
            if (!rule.Targets.TryGetValue(targetId, out TrackedTarget? target) || target.ExpiresAt < now)
            {
                rule.Targets.Remove(targetId);
                continue;
            }

            ZDO? zdo = ZDOMan.instance?.GetZDO(targetId);
            if (zdo == null || !IsEligiblePressureTarget(rule, zdo, catalog))
            {
                rule.Targets.Remove(targetId);
                continue;
            }

            target.PrefabHash = zdo.GetPrefab();
            target.LastKnownPosition = zdo.GetPosition();
            float baseHealth = GetMaxHealth(zdo, target.PrefabHash, catalog);
            float damage = baseHealth * rule.PercentMaxHealthPerSecond * DamageInterval;
            if (damage <= 0f)
            {
                continue;
            }

            HitData hit = new()
            {
                m_hitType = HitData.HitType.Undefined,
                m_point = target.LastKnownPosition
            };
            hit.m_damage.m_damage = damage;
            ZRoutedRpc.instance?.InvokeRoutedRPC(zdo.GetOwner(), zdo.m_uid, "RPC_Damage", hit);
            TrySendMessage(rule, target.LastKnownPosition, now);
        }

        TargetIdBuffer.Clear();
    }

    private static void RemoveExpiredTargets(Rule rule, double now)
    {
        CopyTargetIds(rule, TargetIdBuffer);
        foreach (ZDOID targetId in TargetIdBuffer)
        {
            if (!rule.Targets.TryGetValue(targetId, out TrackedTarget? target) || target.ExpiresAt < now)
            {
                rule.Targets.Remove(targetId);
            }
        }

        TargetIdBuffer.Clear();
    }

    private static bool IsEligiblePressureTarget(Rule rule, ZDO zdo, CharacterPrefabCatalog catalog)
    {
        if (zdo == null || !zdo.IsValid())
        {
            return false;
        }

        int prefabHash = zdo.GetPrefab();
        if (prefabHash == 0 ||
            !catalog.CharacterPrefabHashes.Contains(prefabHash) ||
            catalog.PlayerPrefabHashes.Contains(prefabHash) ||
            !IsValidLiveZdo(zdo, GetBaseHealth(catalog, prefabHash)))
        {
            return false;
        }

        bool hasPrefabTargeting = rule.ExtraPressuredPrefabHashes.Count > 0 || rule.ExcludedTamedPrefabHashes.Count > 0;
        if (!hasPrefabTargeting)
        {
            return IsTamedMonsterAiZdo(zdo, prefabHash, catalog);
        }

        if (rule.ExtraPressuredPrefabHashes.Contains(prefabHash))
        {
            return true;
        }

        return IsTamedMonsterAiZdo(zdo, prefabHash, catalog) &&
               !rule.ExcludedTamedPrefabHashes.Contains(prefabHash);
    }

    private static bool IsTamedMonsterAiZdo(ZDO zdo, int prefabHash, CharacterPrefabCatalog catalog)
    {
        return zdo.GetBool(ZDOVars.s_tamed) &&
               catalog.MonsterAiCharacterPrefabHashes.Contains(prefabHash);
    }

    private static bool IsValidLiveZdo(ZDO? zdo, float baseHealth)
    {
        if (zdo == null || !zdo.IsValid())
        {
            return false;
        }

        float maxHealth = zdo.GetFloat(ZDOVars.s_maxHealth, Mathf.Max(baseHealth, 1f));
        return zdo.GetFloat(ZDOVars.s_health, maxHealth) > 0f;
    }

    private static float GetHorizontalDistanceSqr(Vector3 origin, Vector3 target)
    {
        float dx = target.x - origin.x;
        float dz = target.z - origin.z;
        return dx * dx + dz * dz;
    }

    private static CharacterPrefabCatalog EnsureCharacterPrefabCatalog()
    {
        int gameDataSignature = BossRulesRuntime.GetGameDataSignature();
        if (_characterPrefabCatalog.GameDataSignature == gameDataSignature)
        {
            return _characterPrefabCatalog;
        }

        CharacterPrefabCatalog catalog = new()
        {
            GameDataSignature = gameDataSignature
        };

        foreach (GameObject prefab in BossRulesRuntime.EnumeratePrefabsForRuntime())
        {
            if (prefab == null || !prefab.TryGetComponent(out Character character))
            {
                continue;
            }

            string prefabName = BossRulesRuntime.GetPrefabNameForRuntime(prefab);
            if (string.IsNullOrWhiteSpace(prefabName))
            {
                continue;
            }

            int prefabHash = prefabName.GetStableHashCode();
            catalog.CharacterPrefabHashes.Add(prefabHash);
            catalog.PrefabNamesByHash[prefabHash] = prefabName;
            catalog.BaseHealthByHash[prefabHash] = Mathf.Max(character.m_health, 1f);
            if (prefab.GetComponent<MonsterAI>() != null)
            {
                catalog.MonsterAiCharacterPrefabHashes.Add(prefabHash);
            }

            if (character.IsPlayer())
            {
                catalog.PlayerPrefabHashes.Add(prefabHash);
            }
        }

        foreach (string bossPrefabName in BossRulesRuntime.GetAutoDetectedBossPrefabNames())
        {
            string normalizedName = (bossPrefabName ?? "").Trim();
            if (normalizedName.Length == 0)
            {
                continue;
            }

            int prefabHash = normalizedName.GetStableHashCode();
            catalog.PrefabNamesByHash[prefabHash] = normalizedName;
        }

        _characterPrefabCatalog = catalog;
        return _characterPrefabCatalog;
    }

    private static IReadOnlyList<int> GetBossPrefabHashes(Rule rule, CharacterPrefabCatalog catalog)
    {
        if (rule.CachedBossPrefabHashSignature == catalog.GameDataSignature)
        {
            return rule.CachedBossPrefabHashes;
        }

        rule.CachedBossPrefabHashes.Clear();
        HashSet<int> yielded = new();
        foreach (string bossPrefabName in BossRulesRuntime.GetAutoDetectedBossPrefabNames())
        {
            string normalizedName = (bossPrefabName ?? "").Trim();
            if (normalizedName.Length == 0)
            {
                continue;
            }

            int prefabHash = normalizedName.GetStableHashCode();
            if (prefabHash != 0 &&
                !rule.ExcludedBossPrefabHashes.Contains(prefabHash) &&
                yielded.Add(prefabHash))
            {
                rule.CachedBossPrefabHashes.Add(prefabHash);
            }
        }

        foreach (int prefabHash in rule.BossPrefabHashes)
        {
            if (prefabHash != 0 &&
                !rule.ExcludedBossPrefabHashes.Contains(prefabHash) &&
                yielded.Add(prefabHash))
            {
                rule.CachedBossPrefabHashes.Add(prefabHash);
            }
        }

        rule.CachedBossPrefabHashSignature = catalog.GameDataSignature;
        return rule.CachedBossPrefabHashes;
    }

    private static float GetBaseHealth(CharacterPrefabCatalog catalog, int prefabHash)
    {
        return catalog.BaseHealthByHash.TryGetValue(prefabHash, out float baseHealth)
            ? Mathf.Max(baseHealth, 1f)
            : 1f;
    }

    private static float GetMaxHealth(ZDO zdo, int prefabHash, CharacterPrefabCatalog catalog)
    {
        float baseHealth = GetBaseHealth(catalog, prefabHash);
        int level = Mathf.Max(1, zdo.GetInt(ZDOVars.s_level, 1));
        return zdo.GetFloat(ZDOVars.s_maxHealth, baseHealth * level);
    }

    private static bool TryGetCharacterZdo(Character character, out ZDO? zdo)
    {
        zdo = character?.m_nview?.GetZDO();
        return zdo != null;
    }

    private static ZDO? ResolveAttackerZdo(HitData hit)
    {
        Character? attacker = hit.GetAttacker();
        if (attacker != null && TryGetCharacterZdo(attacker, out ZDO? characterZdo))
        {
            return characterZdo;
        }

        return !hit.m_attacker.IsNone() ? ZDOMan.instance?.GetZDO(hit.m_attacker) : null;
    }

    private static bool TryGetActiveMultiplier(ZDO zdo, int multiplierKey, double now, out float multiplier)
    {
        multiplier = 1f;
        if (zdo.GetFloat(ActiveUntilKey, 0f) <= now)
        {
            return false;
        }

        multiplier = Mathf.Clamp(zdo.GetFloat(multiplierKey, 1f), 0f, 10f);
        return !Mathf.Approximately(multiplier, 1f);
    }

    private static void TrySendMessage(
        Rule rule,
        Vector3 targetPosition,
        double now)
    {
        if (!SceneProximityQueries.TryFindNearestLivingServerPlayerInRangeXZ(targetPosition, Mathf.Max(rule.Range, 32f), out long playerId) ||
            playerId == 0L)
        {
            return;
        }

        if (rule.NextMessageByPlayer.TryGetValue(playerId, out double nextMessageAt) && now < nextMessageAt)
        {
            return;
        }

        rule.NextMessageByPlayer[playerId] = now + MessageInterval;
        if (playerId == ZNet.GetUID() && Player.m_localPlayer != null)
        {
            Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, LocalizedMessage);
            return;
        }

        ZRoutedRpc.instance?.InvokeRoutedRPC(
            playerId,
            "ShowMessage",
            (int)MessageHud.MessageType.TopLeft,
            LocalizedMessage);
    }

    private static double GetTimeSeconds()
    {
        return ZNet.instance?.GetTimeSeconds() ?? Time.time;
    }

    private static void CopyTargetIds(Rule rule, List<ZDOID> targetIds)
    {
        targetIds.Clear();
        foreach (ZDOID targetId in rule.Targets.Keys)
        {
            targetIds.Add(targetId);
        }
    }

    private static void ClearTransientBuffers()
    {
        BossCandidateBuffer.Clear();
        TargetCandidateBuffer.Clear();
        BossZdoBuffer.Clear();
        SectorZdoBuffer.Clear();
        TargetIdBuffer.Clear();
    }

    private static void AddHashes(HashSet<int> hashes, IEnumerable<string>? values)
    {
        if (values == null)
        {
            return;
        }

        foreach (string value in values)
        {
            string normalized = (value ?? "").Trim();
            if (normalized.Length > 0)
            {
                hashes.Add(normalized.GetStableHashCode());
            }
        }
    }
}
