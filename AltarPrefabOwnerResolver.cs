using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BepInEx.Bootstrap;
using UnityEngine;

namespace BossRules;

internal readonly struct AltarPrefabOwnerSource
{
    internal AltarPrefabOwnerSource(
        string prefabName,
        string prefabAssetId,
        string sourcePrefabName)
    {
        PrefabName = (prefabName ?? "").Trim();
        PrefabAssetId = NormalizeAssetId(prefabAssetId);
        SourcePrefabName = (sourcePrefabName ?? "").Trim();
    }

    internal string PrefabName { get; }
    internal string PrefabAssetId { get; }
    internal string SourcePrefabName { get; }

    private static string NormalizeAssetId(string? assetId)
    {
        return (assetId ?? "").Trim().ToLowerInvariant();
    }
}

internal sealed class AltarPrefabOwnerSnapshot
{
    private readonly Dictionary<string, string> _owners;

    internal AltarPrefabOwnerSnapshot(Dictionary<string, string> owners)
    {
        _owners = owners ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    internal string GetOwnerName(string? prefabName)
    {
        string normalized = (prefabName ?? "").Trim();
        return normalized.Length > 0 &&
               _owners.TryGetValue(normalized, out string? ownerName) &&
               !string.IsNullOrWhiteSpace(ownerName)
            ? ownerName
            : AltarPrefabOwnerResolver.UnknownOwnerName;
    }
}

internal static class AltarPrefabOwnerResolver
{
    internal const string VanillaOwnerName = "Valheim";
    internal const string UnknownOwnerName = "Unknown / Untracked";

    private const string CacheFormatVersion = "v1";
    private const string ResolverLogicVersion = "location-owner-v4";
    private const string RuntimeAssetIdPrefix = "000000010000000100000001";
    private const string JotunnZoneManagerTypeName = "Jotunn.Managers.ZoneManager";
    private const string JotunnPrefabManagerTypeName = "Jotunn.Managers.PrefabManager";
    private const string LocationManagerTemplateTypeName = "LocationManager.Location";
    private static readonly object Sync = new();
    private static readonly HashSet<string> VanillaPrefabNames = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> VanillaAssetIds = new(StringComparer.OrdinalIgnoreCase);
    private static string _snapshotSignature = "";
    private static AltarPrefabOwnerSnapshot _snapshot =
        new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    private static VanillaCatalogState _vanillaCatalogState;
    private static bool _resolverWarningLogged;
    private static bool _cacheWarningLogged;
    private static bool _vanillaCatalogWarningLogged;
    private static bool _allowCacheLoad = true;

    private enum VanillaCatalogState
    {
        Uninitialized,
        Loaded,
        Unavailable
    }

    private readonly struct OwnerMapping
    {
        internal OwnerMapping(string ownerName, int priority)
        {
            OwnerName = ownerName;
            Priority = priority;
        }

        internal string OwnerName { get; }
        internal int Priority { get; }
    }

    private sealed class PluginResources
    {
        internal string OwnerName { get; set; } = "";
        internal string[] ResourceNames { get; set; } = Array.Empty<string>();
    }

