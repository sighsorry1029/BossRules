using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BossRules;

internal static class OfferingBowlHoverInfoFormatter
{
    private const float HoverInfoCacheSeconds = 0.25f;
    private static readonly List<OfferingBowl> RegisteredOfferingBowls = new();
    private static readonly HashSet<int> RegisteredOfferingBowlIds = new();
    private static readonly Dictionary<int, HoverInfoCacheEntry> HoverInfoCache = new();

    private sealed class HoverInfoCacheEntry
    {
        public string Info { get; set; } = "";
        public float ExpiresAt { get; set; }
    }

    internal static string AppendInfo(string baseText, OfferingBowl? offeringBowl)
    {
        if (!BossRulesConfig.ShouldShowOfferingBowlHoverInfo() || offeringBowl == null)
        {
            return baseText ?? "";
        }

        string info = GetCachedInfo(offeringBowl);
        if (info.Length == 0)
        {
            return baseText ?? "";
        }

        return string.IsNullOrWhiteSpace(baseText) ? info : $"{baseText}\n{info}";
    }

    internal static void RegisterOfferingBowl(OfferingBowl? offeringBowl)
    {
        if (offeringBowl == null)
        {
            return;
        }

        if (RegisteredOfferingBowlIds.Add(offeringBowl.GetInstanceID()))
        {
            RegisteredOfferingBowls.Add(offeringBowl);
        }
    }

    internal static IReadOnlyList<OfferingBowl> GetKnownOfferingBowls()
    {
        CleanupRegisteredOfferingBowls();
        if (RegisteredOfferingBowls.Count == 0)
        {
            foreach (OfferingBowl offeringBowl in UnityEngine.Object.FindObjectsByType<OfferingBowl>(FindObjectsSortMode.None))
            {
                RegisterOfferingBowl(offeringBowl);
            }
        }

        return RegisteredOfferingBowls;
    }

    internal static void ClearRuntimeCaches()
    {
        HoverInfoCache.Clear();
    }

    internal static void ResetRuntimeState()
    {
        RegisteredOfferingBowls.Clear();
        RegisteredOfferingBowlIds.Clear();
        HoverInfoCache.Clear();
    }

    private static string GetCachedInfo(OfferingBowl offeringBowl)
    {
        int instanceId = offeringBowl.GetInstanceID();
        float now = Time.realtimeSinceStartup;
        if (HoverInfoCache.TryGetValue(instanceId, out HoverInfoCacheEntry? cached) &&
            cached.ExpiresAt > now)
        {
            return cached.Info;
        }

        string info = BuildInfo(offeringBowl);
        HoverInfoCache[instanceId] = new HoverInfoCacheEntry
        {
            Info = info,
            ExpiresAt = now + HoverInfoCacheSeconds
        };
        return info;
    }

    private static string BuildInfo(OfferingBowl offeringBowl)
    {
        string spawnedText = GetSpawnedText(offeringBowl);
        string requiredText = GetRequiredText(offeringBowl);

        if (spawnedText.Length == 0)
        {
            return requiredText;
        }

        if (requiredText.Length == 0)
        {
            return spawnedText;
        }

        return $"{spawnedText}\n{requiredText}";
    }

    private static string GetSpawnedText(OfferingBowl offeringBowl)
    {
        if (offeringBowl.m_bossPrefab != null)
        {
            return GetCharacterDisplayName(offeringBowl.m_bossPrefab);
        }

        return offeringBowl.m_itemPrefab != null ? GetItemDisplayName(offeringBowl.m_itemPrefab) : "";
    }

    private static string GetRequiredText(OfferingBowl offeringBowl)
    {
        if (offeringBowl.m_useItemStands)
        {
            return GetRequiredTextFromItemStands(offeringBowl);
        }

        if (offeringBowl.m_bossItem == null)
        {
            return "";
        }

        string itemName = GetItemDisplayName(offeringBowl.m_bossItem);
        if (itemName.Length == 0)
        {
            return "";
        }

        int amount = Math.Max(1, offeringBowl.m_bossItems);
        return amount > 1 ? $"{itemName} x{amount}" : itemName;
    }

