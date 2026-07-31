using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRules;

internal static class QueenDungeonAltarSupport
{
    internal const string LocationPrefabName = "Mistlands_DvergrBossEntrance1";
    internal const string GeneratorPrefabName = "DG_DvergrBoss";
    internal const string RoomPrefabName = "dvergr_new_bossroom_ENTRANCE02";
    internal const string OfferingBowlPrefabName = "offeraltar_queen";

    private static readonly int GeneratorLocationPrefabZdoKey =
        $"{BossRulesPlugin.ModName}.queen_dungeon_generator_location_prefab".GetStableHashCode();
    private static readonly Dictionary<DungeonGenerator, string> GeneratorLocationPrefabs = new();

    [ThreadStatic]
    private static string? _activeLocationSpawnPrefab;

    internal static bool IsSupportedLocationPrefab(string? prefabName)
    {
        string normalized = (prefabName ?? "").Trim();
        return string.Equals(normalized, LocationPrefabName, StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(LocationPrefabName + ":", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsTargetGenerator(DungeonGenerator? generator)
    {
        return generator != null &&
               string.Equals(
                   TrimCloneSuffix(generator.gameObject.name),
                   GeneratorPrefabName,
                   StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsTargetRoom(DungeonDB.RoomData? roomData)
    {
        return roomData != null &&
               roomData.m_prefab.IsValid &&
               string.Equals(
                   (roomData.m_prefab.Name ?? "").Trim(),
                   RoomPrefabName,
                   StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsTargetOfferingBowl(OfferingBowl? offeringBowl)
    {
        return offeringBowl != null &&
               string.Equals(
                   TrimCloneSuffix(offeringBowl.gameObject.name),
                   OfferingBowlPrefabName,
                   StringComparison.OrdinalIgnoreCase);
    }

    internal static string BeginLocationSpawnContext(string prefabName)
    {
        string previous = _activeLocationSpawnPrefab ?? "";
        _activeLocationSpawnPrefab = (prefabName ?? "").Trim();
        return previous;
    }

    internal static void RestoreLocationSpawnContext(string previousPrefabName)
    {
        _activeLocationSpawnPrefab = string.IsNullOrWhiteSpace(previousPrefabName)
            ? null
            : previousPrefabName.Trim();
    }

    internal static bool TryResolveGeneratorLocationPrefab(
        DungeonGenerator? generator,
        out string locationPrefab)
    {
        locationPrefab = "";
        if (!IsTargetGenerator(generator))
        {
            return false;
        }

        DungeonGenerator resolvedGenerator = generator!;
        if (GeneratorLocationPrefabs.TryGetValue(resolvedGenerator, out string? cached) &&
            IsSupportedLocationPrefab(cached))
        {
            locationPrefab = cached.Trim();
            RecordGeneratorLocationPrefab(resolvedGenerator, locationPrefab);
            return true;
        }

        ZNetView? nview = resolvedGenerator.GetComponent<ZNetView>();
        ZDO? zdo = nview?.GetZDO();
        string zdoPrefab = (zdo?.GetString(GeneratorLocationPrefabZdoKey, "") ?? "").Trim();
        if (IsSupportedLocationPrefab(zdoPrefab))
        {
            locationPrefab = zdoPrefab;
            RecordGeneratorLocationPrefab(resolvedGenerator, locationPrefab);
            return true;
        }

        if (IsSupportedLocationPrefab(_activeLocationSpawnPrefab))
        {
            locationPrefab = _activeLocationSpawnPrefab!.Trim();
            RecordGeneratorLocationPrefab(resolvedGenerator, locationPrefab);
            return true;
        }

        Location? location = resolvedGenerator.GetComponentInParent<Location>(true);
        if (location != null &&
            AltarLocationResolver.TryResolveLocationPrefabName(location, out locationPrefab) &&
            IsSupportedLocationPrefab(locationPrefab))
        {
            RecordGeneratorLocationPrefab(resolvedGenerator, locationPrefab);
            return true;
        }

        LocationProxy? parentProxy = resolvedGenerator.GetComponentInParent<LocationProxy>(true);
        if (parentProxy != null &&
            AltarLocationResolver.TryResolveLocationProxyPrefabName(
                parentProxy,
                out locationPrefab) &&
            IsSupportedLocationPrefab(locationPrefab))
        {
            RecordGeneratorLocationPrefab(resolvedGenerator, locationPrefab);
            return true;
        }

        if (TryResolveLoadedLocationProxyPrefab(
                resolvedGenerator.transform.position,
                out locationPrefab))
        {
            RecordGeneratorLocationPrefab(resolvedGenerator, locationPrefab);
            return true;
        }

        locationPrefab = "";
        return false;
    }

    internal static void ResetRuntimeState()
    {
        GeneratorLocationPrefabs.Clear();
        _activeLocationSpawnPrefab = null;
    }

    private static void RecordGeneratorLocationPrefab(
        DungeonGenerator generator,
        string locationPrefab)
    {
        string normalized = (locationPrefab ?? "").Trim();
        if (!IsTargetGenerator(generator) ||
            !IsSupportedLocationPrefab(normalized))
        {
            return;
        }

        foreach (DungeonGenerator registeredGenerator in
                 new List<DungeonGenerator>(GeneratorLocationPrefabs.Keys))
        {
            if (registeredGenerator == null)
            {
                GeneratorLocationPrefabs.Remove(registeredGenerator!);
            }
        }

        bool changed = !GeneratorLocationPrefabs.TryGetValue(generator, out string? current) ||
                       !string.Equals(current, normalized, StringComparison.OrdinalIgnoreCase);
        GeneratorLocationPrefabs[generator] = normalized;

        ZNetView? nview = generator.GetComponent<ZNetView>();
        ZDO? zdo = nview?.GetZDO();
        if (nview?.IsOwner() == true && zdo != null)
        {
            zdo.Set(GeneratorLocationPrefabZdoKey, normalized);
        }

        if (changed)
        {
            BossRulesDebugLog.Client(
                $"Queen dungeon generator context resolved prefab={normalized} generator={generator.name}.");
        }
    }

    private static bool TryResolveLoadedLocationProxyPrefab(
        Vector3 position,
        out string locationPrefab)
    {
        locationPrefab = "";
        Vector2i targetZone = ZoneSystem.GetZone(position);
        float nearestHorizontalDistance = float.MaxValue;
        foreach (LocationProxy proxy in UnityEngine.Object.FindObjectsByType<LocationProxy>(
                     FindObjectsSortMode.None))
        {
            if (proxy == null || !ZoneSystem.GetZone(proxy.transform.position).Equals(targetZone))
            {
                continue;
            }

            float x = proxy.transform.position.x - position.x;
            float z = proxy.transform.position.z - position.z;
            float horizontalDistance = x * x + z * z;
            if (horizontalDistance >= nearestHorizontalDistance ||
                !AltarLocationResolver.TryResolveLocationProxyPrefabName(
                    proxy,
                    out string candidate) ||
                !IsSupportedLocationPrefab(candidate))
            {
                continue;
            }

            nearestHorizontalDistance = horizontalDistance;
            locationPrefab = candidate;
        }

        return locationPrefab.Length > 0;
    }

    private static string TrimCloneSuffix(string? name)
    {
        string value = (name ?? "").Trim();
        const string cloneSuffix = "(Clone)";
        return value.EndsWith(cloneSuffix, StringComparison.Ordinal)
            ? value.Substring(0, value.Length - cloneSuffix.Length).TrimEnd()
            : value;
    }
}
