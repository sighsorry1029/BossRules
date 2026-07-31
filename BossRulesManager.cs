using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRules;

internal static class BossRulesManager
{
    private static readonly Dictionary<string, HashSet<Character>> TrackedBossesByPrefab =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly int CreatureSpawnerDuplicateBlockTicksZdoKey =
        $"{BossRulesPlugin.ModName}.creature_spawner_same_boss_duplicate_block_ticks".GetStableHashCode();
    private static readonly Dictionary<int, long> LastCreatureSpawnerBlockTicksByInstanceId = new();

    internal static bool ShouldBlockConfiguredSameBossSpawn(GameObject? targetPrefab, Vector3 sourcePosition)
    {
        return ShouldBlockSameBossSpawn(targetPrefab, sourcePosition, BossRulesConfig.GetSameBossDuplicateBlockRadius());
    }

    internal static bool ShouldBlockSameBossSpawn(GameObject? targetPrefab, Vector3 sourcePosition, float radius)
    {
        if (radius <= 0f || !TryGetBossPrefabName(targetPrefab, out string targetPrefabName))
        {
            return false;
        }

        if (!TrackedBossesByPrefab.TryGetValue(targetPrefabName, out HashSet<Character>? trackedBosses) ||
            trackedBosses.Count == 0)
        {
            return false;
        }

        float radiusSquared = radius * radius;
        trackedBosses.RemoveWhere(static character => !IsTrackableBossCharacter(character));
        if (trackedBosses.Count == 0)
        {
            TrackedBossesByPrefab.Remove(targetPrefabName);
            return false;
        }

        foreach (Character trackedBoss in trackedBosses)
        {
            Vector3 offset = trackedBoss.GetCenterPoint() - sourcePosition;
            offset.y = 0f;
            if (offset.sqrMagnitude < radiusSquared)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool ShouldBlockCreatureSpawnerUpdate(CreatureSpawner? creatureSpawner)
    {
        if (creatureSpawner == null ||
            !TryGetCreatureSpawnerTimingZdo(creatureSpawner, out ZNetView nview, out ZDO zdo) ||
            !nview.IsOwner())
        {
            return false;
        }

        float radius = BossRulesConfig.GetSameBossDuplicateBlockRadius();
        if (radius <= 0f)
        {
            ClearCreatureSpawnerBlockTicks(creatureSpawner, zdo);
            return false;
        }

        if (ShouldBlockSameBossSpawn(
                creatureSpawner.m_creaturePrefab,
                creatureSpawner.transform.position,
                radius))
        {
            RecordCreatureSpawnerBlock(creatureSpawner, zdo);
            return true;
        }

        return ShouldDelayCreatureSpawnerAfterBlock(creatureSpawner, zdo);
    }

    internal static void RemoveCreatureSpawner(CreatureSpawner? creatureSpawner)
    {
        if (creatureSpawner != null)
        {
            LastCreatureSpawnerBlockTicksByInstanceId.Remove(creatureSpawner.GetInstanceID());
        }
    }

    internal static void TrackBossCharacter(Character? character)
    {
        if (!TryGetTrackableBossPrefabName(character, out string prefabName))
        {
            return;
        }

        if (!TrackedBossesByPrefab.TryGetValue(prefabName, out HashSet<Character>? trackedBosses))
        {
            trackedBosses = new HashSet<Character>();
            TrackedBossesByPrefab[prefabName] = trackedBosses;
        }

        trackedBosses.Add(character!);
    }

    internal static void UntrackBossCharacter(Character? character)
    {
        if (character == null)
        {
            return;
        }

        if (TryGetTrackableBossPrefabName(character, out string prefabName) &&
            TrackedBossesByPrefab.TryGetValue(prefabName, out HashSet<Character>? trackedBosses))
        {
            trackedBosses.Remove(character);
            if (trackedBosses.Count == 0)
            {
                TrackedBossesByPrefab.Remove(prefabName);
            }

            return;
        }

        string? emptyPrefab = null;
        foreach (KeyValuePair<string, HashSet<Character>> pair in TrackedBossesByPrefab)
        {
            if (pair.Value.Remove(character))
            {
                if (pair.Value.Count == 0)
                {
                    emptyPrefab = pair.Key;
                }

                break;
            }
        }

        if (emptyPrefab != null)
        {
            TrackedBossesByPrefab.Remove(emptyPrefab);
        }
    }

    internal static void ClearRuntimeState()
    {
        TrackedBossesByPrefab.Clear();
        LastCreatureSpawnerBlockTicksByInstanceId.Clear();
    }

    private static void RecordCreatureSpawnerBlock(CreatureSpawner creatureSpawner, ZDO zdo)
    {
        long nowTicks = GetNetworkTimeTicks();
        LastCreatureSpawnerBlockTicksByInstanceId[creatureSpawner.GetInstanceID()] = nowTicks;
        zdo.Set(CreatureSpawnerDuplicateBlockTicksZdoKey, nowTicks);
        zdo.Set(ZDOVars.s_aliveTime, nowTicks);
    }

    private static bool ShouldDelayCreatureSpawnerAfterBlock(CreatureSpawner creatureSpawner, ZDO zdo)
    {
        if (creatureSpawner.m_respawnTimeMinuts <= 0f)
        {
            ClearCreatureSpawnerBlockTicks(creatureSpawner, zdo);
            return false;
        }

        long lastBlockTicks = GetCreatureSpawnerBlockTicks(creatureSpawner, zdo);
        if (lastBlockTicks <= 0L)
        {
            return false;
        }

        long nowTicks = GetNetworkTimeTicks();
        if (lastBlockTicks > nowTicks)
        {
            RecordCreatureSpawnerBlock(creatureSpawner, zdo);
            return true;
        }

        double elapsedMinutes = (new DateTime(nowTicks) - new DateTime(lastBlockTicks)).TotalMinutes;
        if (elapsedMinutes < creatureSpawner.m_respawnTimeMinuts)
        {
            return true;
        }

        ClearCreatureSpawnerBlockTicks(creatureSpawner, zdo);
        return false;
    }

    private static long GetCreatureSpawnerBlockTicks(CreatureSpawner creatureSpawner, ZDO zdo)
    {
        LastCreatureSpawnerBlockTicksByInstanceId.TryGetValue(creatureSpawner.GetInstanceID(), out long localTicks);
        return Math.Max(localTicks, zdo.GetLong(CreatureSpawnerDuplicateBlockTicksZdoKey, 0L));
    }

    private static void ClearCreatureSpawnerBlockTicks(CreatureSpawner creatureSpawner, ZDO zdo)
    {
        LastCreatureSpawnerBlockTicksByInstanceId.Remove(creatureSpawner.GetInstanceID());
        zdo.Set(CreatureSpawnerDuplicateBlockTicksZdoKey, 0L);
    }

    private static bool TryGetCreatureSpawnerTimingZdo(
        CreatureSpawner creatureSpawner,
        out ZNetView nview,
        out ZDO zdo)
    {
        nview = null!;
        zdo = null!;
        if (!creatureSpawner.TryGetComponent(out ZNetView? candidate) ||
            candidate == null)
        {
            return false;
        }

        nview = candidate;
        zdo = nview.GetZDO();
        return zdo != null;
    }

    private static long GetNetworkTimeTicks()
    {
        return ZNet.instance != null
            ? ZNet.instance.GetTime().Ticks
            : DateTime.UtcNow.Ticks;
    }

    private static bool TryGetTrackableBossPrefabName(Character? character, out string prefabName)
    {
        prefabName = "";
        return IsTrackableBossCharacter(character) &&
               TryGetBossPrefabName(character!.gameObject, out prefabName);
    }

    private static bool IsTrackableBossCharacter(Character? character)
    {
        return character != null &&
               character.gameObject != null &&
               !character.IsDead() &&
               character.IsBoss();
    }

    private static bool TryGetBossPrefabName(GameObject? prefab, out string prefabName)
    {
        prefabName = AltarRuntime.GetPrefabName(prefab);
        return prefab != null &&
               prefabName.Length > 0 &&
               prefab.TryGetComponent(out Character character) &&
               character.IsBoss();
    }
}
