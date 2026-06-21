using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRules;

internal static class BossRulesManager
{
    private static readonly Dictionary<string, HashSet<Character>> TrackedBossesByPrefab =
        new(StringComparer.OrdinalIgnoreCase);

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
        CreatureSpawnerDuplicateBlockRuntime.ClearRuntimeState();
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