    private static string GetRequiredTextFromItemStands(OfferingBowl offeringBowl)
    {
        Dictionary<string, int> countsByName = new(StringComparer.Ordinal);
        foreach (ItemStand itemStand in AltarItemStandHoverInfoFormatter.GetDisplayRelevantItemStands(offeringBowl))
        {
            if (itemStand == null || itemStand.m_supportedItems == null || itemStand.m_supportedItems.Count != 1)
            {
                continue;
            }

            string itemName = GetItemDisplayName(itemStand.m_supportedItems[0]);
            if (itemName.Length == 0)
            {
                continue;
            }

            countsByName[itemName] = countsByName.TryGetValue(itemName, out int count) ? count + 1 : 1;
        }

        return countsByName.Count == 0
            ? ""
            : string.Join(", ", countsByName.Select(pair => pair.Value > 1 ? $"{pair.Key} x{pair.Value}" : pair.Key));
    }

    private static string GetCharacterDisplayName(GameObject prefab)
    {
        return prefab.TryGetComponent(out Character character)
            ? Localize(string.IsNullOrWhiteSpace(character.m_name) ? prefab.name : character.m_name)
            : Localize(prefab.name);
    }

    internal static string GetItemDisplayName(ItemDrop itemDrop)
    {
        return Localize(itemDrop.m_itemData.m_shared.m_name);
    }

    private static string Localize(string text)
    {
        return Localization.instance != null ? Localization.instance.Localize(text ?? "") : (text ?? "");
    }

    private static void CleanupRegisteredOfferingBowls()
    {
        for (int index = RegisteredOfferingBowls.Count - 1; index >= 0; index--)
        {
            if (RegisteredOfferingBowls[index] != null)
            {
                continue;
            }

            RegisteredOfferingBowls.RemoveAt(index);
        }

        RegisteredOfferingBowlIds.Clear();
        foreach (OfferingBowl offeringBowl in RegisteredOfferingBowls)
        {
            if (offeringBowl != null)
            {
                RegisteredOfferingBowlIds.Add(offeringBowl.GetInstanceID());
            }
        }
    }
}

internal static class AltarItemStandHoverInfoFormatter
{
    private const float RelevantOfferingBowlCacheSeconds = 1f;
    private const float HoverInfoCacheSeconds = 0.25f;
    private static readonly ItemStand[] EmptyItemStands = Array.Empty<ItemStand>();
    private static readonly List<ItemStand> RegisteredItemStands = new();
    private static readonly HashSet<int> RegisteredItemStandIds = new();
    private static readonly Dictionary<int, RelevantOfferingBowlCacheEntry> RelevantOfferingBowlCache = new();
    private static readonly Dictionary<int, HoverInfoCacheEntry> HoverInfoCache = new();

    private sealed class RelevantOfferingBowlCacheEntry
    {
        public OfferingBowl? OfferingBowl { get; set; }
        public float ExpiresAt { get; set; }
    }

    private sealed class HoverInfoCacheEntry
    {
        public string Info { get; set; } = "";
        public float ExpiresAt { get; set; }
    }

    internal static string AppendInfo(string baseText, ItemStand? itemStand)
    {
        if (!BossRulesConfig.ShouldShowOfferingBowlHoverInfo() || itemStand == null)
        {
            return baseText ?? "";
        }

        if (itemStand.GetComponentInParent<BossStone>() != null)
        {
            return baseText ?? "";
        }

        if (!TryGetRelevantOfferingBowl(itemStand, out _))
        {
            return baseText ?? "";
        }

        string info = GetCachedInfo(itemStand);
        if (info.Length == 0)
        {
            return baseText ?? "";
        }

        return string.IsNullOrWhiteSpace(baseText) ? info : $"{baseText}\n{info}";
    }

    internal static void RegisterItemStand(ItemStand? itemStand)
    {
        if (itemStand == null)
        {
            return;
        }

        if (RegisteredItemStandIds.Add(itemStand.GetInstanceID()))
        {
            RegisteredItemStands.Add(itemStand);
        }
    }

    internal static void ClearRuntimeCaches()
    {
        RelevantOfferingBowlCache.Clear();
        HoverInfoCache.Clear();
        OfferingBowlHoverInfoFormatter.ClearRuntimeCaches();
    }

    internal static void ResetRuntimeState()
    {
        RegisteredItemStands.Clear();
        RegisteredItemStandIds.Clear();
        RelevantOfferingBowlCache.Clear();
        HoverInfoCache.Clear();
        OfferingBowlHoverInfoFormatter.ResetRuntimeState();
    }

