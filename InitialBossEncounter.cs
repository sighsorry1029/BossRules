using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRules;

// Provenance and first approach belong to the individual ZDO, not the prefab.
// Never infer initial placement from an unmarked boss found in an existing world.
internal static class InitialBossEncounter
{
    private static readonly int EncounterKey = $"{BossRulesPlugin.ModName}.initial_boss_encounter".GetStableHashCode();
    private static readonly int QueenHash = "SeekerQueen".GetStableHashCode();
    private static readonly int FrozenKingHash = "FrozenKing".GetStableHashCode();
    private const int WaitingForPlayer = 1;
    private const int Encountered = 2;
    private const float EncounterHeightRange = 32f;
    private const float CheckInterval = 1f;
    private static readonly HashSet<Character> WaitingCharacters = new();
    private static readonly List<Character> PendingRemovals = new();
    private static float _nextCheckAt;

    [ThreadStatic] private static int _generationBossHash;

    internal static int BeginLocationGeneration(string prefab, ZoneSystem.SpawnMode mode)
    {
        bool isNorth = string.Equals(prefab, "DN_Bossroom", StringComparison.OrdinalIgnoreCase) ||
                       prefab.StartsWith("DN_Bossroom:", StringComparison.OrdinalIgnoreCase);
        return BeginGeneration(isNorth ? FrozenKingHash : 0, mode);
    }

    internal static int BeginRoomGeneration(bool isQueenRoom, ZoneSystem.SpawnMode mode)
    {
        return BeginGeneration(isQueenRoom ? QueenHash : 0, mode);
    }

    private static int BeginGeneration(int bossHash, ZoneSystem.SpawnMode mode)
    {
        int previous = _generationBossHash;
        _generationBossHash = mode == ZoneSystem.SpawnMode.Full || mode == ZoneSystem.SpawnMode.Ghost
            ? bossHash
            : 0;
        return previous;
    }

    internal static void RestoreGeneration(int previous) => _generationBossHash = previous;

    internal static void MarkCreatedBoss(int prefabHash, ZDO? zdo)
    {
        // ZNetView has not called SetPrefab yet; use the creation argument.
        if (_generationBossHash == 0 || prefabHash != _generationBossHash ||
            zdo == null || !zdo.IsOwner() || zdo.GetInt(EncounterKey) != 0 ||
            AltarRuntime.IsAltarSummoned(zdo))
        {
            return;
        }

        zdo.Set(EncounterKey, WaitingForPlayer);
        BossRulesDebugLog.Client($"Initial boss protected prefabHash={prefabHash} zdo={zdo.m_uid}.");
    }

    internal static bool IsWaitingForPlayer(ZDO zdo)
    {
        return zdo.GetInt(EncounterKey) == WaitingForPlayer && !AltarRuntime.IsAltarSummoned(zdo);
    }

    internal static void Track(Character character)
    {
        if (character == null)
        {
            return;
        }

        int prefabHash = Utils.GetPrefabName(character.gameObject).GetStableHashCode();
        if (prefabHash == QueenHash || prefabHash == FrozenKingHash)
        {
            // Replicas may receive provenance after Awake. Register only the two
            // supported prefab types, but read their current marker on each check.
            WaitingCharacters.Add(character);
        }
    }

    internal static void Untrack(Character character) => WaitingCharacters.Remove(character);

    internal static void CheckApproaches()
    {
        if (WaitingCharacters.Count == 0 || Time.time < _nextCheckAt)
        {
            return;
        }

        _nextCheckAt = Time.time + CheckInterval;
        if (!BossRulesRuntime.IsDespawnTrackingRuleLookupReady())
        {
            return;
        }

        PendingRemovals.Clear();
        foreach (Character character in WaitingCharacters)
        {
            ZNetView? view = character != null ? character.GetComponent<ZNetView>() : null;
            ZDO? zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null || character!.GetHealth() <= 0f ||
                zdo.GetInt(EncounterKey) == Encountered || AltarRuntime.IsAltarSummoned(zdo))
            {
                PendingRemovals.Add(character!);
                continue;
            }

            // The owner writes the ZDO, even when it is a remote client. Keep
            // non-owned loaded bosses registered in case ownership moves here.
            if (!IsWaitingForPlayer(zdo) || !view!.IsOwner())
            {
                continue;
            }

            float range = BossRulesRuntime.GetInitialBossEncounterRange(zdo);
            if (range <= 0f)
            {
                continue;
            }

            Vector3 bossPosition = character!.transform.position;
            foreach (Player player in Player.GetAllPlayers())
            {
                if (player == null || player.IsDead() ||
                    !IsWithinEncounterRange(player.transform.position, bossPosition, range))
                {
                    continue;
                }

                ZNetView? playerView = player.GetComponent<ZNetView>();
                if (playerView == null || !playerView.IsValid() || player.GetPlayerID() == 0L)
                {
                    continue;
                }

                zdo.Set(EncounterKey, Encountered);
                PendingRemovals.Add(character);
                BossRulesDebugLog.Client($"Initial boss encountered by player proximity zdo={zdo.m_uid}.");
                break;
            }
        }

        foreach (Character character in PendingRemovals)
        {
            WaitingCharacters.Remove(character);
        }
        PendingRemovals.Clear();
    }

    internal static bool IsWithinEncounterRange(Vector3 player, Vector3 boss, float range)
    {
        float dx = player.x - boss.x;
        float dz = player.z - boss.z;
        return range > 0f && dx * dx + dz * dz < range * range &&
               Mathf.Abs(player.y - boss.y) <= EncounterHeightRange;
    }

    internal static void ResetRuntimeState()
    {
        WaitingCharacters.Clear();
        PendingRemovals.Clear();
        _nextCheckAt = 0f;
        _generationBossHash = 0;
    }
}