    internal static AltarPrefabOwnerSnapshot GetSnapshot(
        IEnumerable<AltarPrefabOwnerSource> prefabSources)
    {
        List<AltarPrefabOwnerSource> normalizedSources =
            (prefabSources ?? Enumerable.Empty<AltarPrefabOwnerSource>())
            .Where(source => source.PrefabName.Length > 0)
            .GroupBy(source => source.PrefabName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(source => source.PrefabName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalizedSources.Count == 0)
        {
            return new AltarPrefabOwnerSnapshot(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        try
        {
            string signature = BuildSnapshotSignature(normalizedSources);
            if (string.Equals(signature, _snapshotSignature, StringComparison.Ordinal))
            {
                return _snapshot;
            }

            lock (Sync)
            {
                if (string.Equals(signature, _snapshotSignature, StringComparison.Ordinal))
                {
                    return _snapshot;
                }

                if (!_allowCacheLoad ||
                    !TryLoadSnapshotFromCache(
                        signature,
                        normalizedSources.Select(source => source.PrefabName).ToList(),
                        out Dictionary<string, string> cachedOwners))
                {
                    cachedOwners = BuildOwnerMappings(normalizedSources);
                    SaveSnapshotToCache(signature, cachedOwners);
                }

                _snapshot = new AltarPrefabOwnerSnapshot(cachedOwners);
                _snapshotSignature = signature;
                _allowCacheLoad = true;
                return _snapshot;
            }
        }
        catch (Exception ex)
        {
            WarnResolverFailure(ex);
            return BuildUnknownSnapshot(
                normalizedSources.Select(source => source.PrefabName));
        }
    }

    internal static void ResetRuntimeSnapshot(bool allowCacheLoad)
    {
        lock (Sync)
        {
            _snapshotSignature = "";
            _snapshot = new AltarPrefabOwnerSnapshot(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            _allowCacheLoad = allowCacheLoad;
        }
    }

    internal static int GetOwnerSortBucket(string ownerName)
    {
        if (string.Equals(ownerName, VanillaOwnerName, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return string.Equals(ownerName, UnknownOwnerName, StringComparison.OrdinalIgnoreCase)
            ? 2
            : 1;
    }

    internal static string NormalizeOwnerName(string? ownerName)
    {
        string normalized = (ownerName ?? "")
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return normalized.Length > 0 ? normalized : UnknownOwnerName;
    }

    private static Dictionary<string, string> BuildOwnerMappings(
        IReadOnlyCollection<AltarPrefabOwnerSource> prefabSources)
    {
        HashSet<string> lookupCandidates = new(StringComparer.OrdinalIgnoreCase);
        foreach (AltarPrefabOwnerSource source in prefabSources)
        {
            foreach (string candidate in EnumerateSourceLookupCandidates(source))
            {
                lookupCandidates.Add(candidate);
            }
        }

        Dictionary<string, OwnerMapping> provenance =
            new(StringComparer.OrdinalIgnoreCase);
        CollectJotunnManagerMappings(
            JotunnPrefabManagerTypeName,
            "Prefabs",
            priority: 2,
            lookupCandidates,
            provenance);
        CollectLocationManagerTemplateMappings(lookupCandidates, provenance);
        CollectJotunnManagerMappings(
            JotunnZoneManagerTypeName,
            "Locations",
            priority: 0,
            lookupCandidates,
            provenance);
        EnsureVanillaCatalogLoaded();

        HashSet<string> unresolvedBundleCandidates =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (AltarPrefabOwnerSource source in prefabSources)
        {
            if (HasProvenance(source, provenance) ||
                IsVanillaSource(source))
            {
                continue;
            }

            foreach (string candidate in EnumerateSourceLookupCandidates(source))
            {
                unresolvedBundleCandidates.Add(candidate);
            }
        }

        if (unresolvedBundleCandidates.Count > 0)
        {
            CollectLoadedBundleMappings(unresolvedBundleCandidates, provenance);
        }

        Dictionary<string, string> owners = new(StringComparer.OrdinalIgnoreCase);
        foreach (AltarPrefabOwnerSource source in prefabSources)
        {
            owners[source.PrefabName] = ResolveOwnerName(source, provenance);
        }

        return owners;
    }

    private static string ResolveOwnerName(
        AltarPrefabOwnerSource source,
        IReadOnlyDictionary<string, OwnerMapping> provenance)
    {
        foreach (string candidate in EnumerateSourceLookupCandidates(source))
        {
            if (provenance.TryGetValue(candidate, out OwnerMapping mapping) &&
                !string.IsNullOrWhiteSpace(mapping.OwnerName))
            {
                return NormalizeOwnerName(mapping.OwnerName);
            }
        }

        if (IsVanillaSource(source))
        {
            return VanillaOwnerName;
        }

        return UnknownOwnerName;
    }

    private static bool HasProvenance(
        AltarPrefabOwnerSource source,
        IReadOnlyDictionary<string, OwnerMapping> provenance)
    {
        return EnumerateSourceLookupCandidates(source)
            .Any(provenance.ContainsKey);
    }

    private static bool IsVanillaSource(AltarPrefabOwnerSource source)
    {
        if (_vanillaCatalogState == VanillaCatalogState.Loaded)
        {
            if (source.PrefabAssetId.Length > 0 &&
                VanillaAssetIds.Contains(source.PrefabAssetId))
            {
                return true;
            }

            if (EnumerateSourceLookupCandidates(source)
                .Any(VanillaPrefabNames.Contains))
            {
                return true;
            }
        }

        return QueenDungeonAltarSupport.IsSupportedLocationPrefab(
            source.PrefabName);
    }

    private static IEnumerable<string> EnumerateSourceLookupCandidates(
        AltarPrefabOwnerSource source)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string candidate in EnumerateLookupCandidates(source.PrefabName)
                     .Concat(EnumerateLookupCandidates(source.SourcePrefabName)))
        {
            if (seen.Add(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static IEnumerable<string> EnumerateLookupCandidates(string prefabName)
    {
        List<string> candidates = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        string normalized = (prefabName ?? "").Trim();
        if (normalized.Length == 0)
        {
            yield break;
        }

        void AddIfNew(string candidate)
        {
            string trimmed = (candidate ?? "").Trim();
            if (trimmed.Length > 0 && seen.Add(trimmed))
            {
                candidates.Add(trimmed);
            }
        }

        AddIfNew(normalized);
        string withoutCloneSuffix = TrimCloneSuffix(normalized);
        AddIfNew(withoutCloneSuffix);

        int aliasSeparatorIndex = withoutCloneSuffix.IndexOf(':');
        if (aliasSeparatorIndex > 0)
        {
            AddIfNew(withoutCloneSuffix.Substring(0, aliasSeparatorIndex));
        }

        foreach (string candidate in candidates)
        {
            yield return candidate;
        }
    }

    private static void CollectLoadedBundleMappings(
        HashSet<string> lookupCandidates,
        Dictionary<string, OwnerMapping> mappings)
    {
        List<PluginResources> plugins = BuildPluginResources();
        IEnumerable<AssetBundle> bundles;
        try
        {
            bundles = AssetBundle.GetAllLoadedAssetBundles()
                .Where(bundle => bundle != null)
                .OrderBy(bundle => bundle.name ?? "", StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return;
        }

        foreach (AssetBundle bundle in bundles)
        {
            string bundleName = (bundle.name ?? "").Trim();
            if (bundleName.Length == 0)
            {
                continue;
            }

            List<string> matchingOwners = plugins
                .Where(candidate => candidate.ResourceNames.Any(resourceName =>
                    IsBundleResourceMatch(resourceName, bundleName)))
                .Select(candidate => candidate.OwnerName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (matchingOwners.Count != 1)
            {
                continue;
            }

            string[] assetNames;
            try
            {
                assetNames = bundle.GetAllAssetNames();
            }
            catch
            {
                continue;
            }

            foreach (string assetName in assetNames)
            {
                if (!assetName.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string candidate = Path.GetFileNameWithoutExtension(assetName).Trim();
                if (lookupCandidates.Contains(candidate))
                {
                    TryAddOwnerMapping(
                        mappings,
                        candidate,
                        matchingOwners[0],
                        priority: 3);
                }
            }
        }
    }

    private static bool IsBundleResourceMatch(
        string? resourceName,
        string bundleName)
    {
        string normalizedResource = (resourceName ?? "").Trim();
        string normalizedBundle = (bundleName ?? "").Trim();
        return normalizedResource.Length > 0 &&
               normalizedBundle.Length > 0 &&
               (string.Equals(
                    normalizedResource,
                    normalizedBundle,
                    StringComparison.OrdinalIgnoreCase) ||
                normalizedResource.EndsWith(
                    "." + normalizedBundle,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static List<PluginResources> BuildPluginResources()
    {
        List<PluginResources> plugins = new();
        foreach (var pluginInfo in Chainloader.PluginInfos.Values)
        {
            string ownerName = GetPluginDisplayName(pluginInfo.Metadata.Name, pluginInfo.Metadata.GUID);
            string[] resourceNames;
            try
            {
                resourceNames = pluginInfo.Instance?
                    .GetType()
                    .Assembly
                    .GetManifestResourceNames() ?? Array.Empty<string>();
            }
            catch
            {
                resourceNames = Array.Empty<string>();
            }

            plugins.Add(new PluginResources
            {
                OwnerName = ownerName,
                ResourceNames = resourceNames
            });
        }

        return plugins;
    }

    private static void CollectJotunnManagerMappings(
        string managerTypeName,
        string collectionMemberName,
        int priority,
        HashSet<string> lookupCandidates,
        Dictionary<string, OwnerMapping> mappings)
    {
        foreach (Assembly assembly in GetLoadedAssemblies())
        {
            Type? managerType = SafeGetType(assembly, managerTypeName);
            if (managerType == null)
            {
                continue;
            }

            object? managerInstance = TryGetStaticMemberValue(managerType, "Instance");
            if (managerInstance == null ||
                !TryGetRawMemberValue(
                    managerInstance,
                    collectionMemberName,
                    out object? collectionValue))
            {
                continue;
            }

            string fallbackOwner = ResolveAssemblyOwnerName(assembly);
            foreach (object holder in EnumerateCollectionValues(collectionValue))
            {
                string prefabName = NormalizePrefabName(GetPrefabNameFromHolder(holder));
                if (!lookupCandidates.Contains(prefabName))
                {
                    continue;
                }

                string ownerName = ResolveOwnerNameFromSourceModHolder(
                    holder,
                    fallbackOwner);
                TryAddOwnerMapping(
                    mappings,
                    prefabName,
                    ownerName,
                    priority);
            }
        }
    }

    private static void CollectLocationManagerTemplateMappings(
        HashSet<string> lookupCandidates,
        Dictionary<string, OwnerMapping> mappings)
    {
        foreach (Assembly assembly in GetLoadedAssemblies())
        {
            Type? locationType = SafeGetType(assembly, LocationManagerTemplateTypeName);
            FieldInfo? registeredLocationsField = locationType?.GetField(
                "registeredLocations",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (registeredLocationsField == null)
            {
                continue;
            }

            object? registeredLocations;
            try
            {
                registeredLocations = registeredLocationsField.GetValue(null);
            }
            catch
            {
                continue;
            }

            string ownerName = ResolveAssemblyOwnerName(assembly);
            foreach (object holder in EnumerateCollectionValues(registeredLocations))
            {
                string prefabName = NormalizePrefabName(GetPrefabNameFromHolder(holder));
                if (lookupCandidates.Contains(prefabName))
                {
                    TryAddOwnerMapping(
                        mappings,
                        prefabName,
                        ownerName,
                        priority: 1);
                }
            }
        }
    }

    private static IEnumerable<object> EnumerateCollectionValues(object? value)
    {
        IEnumerable enumerable = value is IDictionary dictionary
            ? dictionary.Values
            : value as IEnumerable ?? Array.Empty<object>();
        foreach (object? entry in enumerable)
        {
            if (entry != null)
            {
                yield return entry;
            }
        }
    }

    private static string? GetPrefabNameFromHolder(object? holder)
    {
        if (holder == null)
        {
            return null;
        }

        if (holder is GameObject gameObject)
        {
            return gameObject.name;
        }

        if (holder is Component component)
        {
            return component.gameObject != null
                ? component.gameObject.name
                : component.name;
        }

        if (TryGetRawMemberValue(holder, "Prefab", out object? prefabValue))
        {
            if (prefabValue is GameObject prefab)
            {
                return prefab.name;
            }

            if (prefabValue is Component prefabComponent)
            {
                return prefabComponent.gameObject != null
                    ? prefabComponent.gameObject.name
                    : prefabComponent.name;
            }
        }

        foreach (string memberName in new[] { "Location", "location" })
        {
            if (!TryGetRawMemberValue(holder, memberName, out object? locationValue))
            {
                continue;
            }

            if (locationValue is GameObject locationObject)
            {
                return locationObject.name;
            }

            if (locationValue is Component locationComponent)
            {
                return locationComponent.gameObject != null
                    ? locationComponent.gameObject.name
                    : locationComponent.name;
            }
        }

        if (TryGetRawMemberValue(holder, "ZoneLocation", out object? zoneLocation) &&
            zoneLocation != null &&
            TryGetRawMemberValue(
                zoneLocation,
                "m_prefabName",
                out object? zonePrefabName))
        {
            return zonePrefabName?.ToString();
        }

        return null;
    }

    private static string ResolveOwnerNameFromSourceModHolder(
        object holder,
        string fallbackOwnerName)
    {
        if (TryGetRawMemberValue(holder, "SourceMod", out object? sourceMod) &&
            sourceMod != null &&
            TryResolvePluginOwnerName(sourceMod, out string ownerName))
        {
            return ownerName;
        }

        return fallbackOwnerName;
    }

    private static bool TryResolvePluginOwnerName(object sourceMod, out string ownerName)
    {
        ownerName = "";
        if (TryGetRawMemberValue(sourceMod, "GUID", out object? guidValue))
        {
            string pluginGuid = (guidValue?.ToString() ?? "").Trim();
            if (pluginGuid.Length > 0 &&
                Chainloader.PluginInfos.TryGetValue(pluginGuid, out var pluginInfo))
            {
                ownerName = GetPluginDisplayName(
                    pluginInfo.Metadata.Name,
                    pluginInfo.Metadata.GUID);
                return true;
            }

            if (pluginGuid.Length > 0)
            {
                ownerName = pluginGuid;
                return true;
            }
        }

        if (TryGetRawMemberValue(sourceMod, "Name", out object? nameValue))
        {
            ownerName = (nameValue?.ToString() ?? "").Trim();
            return ownerName.Length > 0;
        }

        return false;
    }

    private static string ResolveAssemblyOwnerName(Assembly assembly)
    {
        foreach (var pluginInfo in Chainloader.PluginInfos.Values)
        {
            Assembly? pluginAssembly = pluginInfo.Instance?.GetType().Assembly;
            if (pluginAssembly == null || !ReferenceEquals(pluginAssembly, assembly))
            {
                continue;
            }

            return GetPluginDisplayName(
                pluginInfo.Metadata.Name,
                pluginInfo.Metadata.GUID);
        }

        string assemblyName = assembly.GetName().Name ?? assembly.FullName ?? "";
        return NormalizeOwnerName(assemblyName);
    }

    private static string GetPluginDisplayName(string? pluginName, string? pluginGuid)
    {
        string normalizedName = NormalizeOwnerName(pluginName);
        if (!string.Equals(
                normalizedName,
                UnknownOwnerName,
                StringComparison.OrdinalIgnoreCase))
        {
            return normalizedName;
        }

        return NormalizeOwnerName(pluginGuid);
    }

    private static void TryAddOwnerMapping(
        Dictionary<string, OwnerMapping> mappings,
        string prefabName,
        string ownerName,
        int priority)
    {
        string normalizedPrefabName = NormalizePrefabName(prefabName);
        string normalizedOwnerName = NormalizeOwnerName(ownerName);
        if (normalizedPrefabName.Length == 0 ||
            string.Equals(
                normalizedOwnerName,
                UnknownOwnerName,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (mappings.TryGetValue(normalizedPrefabName, out OwnerMapping existing) &&
            existing.Priority <= priority)
        {
            return;
        }

        mappings[normalizedPrefabName] =
            new OwnerMapping(normalizedOwnerName, priority);
    }

    private static void EnsureVanillaCatalogLoaded()
    {
        if (_vanillaCatalogState != VanillaCatalogState.Uninitialized)
        {
            return;
        }

        string[] manifestPaths = GetVanillaManifestPaths()
            .Where(File.Exists)
            .ToArray();
        if (manifestPaths.Length == 0)
        {
            _vanillaCatalogState = VanillaCatalogState.Unavailable;
            WarnVanillaCatalogFailure(
                $"Vanilla prefab manifests were not found under " +
                $"'{GetVanillaManifestDirectoryPath()}'.");
            return;
        }

        int loadedManifestCount = 0;
        string firstFailedManifestPath = "";
        Exception? firstFailure = null;
        foreach (string manifestPath in manifestPaths)
        {
            try
            {
                ReadVanillaManifest(manifestPath);
                loadedManifestCount++;
            }
            catch (Exception ex)
            {
                firstFailure ??= ex;
                if (firstFailedManifestPath.Length == 0)
                {
                    firstFailedManifestPath = manifestPath;
                }
            }
        }

        if (loadedManifestCount > 0)
        {
            _vanillaCatalogState = VanillaCatalogState.Loaded;
            if (firstFailure != null)
            {
                WarnVanillaCatalogFailure(
                    $"Failed to read one vanilla prefab manifest at " +
                    $"'{firstFailedManifestPath}'. Other manifests remain available. " +
                    $"{firstFailure.GetType().Name}: {firstFailure.Message}");
            }

            return;
        }

        VanillaPrefabNames.Clear();
        VanillaAssetIds.Clear();
        _vanillaCatalogState = VanillaCatalogState.Unavailable;
        WarnVanillaCatalogFailure(
            $"Failed to read vanilla prefab manifests under " +
            $"'{GetVanillaManifestDirectoryPath()}'. " +
            $"{firstFailure?.GetType().Name ?? "Unknown error"}: " +
            $"{firstFailure?.Message ?? "No manifest could be read."}");
    }

    private static void ReadVanillaManifest(string manifestPath)
    {
        const string marker = "path in bundle:";
        const string assetIdMarker = "asset ID:";
        HashSet<string> prefabNames = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> assetIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (string rawLine in File.ReadLines(manifestPath))
        {
            int assetIdMarkerIndex = rawLine.IndexOf(
                assetIdMarker,
                StringComparison.OrdinalIgnoreCase);
            if (assetIdMarkerIndex >= 0)
            {
                string assetId = rawLine
                    .Substring(assetIdMarkerIndex + assetIdMarker.Length)
                    .Trim()
                    .ToLowerInvariant();
                if (assetId.Length > 0)
                {
                    assetIds.Add(assetId);
                }
            }

            int markerIndex = rawLine.IndexOf(
                marker,
                StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                continue;
            }

            string bundlePath = rawLine
                .Substring(markerIndex + marker.Length)
                .Trim();
            if (!bundlePath.EndsWith(
                    ".prefab",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string prefabName = Path.GetFileNameWithoutExtension(bundlePath);
            if (!string.IsNullOrWhiteSpace(prefabName))
            {
                prefabNames.Add(prefabName.Trim());
            }
        }

        VanillaPrefabNames.UnionWith(prefabNames);
        VanillaAssetIds.UnionWith(assetIds);
    }

    private static string BuildSnapshotSignature(
        IReadOnlyCollection<AltarPrefabOwnerSource> prefabSources)
    {
        StringBuilder builder = new();
        builder.AppendLine(ResolverLogicVersion);
        foreach (AltarPrefabOwnerSource source in prefabSources)
        {
            builder.Append("prefab:")
                .Append(source.PrefabName)
                .Append(':')
                .Append(source.SourcePrefabName)
                .Append(':')
                .Append(GetStableAssetIdSignature(source.PrefabAssetId))
                .AppendLine();
        }

        foreach (var pluginInfo in Chainloader.PluginInfos.Values
                     .OrderBy(
                         pluginInfo => pluginInfo.Metadata.GUID ?? "",
                         StringComparer.OrdinalIgnoreCase))
        {
            Assembly? assembly = pluginInfo.Instance?.GetType().Assembly;
            builder.Append("plugin:")
                .Append(pluginInfo.Metadata.GUID ?? "")
                .Append(':')
                .Append(pluginInfo.Metadata.Name ?? "")
                .Append(':')
                .Append(GetAssemblyModuleVersionId(assembly))
                .AppendLine();
        }

        foreach (Assembly assembly in GetLoadedAssemblies())
        {
            builder.Append("assembly:")
                .Append(assembly.FullName ?? assembly.GetName().Name ?? "")
                .Append(':')
                .Append(GetAssemblyModuleVersionId(assembly))
                .AppendLine();
        }

        try
        {
            foreach (string bundleName in AssetBundle
                         .GetAllLoadedAssetBundles()
                         .Select(bundle => bundle?.name ?? "")
                         .Where(bundleName => bundleName.Length > 0)
                         .OrderBy(bundleName => bundleName, StringComparer.OrdinalIgnoreCase))
            {
                builder.Append("bundle:").AppendLine(bundleName);
            }
        }
        catch
        {
            builder.AppendLine("bundle:<unavailable>");
        }

        foreach (string manifestPath in GetVanillaManifestPaths())
        {
            builder.Append("vanilla:")
                .Append(Path.GetFileName(manifestPath))
                .Append(':');
            if (File.Exists(manifestPath))
            {
                FileInfo manifest = new(manifestPath);
                builder.Append(manifest.Length)
                    .Append(':')
                    .Append(manifest.LastWriteTimeUtc.Ticks)
                    .AppendLine();
            }
            else
            {
                builder.AppendLine("<missing>");
            }
        }

        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(
            Encoding.UTF8.GetBytes(builder.ToString()));
        StringBuilder hashBuilder = new(hash.Length * 2);
        foreach (byte value in hash)
        {
            hashBuilder.Append(value.ToString("x2"));
        }

        return hashBuilder.ToString();
    }

    private static string GetStableAssetIdSignature(string assetId)
    {
        string normalized = (assetId ?? "").Trim().ToLowerInvariant();
        return normalized.StartsWith(
            RuntimeAssetIdPrefix,
            StringComparison.Ordinal)
            ? "<runtime>"
            : normalized;
    }

    private static Assembly[] GetLoadedAssemblies()
    {
        return AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(assembly => !assembly.IsDynamic)
            .OrderBy(
                assembly => assembly.FullName ?? assembly.GetName().Name ?? "",
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetAssemblyModuleVersionId(Assembly? assembly)
    {
        if (assembly == null)
        {
            return "";
        }

        try
        {
            return assembly.ManifestModule.ModuleVersionId.ToString("N");
        }
        catch
        {
            return "";
        }
    }

    private static bool TryLoadSnapshotFromCache(
        string signature,
        IReadOnlyCollection<string> prefabNames,
        out Dictionary<string, string> owners)
    {
        owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string cachePath = GetCachePath();
        if (!File.Exists(cachePath))
        {
            return false;
        }

        try
        {
            string[] lines = File.ReadAllLines(cachePath);
            if (lines.Length < 2 ||
                !string.Equals(
                    lines[0],
                    CacheFormatVersion,
                    StringComparison.Ordinal) ||
                !string.Equals(lines[1], signature, StringComparison.Ordinal))
            {
                return false;
            }

            for (int index = 2; index < lines.Length; index++)
            {
                string[] parts = lines[index].Split('\t');
                if (parts.Length != 2)
                {
                    continue;
                }

                string prefabName = DecodeCacheField(parts[0]);
                string ownerName = NormalizeOwnerName(
                    DecodeCacheField(parts[1]));
                if (prefabName.Length > 0)
                {
                    owners[prefabName] = ownerName;
                }
            }

            return prefabNames.All(owners.ContainsKey) &&
                   owners.Values.All(ownerName =>
                       !string.Equals(
                           ownerName,
                           UnknownOwnerName,
                           StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            owners.Clear();
            return false;
        }
    }

    private static void SaveSnapshotToCache(
        string signature,
        IReadOnlyDictionary<string, string> owners)
    {
        if (owners.Values.Any(ownerName =>
                string.Equals(
                    ownerName,
                    UnknownOwnerName,
                    StringComparison.OrdinalIgnoreCase)))
        {
            DeleteSnapshotCache();
            return;
        }

        try
        {
            string cachePath = GetCachePath();
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            StringBuilder builder = new();
            builder.AppendLine(CacheFormatVersion);
            builder.AppendLine(signature);
            foreach (KeyValuePair<string, string> pair in owners
                         .OrderBy(
                             pair => pair.Key,
                             StringComparer.OrdinalIgnoreCase))
            {
                builder.Append(EncodeCacheField(pair.Key))
                    .Append('\t')
                    .Append(EncodeCacheField(pair.Value))
                    .AppendLine();
            }

            string content = builder.ToString();
            string existing = File.Exists(cachePath)
                ? File.ReadAllText(cachePath)
                : "";
            if (!string.Equals(existing, content, StringComparison.Ordinal))
            {
                File.WriteAllText(cachePath, content, new UTF8Encoding(false));
            }
        }
        catch (Exception ex)
        {
            if (_cacheWarningLogged)
            {
                return;
            }

            _cacheWarningLogged = true;
            BossRulesPlugin.BossRulesLogger.LogWarning(
                $"Failed to cache altar prefab owner mappings. " +
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void DeleteSnapshotCache()
    {
        try
        {
            string cachePath = GetCachePath();
            if (File.Exists(cachePath))
            {
                File.Delete(cachePath);
            }
        }
        catch (Exception ex)
        {
            if (_cacheWarningLogged)
            {
                return;
            }

            _cacheWarningLogged = true;
            BossRulesPlugin.BossRulesLogger.LogWarning(
                $"Failed to invalidate altar prefab owner cache. " +
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string GetCachePath()
    {
        return Path.Combine(
            BossRulesPlugin.ConfigDirectoryPath,
            "cache",
            ".altar-prefab-owner-cache.txt");
    }

    private static IEnumerable<string> GetVanillaManifestPaths()
    {
        string manifestDirectoryPath = GetVanillaManifestDirectoryPath();
        yield return Path.Combine(manifestDirectoryPath, "manifest");
        yield return Path.Combine(manifestDirectoryPath, "manifest_extended");
    }

    private static string GetVanillaManifestDirectoryPath()
    {
        return Path.Combine(
            Application.dataPath,
            "StreamingAssets",
            "SoftRef");
    }

    private static string EncodeCacheField(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
    }

    private static string DecodeCacheField(string value)
    {
        try
        {
            return Encoding.UTF8.GetString(
                Convert.FromBase64String(value ?? ""));
        }
        catch
        {
            return "";
        }
    }

    private static AltarPrefabOwnerSnapshot BuildUnknownSnapshot(
        IEnumerable<string> prefabNames)
    {
        return new AltarPrefabOwnerSnapshot(
            prefabNames.ToDictionary(
                prefabName => prefabName,
                _ => UnknownOwnerName,
                StringComparer.OrdinalIgnoreCase));
    }

    private static void WarnResolverFailure(Exception exception)
    {
        if (_resolverWarningLogged)
        {
            return;
        }

        _resolverWarningLogged = true;
        BossRulesPlugin.BossRulesLogger.LogWarning(
            $"Failed to resolve altar prefab owners; reference entries will be grouped under " +
            $"'{UnknownOwnerName}'. {exception.GetType().Name}: {exception.Message}");
    }

    private static void WarnVanillaCatalogFailure(string reason)
    {
        if (_vanillaCatalogWarningLogged)
        {
            return;
        }

        _vanillaCatalogWarningLogged = true;
        BossRulesPlugin.BossRulesLogger.LogWarning(
            $"{reason} Unresolved altar location owners will be grouped under " +
            $"'{UnknownOwnerName}'.");
    }

    private static Type? SafeGetType(Assembly assembly, string fullTypeName)
    {
        try
        {
            return assembly.GetType(
                fullTypeName,
                throwOnError: false,
                ignoreCase: false);
        }
        catch
        {
            return null;
        }
    }

    private static object? TryGetStaticMemberValue(
        Type type,
        string memberName)
    {
        try
        {
            PropertyInfo? property = type.GetProperty(
                memberName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
            {
                return property.GetValue(null, null);
            }

            FieldInfo? field = type.GetField(
                memberName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return field?.GetValue(null);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetRawMemberValue(
        object instance,
        string memberName,
        out object? value)
    {
        value = null;
        Type? currentType = instance.GetType();
        while (currentType != null)
        {
            try
            {
                PropertyInfo? property = currentType.GetProperty(
                    memberName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null)
                {
                    value = property.GetValue(instance, null);
                    return true;
                }

                FieldInfo? field = currentType.GetField(
                    memberName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    value = field.GetValue(instance);
                    return true;
                }
            }
            catch
            {
                return false;
            }

            currentType = currentType.BaseType;
        }

        return false;
    }

    private static string NormalizePrefabName(string? prefabName)
    {
        return TrimCloneSuffix((prefabName ?? "").Trim());
    }

    private static string TrimCloneSuffix(string prefabName)
    {
        const string cloneSuffix = "(Clone)";
        return prefabName.EndsWith(cloneSuffix, StringComparison.Ordinal)
            ? prefabName.Substring(
                    0,
                    prefabName.Length - cloneSuffix.Length)
                .TrimEnd()
            : prefabName;
    }
}