    internal static IReadOnlyList<ItemStand> FindRelevantItemStands(OfferingBowl offeringBowl)
    {
        if (offeringBowl == null || !offeringBowl.m_useItemStands)
        {
            return EmptyItemStands;
        }

        EnsureRegistryPopulated();
        CleanupRegisteredItemStands();

        List<ItemStand> itemStands = new();
        HashSet<int> seenIds = new();
        if (TryGetOfferingBowlStructuralRoot(offeringBowl, out Transform? root) && root != null)
        {
            AddRelevantItemStands(root, offeringBowl, itemStands, seenIds);
        }

        foreach (ItemStand itemStand in RegisteredItemStands)
        {
            if (itemStand == null || !seenIds.Add(itemStand.GetInstanceID()) || !IsRelevantToOfferingBowl(itemStand, offeringBowl))
            {
                continue;
            }

            itemStands.Add(itemStand);
        }

        return itemStands.Count == 0 ? EmptyItemStands : itemStands;
    }

    internal static IReadOnlyList<ItemStand> GetDisplayRelevantItemStands(OfferingBowl offeringBowl)
    {
        IReadOnlyList<ItemStand> relevant = FindRelevantItemStands(offeringBowl);
        if (relevant.Count <= 1)
        {
            return relevant;
        }

        Dictionary<(int X, int Y, int Z), ItemStand> bestByPosition = new();
        foreach (ItemStand itemStand in relevant)
        {
            Vector3 position = itemStand.transform.position;
            (int X, int Y, int Z) key =
                ((int)Math.Round(position.x * 20f), (int)Math.Round(position.y * 20f), (int)Math.Round(position.z * 20f));
            if (!bestByPosition.TryGetValue(key, out ItemStand? existing) ||
                GetDisplayPriority(itemStand) > GetDisplayPriority(existing))
            {
                bestByPosition[key] = itemStand;
            }
        }

        return bestByPosition.Count == 0 ? EmptyItemStands : bestByPosition.Values.ToList();
    }

    internal static bool TryGetRelevantOfferingBowl(ItemStand itemStand, out OfferingBowl? offeringBowl)
    {
        offeringBowl = null;
        if (itemStand == null)
        {
            return false;
        }

        int itemStandId = itemStand.GetInstanceID();
        float now = Time.realtimeSinceStartup;
        if (RelevantOfferingBowlCache.TryGetValue(itemStandId, out RelevantOfferingBowlCacheEntry? cached) &&
            cached.ExpiresAt > now)
        {
            offeringBowl = cached.OfferingBowl;
            return offeringBowl != null;
        }

        offeringBowl = FindRelevantOfferingBowlUncached(itemStand);
        RelevantOfferingBowlCache[itemStandId] = new RelevantOfferingBowlCacheEntry
        {
            OfferingBowl = offeringBowl,
            ExpiresAt = now + RelevantOfferingBowlCacheSeconds
        };
        return offeringBowl != null;
    }

    private static OfferingBowl? FindRelevantOfferingBowlUncached(ItemStand itemStand)
    {
        Location? location = itemStand.GetComponentInParent<Location>(true);
        if (location != null)
        {
            return FindNearestRelevantOfferingBowl(itemStand, location.GetComponentsInChildren<OfferingBowl>(true));
        }

        if (TryGetDetachedStructureRoot(itemStand.transform, out Transform? detachedRoot) && detachedRoot != null)
        {
            OfferingBowl? detachedOfferingBowl = FindNearestRelevantOfferingBowl(itemStand, detachedRoot.GetComponentsInChildren<OfferingBowl>(true));
            if (detachedOfferingBowl != null)
            {
                return detachedOfferingBowl;
            }
        }

        return FindNearestRelevantOfferingBowl(itemStand, OfferingBowlHoverInfoFormatter.GetKnownOfferingBowls());
    }

    internal static bool TryResolveOfferingBowlContext(OfferingBowl? offeringBowl, out string locationPrefab, out Transform root)
    {
        locationPrefab = "";
        root = null!;
        if (offeringBowl == null)
        {
            return false;
        }

        root = offeringBowl.transform;
        Location? location = offeringBowl.GetComponentInParent<Location>(true);
        if (location != null)
        {
            root = location.transform;
        }
        else if (TryGetDetachedStructureRoot(offeringBowl.transform, out Transform? detachedRoot) && detachedRoot != null)
        {
            root = detachedRoot;
        }

        if (AltarLocationResolver.TryResolveLocationPrefabName(location, out locationPrefab))
        {
            return locationPrefab.Length > 0;
        }

        if (AltarLocationResolver.TryResolveZoneLocationPrefabName(offeringBowl.transform.position, out locationPrefab))
        {
            return true;
        }

        LocationProxy? proxy = offeringBowl.GetComponentInParent<LocationProxy>(true);
        return proxy != null && AltarLocationResolver.TryResolveLocationProxyPrefabName(proxy, out locationPrefab);
    }

