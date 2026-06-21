using System;
using System.Collections.Generic;

namespace BossRules;

internal static class CreatureSpawnerDuplicateBlockRuntime
{
    private static readonly int DuplicateBlockTicksZdoKey =
        $"{BossRulesPlugin.ModName}.creature_spawner_same_boss_duplicate_block_ticks".GetStableHashCode();
    private static readonly Dictionary<int, long> LastBlockTicksBySpawnerId = new();

    internal static bool ShouldBlockUpdate(CreatureSpawner? creatureSpawner)
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
            ClearBlockTicks(creatureSpawner, zdo);
            return false;
        }

        if (BossRulesManager.ShouldBlockSameBossSpawn(
                creatureSpawner.m_creaturePrefab,
                creatureSpawner.transform.position,
                radius))
        {
            RecordBlock(creatureSpawner, zdo);
            return true;
        }

        return ShouldDelayAfterBlock(creatureSpawner, zdo);
    }

    internal static void RemoveSpawner(CreatureSpawner? creatureSpawner)
    {
        if (creatureSpawner != null)
        {
            LastBlockTicksBySpawnerId.Remove(creatureSpawner.GetInstanceID());
        }
    }

    internal static void ClearRuntimeState()
    {
        LastBlockTicksBySpawnerId.Clear();
    }

    private static void RecordBlock(CreatureSpawner creatureSpawner, ZDO zdo)
    {
        long nowTicks = GetNetworkTimeTicks();
        LastBlockTicksBySpawnerId[creatureSpawner.GetInstanceID()] = nowTicks;
        zdo.Set(DuplicateBlockTicksZdoKey, nowTicks);
        zdo.Set(ZDOVars.s_aliveTime, nowTicks);
    }

    private static bool ShouldDelayAfterBlock(CreatureSpawner creatureSpawner, ZDO zdo)
    {
        if (creatureSpawner.m_respawnTimeMinuts <= 0f)
        {
            ClearBlockTicks(creatureSpawner, zdo);
            return false;
        }

        long lastBlockTicks = GetBlockTicks(creatureSpawner, zdo);
        if (lastBlockTicks <= 0L)
        {
            return false;
        }

        long nowTicks = GetNetworkTimeTicks();
        if (lastBlockTicks > nowTicks)
        {
            RecordBlock(creatureSpawner, zdo);
            return true;
        }

        double elapsedMinutes = (new DateTime(nowTicks) - new DateTime(lastBlockTicks)).TotalMinutes;
        if (elapsedMinutes < creatureSpawner.m_respawnTimeMinuts)
        {
            return true;
        }

        ClearBlockTicks(creatureSpawner, zdo);
        return false;
    }

    private static long GetBlockTicks(CreatureSpawner creatureSpawner, ZDO zdo)
    {
        LastBlockTicksBySpawnerId.TryGetValue(creatureSpawner.GetInstanceID(), out long localTicks);
        return Math.Max(localTicks, zdo.GetLong(DuplicateBlockTicksZdoKey, 0L));
    }

    private static void ClearBlockTicks(CreatureSpawner creatureSpawner, ZDO zdo)
    {
        LastBlockTicksBySpawnerId.Remove(creatureSpawner.GetInstanceID());
        zdo.Set(DuplicateBlockTicksZdoKey, 0L);
    }

    private static bool TryGetCreatureSpawnerTimingZdo(CreatureSpawner creatureSpawner, out ZNetView nview, out ZDO zdo)
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
}
