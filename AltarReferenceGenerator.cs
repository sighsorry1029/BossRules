using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BossRules;

internal sealed class AltarReferenceEntry
{
    [YamlMember(Order = 1)]
    public string Prefab { get; set; } = "";

    [YamlMember(Order = 2)]
    public AltarOfferingBowlDefinition? OfferingBowl { get; set; }

    [YamlMember(Order = 3)]
    public List<AltarReferenceItemStandDefinition>? ItemStands { get; set; }
}

internal sealed class AltarReferenceItemStandDefinition
{
    [YamlMember(Order = 1)]
    public string? Path { get; set; }

    [YamlMember(Order = 2)]
    public bool? CanBeRemoved { get; set; }

    [YamlMember(Order = 3)]
    public bool? AutoAttach { get; set; }

    [YamlMember(Order = 4)]
    public string? OrientationType { get; set; }

    [YamlMember(Order = 5)]
    public FlowStringListDefinition? SupportedTypes { get; set; }

    [YamlMember(Order = 6)]
    public FlowStringListDefinition? SupportedItems { get; set; }

    [YamlMember(Order = 7)]
    public FlowStringListDefinition? UnsupportedItems { get; set; }

    [YamlMember(Order = 8)]
    public float? PowerActivationDelay { get; set; }

    [YamlMember(Order = 9)]
    public string? GuardianPower { get; set; }
}

internal sealed class FlowStringListDefinition : IYamlConvertible
{
    public FlowStringListDefinition()
    {
    }

    public FlowStringListDefinition(IEnumerable<string> values)
    {
        Values = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToList();
    }

    public List<string> Values { get; set; } = new();

    void IYamlConvertible.Read(IParser parser, Type expectedType, ObjectDeserializer nestedObjectDeserializer)
    {
        Values.Clear();
        parser.Consume<SequenceStart>();
        while (!parser.Accept<SequenceEnd>(out _))
        {
            Values.Add((parser.Consume<Scalar>().Value ?? "").Trim());
        }

        parser.Consume<SequenceEnd>();
    }

    void IYamlConvertible.Write(IEmitter emitter, ObjectSerializer nestedObjectSerializer)
    {
        emitter.Emit(new SequenceStart(null, null, true, SequenceStyle.Flow));
        foreach (string value in Values)
        {
            emitter.Emit(new Scalar(value));
        }

        emitter.Emit(new SequenceEnd());
    }
}

