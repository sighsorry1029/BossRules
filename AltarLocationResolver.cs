using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BossRules;

internal static class AltarLocationResolver
{
    private static readonly Dictionary<int, string> LocationPrefabNamesByHash = new();
    private static readonly Dictionary<LocationProxy, string> RuntimeLocationProxyPrefabsByInstance = new();
    private static readonly Dictionary<ZDOID, string> RuntimeLocationProxyPrefabsByZdoId = new();
    private static readonly int LocationProxyResolvedPrefabZdoKey = $"{BossRulesPlugin.ModName}.location_proxy_prefab".GetStableHashCode();
    private static readonly int ExpandWorldDataLocationReferenceHash = "locationreference".GetStableHashCode();
    private static int _expandWorldDataAssemblyCountAtLastResolve = -1;
    private static FieldInfo? _expandWorldDataCurrentLocationField;

    internal static void ResetRuntimeState()
    {
        LocationPrefabNamesByHash.Clear();
        RuntimeLocationProxyPrefabsByInstance.Clear();
        RuntimeLocationProxyPrefabsByZdoId.Clear();
        _expandWorldDataAssemblyCountAtLastResolve = -1;
        _expandWorldDataCurrentLocationField = null;
    }

    internal static void RecordLocationProxyResolvedPrefab(LocationProxy? proxy, string prefabName)
    {
        string normalized = (prefabName ?? "").Trim();
        if (proxy == null || normalized.Length == 0)
        {
            return;
        }

        RuntimeLocationProxyPrefabsByInstance[proxy] = normalized;
        ZNetView? nview = proxy.GetComponent<ZNetView>();
        ZDO? zdo = nview?.GetZDO();
        if (zdo == null)
        {
            return;
        }

        if (zdo.m_uid != ZDOID.None)
        {
            RuntimeLocationProxyPrefabsByZdoId[zdo.m_uid] = normalized;
        }

        if (nview!.IsOwner())
        {
            zdo.Set(LocationProxyResolvedPrefabZdoKey, normalized);
        }
    }

    internal static bool TryResolveLocationPrefabName(Location? location, out string prefabName)
    {
        prefabName = "";
        if (location == null)
        {
            return false;
        }

        LocationProxy? proxy = location.GetComponentInParent<LocationProxy>(true);
        if (proxy != null && TryResolveLocationProxyPrefabName(proxy, out prefabName))
        {
            return true;
        }

        return TryGetLocationPrefabNameWithoutProxy(location, out prefabName);
    }

    internal static bool TryResolveLocationProxyPrefabName(LocationProxy? proxy, out string prefabName)
    {
        prefabName = "";
        if (proxy == null)
        {
            return false;
        }

        if (TryGetRecordedLocationProxyPrefabName(proxy, out prefabName))
        {
            return true;
        }

        if (TryGetLocationProxyHashPrefabName(proxy, out prefabName))
        {
            return true;
        }

        Location? location = proxy.GetComponentInChildren<Location>(true);
        return location != null && TryGetLocationPrefabNameWithoutProxy(location, out prefabName);
    }

    internal static bool TryResolveZoneLocationPrefabName(Vector3 position, out string prefabName)
    {
        prefabName = "";
        if (ZoneSystem.instance == null)
        {
            return false;
        }

        Vector2i zone = ZoneSystem.GetZone(position);
        if (!ZoneSystem.instance.m_locationInstances.TryGetValue(zone, out ZoneSystem.LocationInstance locationInstance))
        {
            return false;
        }

        prefabName = GetZoneLocationPrefabName(locationInstance.m_location);
        return prefabName.Length > 0;
    }

    private static bool TryGetLocationPrefabNameWithoutProxy(Location location, out string prefabName)
    {
        prefabName = "";
        if (location == null)
        {
            return false;
        }

        string livePrefabName = TrimCloneSuffix(location.gameObject.name);
        string liveRootPrefabName = TryGetLocationRootPrefabName(location);
        string zonePrefabName = "";
        if (ZoneSystem.instance != null)
        {
            Vector2i zone = ZoneSystem.GetZone(location.transform.position);
            if (ZoneSystem.instance.m_locationInstances.TryGetValue(zone, out ZoneSystem.LocationInstance locationInstance))
            {
                zonePrefabName = GetZoneLocationPrefabName(locationInstance.m_location);
            }
        }

        if (ShouldPreferLiveLocationPrefabName(liveRootPrefabName, zonePrefabName))
        {
            prefabName = liveRootPrefabName;
            return true;
        }

        if (ShouldPreferLiveLocationPrefabName(livePrefabName, zonePrefabName))
        {
            prefabName = livePrefabName;
            return true;
        }

        prefabName = zonePrefabName.Length > 0 ? zonePrefabName : livePrefabName;
        return prefabName.Length > 0;
    }

    private static bool TryGetRecordedLocationProxyPrefabName(LocationProxy proxy, out string prefabName)
    {
        prefabName = "";
        ZNetView? nview = proxy.GetComponent<ZNetView>();
        ZDO? zdo = nview?.GetZDO();
        if (zdo != null)
        {
            string zdoPrefabName = (zdo.GetString(LocationProxyResolvedPrefabZdoKey, "") ?? "").Trim();
            if (zdoPrefabName.Length > 0)
            {
                RuntimeLocationProxyPrefabsByInstance[proxy] = zdoPrefabName;
                prefabName = zdoPrefabName;
                return true;
            }

            if (zdo.m_uid != ZDOID.None &&
                RuntimeLocationProxyPrefabsByZdoId.TryGetValue(zdo.m_uid, out string? cached) &&
                !string.IsNullOrWhiteSpace(cached))
            {
                RuntimeLocationProxyPrefabsByInstance[proxy] = cached;
                prefabName = cached;
                return true;
            }
        }

        if (RuntimeLocationProxyPrefabsByInstance.TryGetValue(proxy, out string? instanceCached))
        {
            prefabName = instanceCached ?? "";
            return prefabName.Length > 0;
        }

        return false;
    }

