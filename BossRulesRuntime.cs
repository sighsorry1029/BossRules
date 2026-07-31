using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BossRules;

internal static class BossRulesRuntime
{
    private sealed class CompiledDespawnRule
    {
        public float? RangeOverride { get; set; }
        public float? DelayOverride { get; set; }
        public bool RefundsEnabled { get; set; } = true;
    }

    private sealed class RuntimeState
    {
        public static RuntimeState Empty { get; } = new();

        public Dictionary<string, CompiledDespawnRule> RulesByPrefab { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<int, CompiledDespawnRule> RulesByPrefabHash { get; } = new();
        public Dictionary<int, string> PrefabNamesByHash { get; } = new();
        public HashSet<int> EligiblePrefabHashes { get; } = new();
        public HashSet<string> BootstrapPrefabs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<string> BootstrapPrefabOrder { get; set; } = Array.Empty<string>();
    }

    private sealed class BossCatalog
    {
        public static BossCatalog Empty { get; } = new();

        public int GameDataSignature { get; set; } = -1;
        public HashSet<string> BossPrefabNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<int> BossPrefabHashes { get; } = new();
    }

    private static readonly object Sync = new();
    private static RuntimeState _runtimeState = RuntimeState.Empty;
    private static BossCatalog _bossCatalog = BossCatalog.Empty;
    private static int _runtimeGameDataSignature = -1;
    private static int _runtimeConfigurationVersion = -1;
    private static int _configurationVersion;
    private static int _cachedGameDataMarker = int.MinValue;
    private static int _cachedGameDataSignature = -1;
    private static bool _cachedGameDataSignatureDirty = true;
    private static BossRuleConfigurationState _configuration = BossRuleConfigurationState.Empty;
    private static int _despawnLookupVersion;

    internal static void Reload(BossRuleConfigurationState configuration)
    {
        lock (Sync)
        {
            _configuration = configuration ?? BossRuleConfigurationState.Empty;
            _runtimeState = RuntimeState.Empty;
            _runtimeGameDataSignature = -1;
            _runtimeConfigurationVersion = -1;
            _configurationVersion++;
            _cachedGameDataSignatureDirty = true;
            _despawnLookupVersion++;
            DespawnRulesManager.ConfigureDefaults(
                _configuration.DefaultDespawnRange,
                _configuration.DefaultDespawnDelaySeconds);
            DespawnRulesManager.MarkBootstrapScanDirty();
            BossTamedPressureRuntime.Configure(
                _configuration.BossTamedPressureRule);
        }
    }

    internal static void Reset()
    {
        lock (Sync)
        {
            _configuration = BossRuleConfigurationState.Empty;
            _runtimeState = RuntimeState.Empty;
            _runtimeGameDataSignature = -1;
            _runtimeConfigurationVersion = -1;
            _configurationVersion++;
            _cachedGameDataSignatureDirty = true;
            _bossCatalog = BossCatalog.Empty;
            _despawnLookupVersion++;
            DespawnRulesManager.ResetRuntimeState();
            DespawnRulesManager.ConfigureDefaults(
                BossRuleConfigurationState.FallbackDespawnRange,
                BossRuleConfigurationState.FallbackDespawnDelaySeconds);
            BossTamedPressureRuntime.ResetRuntimeState();
        }
    }

    internal static int GetDespawnLookupVersion()
    {
        EnsureRuntimeState();
        return _despawnLookupVersion;
    }

    internal static IReadOnlyList<string> GetDespawnBootstrapPrefabOrder()
    {
        EnsureRuntimeState();
        return _runtimeState.BootstrapPrefabOrder;
    }

    internal static bool IsDespawnTrackingRuleLookupReady()
    {
        EnsureRuntimeState();
        return _runtimeGameDataSignature >= 0;
    }

    internal static bool TryGetCachedDespawnTrackingPrefabHashEligibility(int prefabHash, out bool eligible)
    {
        eligible = false;
        if (prefabHash == 0 || _runtimeGameDataSignature < 0)
        {
            return false;
        }

        eligible = _runtimeState.EligiblePrefabHashes.Contains(prefabHash);
        return true;
    }

    internal static bool IsEligibleDespawnTrackingPrefabHash(int prefabHash)
    {
        if (prefabHash == 0)
        {
            return false;
        }

        EnsureRuntimeState();
        return _runtimeState.EligiblePrefabHashes.Contains(prefabHash);
    }

    internal static bool IsEligibleDespawnTrackingPrefabName(string prefabName)
    {
        if (string.IsNullOrWhiteSpace(prefabName))
        {
            return false;
        }

        EnsureRuntimeState();
        return _runtimeState.BootstrapPrefabs.Contains(prefabName);
    }