internal static class AltarReferenceGenerator
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitDefaults)
        .Build();
    private const float AutoRefreshRetryDelaySeconds = 5f;
    private static readonly HashSet<string> DuplicateComponentWarnings = new(StringComparer.OrdinalIgnoreCase);
    private static bool _autoRefreshDone;
    private static float _nextAutoRefreshAttemptAt;

    internal static void ResetAutoRefresh()
    {
        _autoRefreshDone = false;
        _nextAutoRefreshAttemptAt = 0f;
    }

    internal static void TryAutoRefreshReferenceConfigurationFile()
    {
        if (_autoRefreshDone || !BossRulesPlugin.IsSourceOfTruth || ZoneSystem.instance == null ||
            Time.realtimeSinceStartup < _nextAutoRefreshAttemptAt)
        {
            return;
        }

        if (ZoneSystem.instance.m_locations == null || ZoneSystem.instance.m_locations.Count == 0)
        {
            return;
        }

        try
        {
            string content = BuildReferenceConfigurationContent(out int entryCount);
            if (entryCount == 0)
            {
                return;
            }

            WriteReferenceConfigurationFile(content);
            _autoRefreshDone = true;
        }
        catch (Exception ex)
        {
            _nextAutoRefreshAttemptAt = Time.realtimeSinceStartup + AutoRefreshRetryDelaySeconds;
            BossRulesPlugin.BossRulesLogger.LogWarning(
                $"Failed to update altar reference configuration at {BossRulesPlugin.AltarReferenceYamlFilePath}. {FormatException(ex)} Retrying in {AutoRefreshRetryDelaySeconds:0.#}s.");
        }
    }

    private static string FormatException(Exception exception)
    {
        Exception root = exception;
        while (root is TargetInvocationException && root.InnerException != null)
        {
            root = root.InnerException;
        }

        if (!ReferenceEquals(root, exception))
        {
            return $"{exception.GetType().Name}: {exception.Message} Inner {root.GetType().Name}: {root.Message}.";
        }

        return $"{exception.GetType().Name}: {exception.Message}.";
    }

    private static string BuildReferenceConfigurationContent(out int entryCount)
    {
        List<AltarReferenceEntry> entries = CaptureReferenceEntries()
            .ToList();
        entryCount = entries.Count;

        StringBuilder builder = new();
        builder.AppendLine("# BossRules altar reference");
        builder.AppendLine("# Generated from loaded ZoneSystem location prefabs.");
        builder.AppendLine("# Copy rows to BossRules.altar.yml to override.");
        builder.AppendLine("# This file is overwritten automatically.");
        builder.Append(SerializeReferenceEntries(entries));
        return builder.ToString();
    }

    private static string SerializeReferenceEntries(IReadOnlyList<AltarReferenceEntry> entries)
    {
        if (entries.Count == 0)
        {
            return "[]" + Environment.NewLine;
        }

        StringBuilder builder = new();
        foreach (AltarReferenceEntry entry in entries.OrderBy(entry => entry.Prefab, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine(SerializeReferenceEntry(entry));
        }

        return builder.ToString();
    }

    private static string SerializeReferenceEntry(AltarReferenceEntry entry)
    {
        string serialized = Serializer.Serialize(new[] { entry }).TrimEnd('\r', '\n');
        if (!RequiresQuotedPrefabScalar(entry.Prefab))
        {
            return serialized;
        }

        int lineEnd = serialized.IndexOfAny(new[] { '\r', '\n' });
        string remaining = lineEnd >= 0 ? serialized.Substring(lineEnd) : "";
        string quotedPrefab = EscapeDoubleQuotedYamlScalar(entry.Prefab);
        return $"- prefab: \"{quotedPrefab}\"{remaining}";
    }

    private static bool RequiresQuotedPrefabScalar(string prefab)
    {
        return (prefab ?? "").IndexOf(':') >= 0;
    }

    private static string EscapeDoubleQuotedYamlScalar(string value)
    {
        return (value ?? "")
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
    }

    private static List<AltarReferenceEntry> CaptureReferenceEntries()
    {
        List<AltarReferenceEntry> entries = new();
        HashSet<string> capturedPrefabs = new(StringComparer.OrdinalIgnoreCase);
        DuplicateComponentWarnings.Clear();

        foreach (ZoneSystem.ZoneLocation location in ZoneSystem.instance.m_locations)
        {
            if (TryCaptureReferenceEntry(location, capturedPrefabs, out AltarReferenceEntry? entry) && entry != null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private static bool TryCaptureReferenceEntry(
        ZoneSystem.ZoneLocation location,
        HashSet<string> capturedPrefabs,
        out AltarReferenceEntry? entry)
    {
        entry = null;
        if (location == null || !location.m_prefab.IsValid)
        {
            return false;
        }

        string prefabName = AltarLocationResolver.GetZoneLocationPrefabName(location);
        if (prefabName.Length == 0 || !capturedPrefabs.Add(prefabName))
        {
            return false;
        }

        location.m_prefab.Load();
        GameObject? rootPrefab = location.m_prefab.Asset;
        if (rootPrefab == null)
        {
            return false;
        }

        OfferingBowl[] offeringBowls = rootPrefab.GetComponentsInChildren<OfferingBowl>(true);
        ItemStand[] itemStands = rootPrefab.GetComponentsInChildren<ItemStand>(true);
        if (offeringBowls.Length == 0 && itemStands.Length == 0)
        {
            return false;
        }

        WarnDuplicateComponent(prefabName, "OfferingBowl", offeringBowls.Length);
        entry = new AltarReferenceEntry
        {
            Prefab = prefabName,
            OfferingBowl = offeringBowls.Length > 0 ? ConvertReferenceOfferingBowl(AltarRuntime.CaptureOfferingBowlSnapshot(offeringBowls[0])) : null,
            ItemStands = itemStands.Length > 0
                ? itemStands
                    .Where(itemStand => itemStand != null)
                    .Select(itemStand => ConvertReferenceItemStand(rootPrefab.transform, itemStand))
                    .OrderBy(itemStand => itemStand.Path, StringComparer.Ordinal)
                    .ToList()
                : null
        };

        return entry.OfferingBowl != null || entry.ItemStands is { Count: > 0 };
    }

    private static AltarOfferingBowlDefinition ConvertReferenceOfferingBowl(OfferingBowlSnapshot snapshot)
    {
        return new AltarOfferingBowlDefinition
        {
            BossItem = snapshot.BossItem.Length == 0 ? null : snapshot.BossItem,
            BossItems = snapshot.BossItems == 1 ? null : snapshot.BossItems,
            BossPrefab = snapshot.BossPrefab.Length == 0 ? null : snapshot.BossPrefab,
            ItemPrefab = snapshot.ItemPrefab.Length == 0 ? null : snapshot.ItemPrefab,
            SetGlobalKey = string.IsNullOrWhiteSpace(snapshot.SetGlobalKey) ? null : snapshot.SetGlobalKey,
            RenderSpawnAreaGizmos = snapshot.RenderSpawnAreaGizmos ? true : null,
            AlertOnSpawn = snapshot.AlertOnSpawn ? true : null,
            SpawnBossDelay = IsReferenceDefault(snapshot.SpawnBossDelay, 5f) ? null : snapshot.SpawnBossDelay,
            SpawnBossDistance = RangeFormatting.FromReference(snapshot.SpawnBossMinDistance, snapshot.SpawnBossMaxDistance, 0f, 40f),
            SpawnBossMaxYDistance = IsReferenceDefault(snapshot.SpawnBossMaxYDistance, 9999f) ? null : snapshot.SpawnBossMaxYDistance,
            GetSolidHeightMargin = snapshot.GetSolidHeightMargin == 1000 ? null : snapshot.GetSolidHeightMargin,
            EnableSolidHeightCheck = snapshot.EnableSolidHeightCheck ? null : false,
            SpawnPointClearingRadius = IsReferenceDefault(snapshot.SpawnPointClearingRadius, 0f) ? null : snapshot.SpawnPointClearingRadius,
            SpawnYOffset = IsReferenceDefault(snapshot.SpawnYOffset, 1f) ? null : snapshot.SpawnYOffset,
            UseItemStands = snapshot.UseItemStands ? true : null,
            ItemStandPrefix = string.IsNullOrWhiteSpace(snapshot.ItemStandPrefix) ? null : snapshot.ItemStandPrefix,
            ItemStandMaxRange = IsReferenceDefault(snapshot.ItemStandMaxRange, 20f) ? null : snapshot.ItemStandMaxRange,
            RespawnMinutes = null
        };
    }

    private static AltarReferenceItemStandDefinition ConvertReferenceItemStand(Transform root, ItemStand itemStand)
    {
        ItemStandSnapshot snapshot = AltarRuntime.CaptureItemStandSnapshot(itemStand);
        return new AltarReferenceItemStandDefinition
        {
            Path = AltarRuntime.GetRelativePath(root, itemStand.transform),
            CanBeRemoved = snapshot.CanBeRemoved ? null : false,
            AutoAttach = snapshot.AutoAttach ? true : null,
            OrientationType = string.IsNullOrWhiteSpace(snapshot.OrientationType) || snapshot.OrientationType == ItemStand.Orientation.Vertical.ToString() ? null : snapshot.OrientationType,
            SupportedTypes = snapshot.SupportedTypes.Count == 0 ? null : new FlowStringListDefinition(snapshot.SupportedTypes),
            SupportedItems = snapshot.SupportedItems.Count == 0 ? null : new FlowStringListDefinition(snapshot.SupportedItems),
            UnsupportedItems = snapshot.UnsupportedItems.Count == 0 ? null : new FlowStringListDefinition(snapshot.UnsupportedItems),
            PowerActivationDelay = IsReferenceDefault(snapshot.PowerActivationDelay, 2f) ? null : snapshot.PowerActivationDelay,
            GuardianPower = string.IsNullOrWhiteSpace(snapshot.GuardianPower) ? null : snapshot.GuardianPower
        };
    }

    private static bool IsReferenceDefault(float actual, float expected)
    {
        return Math.Abs(actual - expected) < 0.0001f;
    }

    private static void WarnDuplicateComponent(string prefabName, string componentName, int count)
    {
        if (count <= 1)
        {
            return;
        }

        string key = $"{prefabName}@{componentName}";
        if (DuplicateComponentWarnings.Add(key))
        {
            BossRulesPlugin.BossRulesLogger.LogWarning(
                $"Location prefab '{prefabName}' has multiple {componentName} components. The first one will be used for BossRules.altar.yml.");
        }
    }

    private static void WriteReferenceConfigurationFile(string content)
    {
        string path = BossRulesPlugin.AltarReferenceYamlFilePath;
        string existing = File.Exists(path) ? File.ReadAllText(path) : "";
        if (string.Equals(existing, content, StringComparison.Ordinal))
        {
            return;
        }

        File.WriteAllText(path, content, Encoding.UTF8);
        BossRulesPlugin.BossRulesLogger.LogInfo($"Updated altar reference configuration at {path}.");
    }
}