    private static bool TryGetLocationProxyHashPrefabName(LocationProxy proxy, out string prefabName)
    {
        prefabName = "";
        ZNetView? nview = proxy.GetComponent<ZNetView>();
        ZDO? zdo = nview?.GetZDO();
        int expandWorldDataLocationHash = zdo?.GetInt(ExpandWorldDataLocationReferenceHash) ?? 0;
        if (TryResolveLocationHashPrefabName(expandWorldDataLocationHash, out prefabName))
        {
            return true;
        }

        int locationHash = zdo?.GetInt(ZDOVars.s_location) ?? 0;
        return TryResolveLocationHashPrefabName(locationHash, out prefabName);
    }

    private static bool TryResolveLocationHashPrefabName(int locationHash, out string prefabName)
    {
        prefabName = "";
        if (locationHash == 0)
        {
            return false;
        }

        if (LocationPrefabNamesByHash.TryGetValue(locationHash, out string? cached))
        {
            prefabName = cached ?? "";
            return prefabName.Length > 0;
        }

        if (ZoneSystem.instance == null)
        {
            return false;
        }

        foreach (ZoneSystem.ZoneLocation zoneLocation in ZoneSystem.instance.m_locations)
        {
            string candidate = GetZoneLocationPrefabName(zoneLocation);
            if (candidate.Length == 0 || candidate.GetStableHashCode() != locationHash)
            {
                continue;
            }

            LocationPrefabNamesByHash[locationHash] = candidate;
            prefabName = candidate;
            return true;
        }

        return false;
    }

    private static string TryGetLocationRootPrefabName(Location location)
    {
        Transform? candidateRoot = null;
        LocationProxy? proxy = location.GetComponentInParent<LocationProxy>(true);
        if (proxy != null)
        {
            Transform? current = location.transform;
            while (current != null && current.parent != null)
            {
                if (ReferenceEquals(current.parent, proxy.transform))
                {
                    candidateRoot = current;
                    break;
                }

                current = current.parent;
            }
        }

        candidateRoot ??= location.transform.root;
        return candidateRoot != null ? TrimCloneSuffix(candidateRoot.gameObject.name) : "";
    }

    private static bool ShouldPreferLiveLocationPrefabName(string livePrefabName, string zonePrefabName)
    {
        if (livePrefabName.Length == 0 ||
            string.Equals(livePrefabName, zonePrefabName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (zonePrefabName.Length > 0 &&
            !livePrefabName.Contains(":") &&
            (zonePrefabName.Contains(":") || string.Equals(GetLocationPrefabBaseName(zonePrefabName), livePrefabName, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return livePrefabName.Contains(":") || AltarRuntime.HasConfiguredPrefab(livePrefabName);
    }

    internal static string GetZoneLocationPrefabName(ZoneSystem.ZoneLocation? location)
    {
        return (location?.m_prefabName ?? location?.m_prefab.Name ?? "").Trim();
    }

    internal static string GetLocationSpawnContextPrefabName(ZoneSystem.ZoneLocation? location)
    {
        string locationPrefab = GetZoneLocationPrefabName(location);
        if (!locationPrefab.Contains(":") &&
            TryGetExpandWorldDataCurrentLocationPrefabName(out string currentLocationPrefab) &&
            currentLocationPrefab.Contains(":") &&
            string.Equals(GetLocationPrefabBaseName(currentLocationPrefab), locationPrefab, StringComparison.OrdinalIgnoreCase))
        {
            return currentLocationPrefab;
        }

        return locationPrefab;
    }

    private static bool TryGetExpandWorldDataCurrentLocationPrefabName(out string prefabName)
    {
        prefabName = "";
        int loadedAssemblyCount = AppDomain.CurrentDomain.GetAssemblies().Length;
        if (_expandWorldDataCurrentLocationField == null &&
            _expandWorldDataAssemblyCountAtLastResolve != loadedAssemblyCount)
        {
            Type? locationSpawningType = FindLoadedType("ExpandWorldData.LocationSpawning", "ExpandWorldData");
            _expandWorldDataCurrentLocationField = locationSpawningType?.GetField("CurrentLocation", BindingFlags.Public | BindingFlags.Static);
            _expandWorldDataAssemblyCountAtLastResolve = loadedAssemblyCount;
        }

        try
        {
            if (_expandWorldDataCurrentLocationField?.GetValue(null) is not ZoneSystem.ZoneLocation currentLocation)
            {
                return false;
            }

            prefabName = GetZoneLocationPrefabName(currentLocation);
            return prefabName.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string GetLocationPrefabBaseName(string prefabName)
    {
        string trimmed = (prefabName ?? "").Trim();
        int separator = trimmed.IndexOf(':');
        return separator > 0 ? trimmed.Substring(0, separator).Trim() : trimmed;
    }

    private static Type? FindLoadedType(string fullTypeName, string preferredAssemblySimpleName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.Equals(assembly.GetName().Name, preferredAssemblySimpleName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Type? type = TryGetType(assembly, fullTypeName);
            if (type != null)
            {
                return type;
            }
        }

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = TryGetType(assembly, fullTypeName);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }

    private static Type? TryGetType(Assembly assembly, string fullTypeName)
    {
        try
        {
            return assembly.GetType(fullTypeName, throwOnError: false, ignoreCase: false);
        }
        catch
        {
            return null;
        }
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