    internal static bool TryResolveDespawnTrackingRule(
        ZDO zdo,
        int prefabHashHint,
        string prefabNameHint,
        out string prefabName,
        out float? rangeOverride,
        out float? delayOverride,
        out IReadOnlyCollection<DespawnRefundDrop> refunds)
    {
        prefabName = string.IsNullOrWhiteSpace(prefabNameHint) ? "" : prefabNameHint;
        rangeOverride = null;
        delayOverride = null;
        refunds = Array.Empty<DespawnRefundDrop>();
        int prefabHash = zdo.GetPrefab();
        if (prefabHash == 0)
        {
            prefabHash = prefabHashHint;
        }

        EnsureRuntimeState();
        if (prefabHash != 0)
        {
            if (!_runtimeState.PrefabNamesByHash.TryGetValue(prefabHash, out string resolvedPrefabName) ||
                string.IsNullOrWhiteSpace(resolvedPrefabName))
            {
                resolvedPrefabName = ResolvePrefabName(prefabHash);
            }

            if (!string.IsNullOrWhiteSpace(resolvedPrefabName))
            {
                prefabName = resolvedPrefabName;
            }

            if (_runtimeState.RulesByPrefabHash.TryGetValue(prefabHash, out CompiledDespawnRule? explicitRule))
            {
                rangeOverride = explicitRule.RangeOverride;
                delayOverride = explicitRule.DelayOverride;
                refunds = ResolveRefundsForRule(explicitRule, zdo);
                return !string.IsNullOrWhiteSpace(prefabName);
            }

            if (IsAutoDetectedBossPrefab(prefabHash))
            {
                refunds = ResolveAutoAltarRefunds(zdo);
                return !string.IsNullOrWhiteSpace(prefabName);
            }
        }

        if (!string.IsNullOrWhiteSpace(prefabName) &&
            _runtimeState.RulesByPrefab.TryGetValue(prefabName, out CompiledDespawnRule? namedRule))
        {
            rangeOverride = namedRule.RangeOverride;
            delayOverride = namedRule.DelayOverride;
            refunds = ResolveRefundsForRule(namedRule, zdo);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(prefabName) && IsAutoDetectedBossPrefab(prefabName))
        {
            refunds = ResolveAutoAltarRefunds(zdo);
            return true;
        }

        return false;
    }

    internal static bool IsAutoDetectedBossPrefab(string prefabName)
    {
        if (string.IsNullOrWhiteSpace(prefabName))
        {
            return false;
        }

        BossCatalog catalog = EnsureBossCatalog();
        return catalog.BossPrefabNames.Contains(prefabName);
    }

    internal static bool IsAutoDetectedBossPrefab(int prefabHash)
    {
        if (prefabHash == 0)
        {
            return false;
        }

        BossCatalog catalog = EnsureBossCatalog();
        return catalog.BossPrefabHashes.Contains(prefabHash);
    }

    internal static IReadOnlyCollection<string> GetAutoDetectedBossPrefabNames()
    {
        return EnsureBossCatalog().BossPrefabNames;
    }

    internal static int ComputeGameDataSignature()
    {
        unchecked
        {
            int hash = 17;
            foreach (GameObject prefab in EnumeratePrefabsForRuntime())
            {
                if (prefab == null)
                {
                    continue;
                }

                string prefabName = GetPrefabNameForRuntime(prefab);
                if (prefabName.Length == 0)
                {
                    continue;
                }

                hash = hash * 31 + prefabName.GetStableHashCode();
            }

            return hash;
        }
    }

    internal static int GetGameDataSignature()
    {
        int marker = GetGameDataMarker();
        if (!_cachedGameDataSignatureDirty && _cachedGameDataMarker == marker)
        {
            return _cachedGameDataSignature;
        }

        _cachedGameDataMarker = marker;
        _cachedGameDataSignature = ComputeGameDataSignature();
        _cachedGameDataSignatureDirty = false;
        return _cachedGameDataSignature;
    }

    internal static IEnumerable<GameObject> EnumeratePrefabsForRuntime()
    {
        HashSet<GameObject> seen = new();
        if (ZNetScene.instance?.m_prefabs != null)
        {
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab != null && seen.Add(prefab))
                {
                    yield return prefab;
                }
            }
        }

        if (ObjectDB.instance?.m_items == null)
        {
            yield break;
        }