    internal static bool IsRelevantToOfferingBowl(ItemStand? itemStand, OfferingBowl? offeringBowl)
    {
        if (itemStand == null || offeringBowl == null || !offeringBowl.m_useItemStands)
        {
            return false;
        }

        if (Vector3.Distance(offeringBowl.transform.position, itemStand.transform.position) > offeringBowl.m_itemstandMaxRange)
        {
            return false;
        }

        return itemStand.gameObject.name.StartsWith(offeringBowl.m_itemStandPrefix ?? "", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool TryGetDetachedStructureRoot(Transform transform, out Transform? root)
    {
        if (transform == null)
        {
            root = null;
            return false;
        }

        Transform current = transform;
        while (current.parent != null)
        {
            Transform parent = current.parent;
            if (parent.GetComponent<Location>() != null ||
                parent.GetComponent<LocationProxy>() != null ||
                string.Equals(parent.name, "_ZoneCtrl(Clone)", StringComparison.Ordinal))
            {
                break;
            }

            current = parent;
        }

        root = current;
        return root != null;
    }

    private static string BuildInfo(ItemStand itemStand)
    {
        if (itemStand.m_supportedItems == null || itemStand.m_supportedItems.Count == 0)
        {
            return "";
        }

        return string.Join(", ", itemStand.m_supportedItems
            .Where(item => item != null)
            .Select(OfferingBowlHoverInfoFormatter.GetItemDisplayName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal));
    }

    private static string GetCachedInfo(ItemStand itemStand)
    {
        int instanceId = itemStand.GetInstanceID();
        float now = Time.realtimeSinceStartup;
        if (HoverInfoCache.TryGetValue(instanceId, out HoverInfoCacheEntry? cached) &&
            cached.ExpiresAt > now)
        {
            return cached.Info;
        }

        string info = BuildInfo(itemStand);
        HoverInfoCache[instanceId] = new HoverInfoCacheEntry
        {
            Info = info,
            ExpiresAt = now + HoverInfoCacheSeconds
        };
        return info;
    }

    private static void AddRelevantItemStands(Transform root, OfferingBowl offeringBowl, List<ItemStand> itemStands, HashSet<int> seenIds)
    {
        foreach (ItemStand itemStand in root.GetComponentsInChildren<ItemStand>(true))
        {
            if (itemStand == null || !seenIds.Add(itemStand.GetInstanceID()) || !IsRelevantToOfferingBowl(itemStand, offeringBowl))
            {
                continue;
            }

            itemStands.Add(itemStand);
        }
    }

    private static OfferingBowl? FindNearestRelevantOfferingBowl(ItemStand itemStand, IEnumerable<OfferingBowl> candidates)
    {
        OfferingBowl? nearest = null;
        float bestDistance = float.MaxValue;
        foreach (OfferingBowl candidate in candidates)
        {
            if (candidate == null || !IsRelevantToOfferingBowl(itemStand, candidate))
            {
                continue;
            }

            float distance = Vector3.SqrMagnitude(candidate.transform.position - itemStand.transform.position);
            if (distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            nearest = candidate;
        }

        return nearest;
    }

    private static bool TryGetOfferingBowlStructuralRoot(OfferingBowl offeringBowl, out Transform? root)
    {
        root = offeringBowl.GetComponentInParent<Location>(true)?.transform;
        return root != null || TryGetDetachedStructureRoot(offeringBowl.transform, out root);
    }

    private static int GetDisplayPriority(ItemStand itemStand)
    {
        int score = itemStand.gameObject.activeInHierarchy ? 4 : 0;
        if (itemStand.GetComponentInParent<Location>(true) == null)
        {
            score += 2;
        }

        return score + (itemStand.m_supportedItems?.Count ?? 0);
    }

    private static void EnsureRegistryPopulated()
    {
        if (RegisteredItemStands.Count > 0)
        {
            return;
        }

        foreach (ItemStand itemStand in UnityEngine.Object.FindObjectsByType<ItemStand>(FindObjectsSortMode.None))
        {
            RegisterItemStand(itemStand);
        }
    }

    private static void CleanupRegisteredItemStands()
    {
        for (int index = RegisteredItemStands.Count - 1; index >= 0; index--)
        {
            if (RegisteredItemStands[index] != null)
            {
                continue;
            }

            RegisteredItemStands.RemoveAt(index);
        }

        RegisteredItemStandIds.Clear();
        foreach (ItemStand itemStand in RegisteredItemStands)
        {
            if (itemStand != null)
            {
                RegisteredItemStandIds.Add(itemStand.GetInstanceID());
            }
        }
    }
}