        foreach (GameObject prefab in ObjectDB.instance.m_items)
        {
            if (prefab != null && seen.Add(prefab))
            {
                yield return prefab;
            }
        }
    }

    internal static string GetPrefabNameForRuntime(GameObject? prefab)
    {
        if (prefab == null)
        {
            return "";
        }

        string prefabName = Utils.GetPrefabName(prefab);
        if (!string.IsNullOrWhiteSpace(prefabName))
        {
            return prefabName;
        }

        return TrimCloneSuffix(prefab.name);
    }

    internal static void WarnInvalidEntry(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            BossRulesPlugin.BossRulesLogger.LogWarning(message);
        }
    }

    private static IReadOnlyCollection<DespawnRefundDrop> ResolveRefundsForRule(CompiledDespawnRule rule, ZDO? zdo)
    {
        return rule.RefundsEnabled ? ResolveAutoAltarRefunds(zdo) : Array.Empty<DespawnRefundDrop>();
    }

    private static IReadOnlyCollection<DespawnRefundDrop> ResolveAutoAltarRefunds(ZDO? zdo)
    {
        return zdo != null && AltarRuntime.TryResolveAltarSummonRefunds(zdo, out IReadOnlyCollection<DespawnRefundDrop> refunds)
            ? refunds
            : Array.Empty<DespawnRefundDrop>();
    }

    private static void EnsureRuntimeState()
    {
        int gameDataSignature = GetGameDataSignature();
        if (_runtimeGameDataSignature == gameDataSignature &&
            _runtimeConfigurationVersion == _configurationVersion)
        {
            return;
        }

        _runtimeState = BuildRuntimeState(_configuration);
        _runtimeGameDataSignature = gameDataSignature;
        _runtimeConfigurationVersion = _configurationVersion;
        _despawnLookupVersion++;
    }

    private static RuntimeState BuildRuntimeState(BossRuleConfigurationState configuration)
    {
        RuntimeState state = new();
        foreach (string prefabName in GetAutoDetectedBossPrefabNames())
        {
            state.BootstrapPrefabs.Add(prefabName);
            int prefabHash = prefabName.GetStableHashCode();
            state.PrefabNamesByHash[prefabHash] = prefabName;
            state.EligiblePrefabHashes.Add(prefabHash);
        }

        foreach (BossDespawnDefinition entry in configuration.DespawnRules)
        {
            if (string.IsNullOrWhiteSpace(entry.Prefab))
            {
                continue;
            }

            CompiledDespawnRule compiledRule = new()
            {
                RangeOverride = entry.DespawnRange,
                DelayOverride = entry.DespawnDelay,
                RefundsEnabled = entry.Refunds != false
            };

            string prefabName = entry.Prefab.Trim();
            if (state.RulesByPrefab.ContainsKey(prefabName))
            {
                WarnInvalidEntry($"Character prefab '{prefabName}' defines multiple despawn rules. The later entry overrides the earlier entry.");
            }

            int prefabHash = prefabName.GetStableHashCode();
            state.RulesByPrefab[prefabName] = compiledRule;
            state.RulesByPrefabHash[prefabHash] = compiledRule;
            state.PrefabNamesByHash[prefabHash] = prefabName;
            state.EligiblePrefabHashes.Add(prefabHash);
            state.BootstrapPrefabs.Add(prefabName);
        }

        state.BootstrapPrefabOrder = state.BootstrapPrefabs
            .OrderBy(prefabName => prefabName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return state;
    }

    private static BossCatalog EnsureBossCatalog()
    {
        int gameDataSignature = GetGameDataSignature();
        if (_bossCatalog.GameDataSignature == gameDataSignature)
        {
            return _bossCatalog;
        }

        BossCatalog catalog = new()
        {
            GameDataSignature = gameDataSignature
        };

        foreach (GameObject prefab in EnumeratePrefabsForRuntime())
        {
            if (prefab == null || !prefab.TryGetComponent(out Character character) || !character.IsBoss())
            {
                continue;
            }

            string prefabName = GetPrefabNameForRuntime(prefab);
            if (prefabName.Length == 0)
            {
                continue;
            }

            catalog.BossPrefabNames.Add(prefabName);
            catalog.BossPrefabHashes.Add(prefabName.GetStableHashCode());
        }

        _bossCatalog = catalog;
        return _bossCatalog;
    }

    private static string ResolvePrefabName(int prefabHash)
    {
        if (prefabHash == 0 || ZNetScene.instance == null)
        {
            return "";
        }

        GameObject? prefab = ZNetScene.instance.GetPrefab(prefabHash);
        return prefab != null ? prefab.name : "";
    }

    private static int GetGameDataMarker()
    {
        unchecked
        {
            int hash = 17;
            ZNetScene? zNetScene = ZNetScene.instance;
            hash = hash * 31 + (zNetScene != null ? zNetScene.GetInstanceID() : 0);
            hash = hash * 31 + (zNetScene?.m_prefabs?.Count ?? -1);

            ObjectDB? objectDb = ObjectDB.instance;
            hash = hash * 31 + (objectDb != null ? objectDb.GetInstanceID() : 0);
            hash = hash * 31 + (objectDb?.m_items?.Count ?? -1);
            return hash;
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
