using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BossRules;

internal sealed class AltarConfigurationEntry
{
    [YamlMember(Order = 1)]
    public string Prefab { get; set; } = "";

    [YamlMember(Order = 2)]
    public bool Enabled { get; set; } = true;

    [YamlMember(Order = 3)]
    public AltarOfferingBowlDefinition? OfferingBowl { get; set; }

    [YamlMember(Order = 4)]
    public List<AltarItemStandDefinition>? ItemStands { get; set; }
}

internal sealed class AltarOfferingBowlDefinition
{
    [YamlMember(Order = 1)]
    public string? BossItem { get; set; }

    [YamlMember(Order = 2)]
    public int? BossItems { get; set; }

    [YamlMember(Order = 3)]
    public string? BossPrefab { get; set; }

    [YamlMember(Order = 4)]
    public string? ItemPrefab { get; set; }

    [YamlMember(Order = 5)]
    public string? SetGlobalKey { get; set; }

    [YamlMember(Order = 6)]
    public bool? RenderSpawnAreaGizmos { get; set; }

    [YamlMember(Order = 7)]
    public bool? AlertOnSpawn { get; set; }

    [YamlMember(Order = 8)]
    public float? SpawnBossDelay { get; set; }

    [YamlMember(Order = 9)]
    public FloatRangeDefinition? SpawnBossDistance { get; set; }

    [YamlMember(Order = 10)]
    public float? SpawnBossMaxYDistance { get; set; }

    [YamlMember(Order = 11)]
    public int? GetSolidHeightMargin { get; set; }

    [YamlMember(Order = 12)]
    public bool? EnableSolidHeightCheck { get; set; }

    [YamlMember(Order = 13)]
    public float? SpawnPointClearingRadius { get; set; }

    [YamlMember(Order = 14)]
    public float? SpawnYOffset { get; set; }

    [YamlMember(Order = 15)]
    public bool? UseItemStands { get; set; }

    [YamlMember(Order = 16)]
    public string? ItemStandPrefix { get; set; }

    [YamlMember(Order = 17)]
    public float? ItemStandMaxRange { get; set; }

    [YamlMember(Order = 18)]
    public float? RespawnMinutes { get; set; }
}

internal sealed class AltarItemStandDefinition
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
    public List<string>? SupportedTypes { get; set; }

    [YamlMember(Order = 6)]
    public List<string>? SupportedItems { get; set; }

    [YamlMember(Order = 7)]
    public List<string>? UnsupportedItems { get; set; }

    [YamlMember(Order = 8)]
    public float? PowerActivationDelay { get; set; }

    [YamlMember(Order = 9)]
    public string? GuardianPower { get; set; }
}

internal static class AltarConfiguration
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    internal static bool TryParse(
        string yaml,
        string source,
        out IReadOnlyList<AltarConfigurationEntry> entries)
    {
        entries = Array.Empty<AltarConfigurationEntry>();
        try
        {
            List<AltarConfigurationEntry>? parsed = string.IsNullOrWhiteSpace(yaml)
                ? new List<AltarConfigurationEntry>()
                : Deserializer.Deserialize<List<AltarConfigurationEntry>>(yaml);

            entries = Normalize(parsed ?? new List<AltarConfigurationEntry>());
            BossRulesPlugin.BossRulesLogger.LogInfo($"Loaded altar YAML from {source}: {entries.Count} entries.");
            return true;
        }
        catch (Exception ex)
        {
            BossRulesPlugin.BossRulesLogger.LogError(
                $"Rejected altar YAML from {source}. Keeping the previous configuration. {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static IReadOnlyList<AltarConfigurationEntry> Normalize(List<AltarConfigurationEntry> entries)
    {
        foreach (AltarConfigurationEntry entry in entries)
        {
            entry.Prefab = (entry.Prefab ?? "").Trim();
            NormalizeOfferingBowl(entry.OfferingBowl);
            NormalizeItemStands(entry.ItemStands);
        }

        return entries
            .Where(entry => entry.Prefab.Length > 0)
            .ToList();
    }

    private static void NormalizeOfferingBowl(AltarOfferingBowlDefinition? definition)
    {
        if (definition == null)
        {
            return;
        }

        definition.BossItem = NormalizeOptionalString(definition.BossItem);
        definition.BossPrefab = NormalizeOptionalString(definition.BossPrefab);
        definition.ItemPrefab = NormalizeOptionalString(definition.ItemPrefab);
        definition.SetGlobalKey = NormalizeOptionalString(definition.SetGlobalKey);
        definition.ItemStandPrefix = NormalizeOptionalString(definition.ItemStandPrefix);
    }

    private static void NormalizeItemStands(List<AltarItemStandDefinition>? definitions)
    {
        if (definitions == null)
        {
            return;
        }

        foreach (AltarItemStandDefinition definition in definitions)
        {
            definition.Path = NormalizeOptionalString(definition.Path);
            definition.OrientationType = NormalizeOptionalString(definition.OrientationType);
            definition.SupportedTypes = NormalizeStringList(definition.SupportedTypes);
            definition.SupportedItems = NormalizeStringList(definition.SupportedItems);
            definition.UnsupportedItems = NormalizeStringList(definition.UnsupportedItems);
            definition.GuardianPower = NormalizeOptionalString(definition.GuardianPower);
        }
    }

    private static List<string>? NormalizeStringList(List<string>? values)
    {
        List<string>? normalized = values?
            .Select(value => (value ?? "").Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return normalized is { Count: > 0 } ? normalized : null;
    }

    private static string? NormalizeOptionalString(string? value)
    {
        string trimmed = (value ?? "").Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}

internal static class AltarConfigurationFiles
{
    internal static void EnsureDefaultFiles()
    {
        EnsureTextFile(BossRulesPlugin.AltarYamlFilePath, BuildDefaultAltarYaml());
        EnsureTextFile(BossRulesPlugin.AltarReferenceYamlFilePath, BuildReferencePlaceholderYaml());
    }

    private static void EnsureTextFile(string path, string content)
    {
        if (File.Exists(path))
        {
            return;
        }

        File.WriteAllText(path, content);
        BossRulesPlugin.BossRulesLogger.LogInfo($"Created {path}.");
    }

    private static string BuildDefaultAltarYaml()
    {
        StringBuilder builder = new();
        builder.AppendLine("# BossRules altar overrides");
        builder.AppendLine("#");
        builder.AppendLine("# This file owns boss altar OfferingBowl and boss ItemStand edits.");
        builder.AppendLine("# It is intentionally inert by default.");
        builder.AppendLine("# Copy rows from BossRules.altar.reference.yml and uncomment only the fields you want to override.");
        builder.AppendLine("# Unless noted otherwise, null/empty/omitted override fields keep the current prefab value.");
        builder.AppendLine("#");
        builder.AppendLine("# offeringBowl");
        builder.AppendLine("#");
        builder.AppendLine("# - prefab: Bonemass");
        builder.AppendLine("#   enabled: true # Default true when omitted.");
        builder.AppendLine("#   offeringBowl:");
        builder.AppendLine("#     bossItem: null # ex) WitheredBone. Required direct offering item prefab.");
        builder.AppendLine("#     bossItems: null # ex) 10. Number of bossItem items required; clamped to at least 1 when set.");
        builder.AppendLine("#     bossPrefab: null # ex) Bonemass. Boss character prefab spawned after a valid offering.");
        builder.AppendLine("#     itemPrefab: null # ex) Wishbone. Optional item reward prefab instead of spawning a boss.");
        builder.AppendLine("#     setGlobalKey: null # ex) defeated_bonemass. Optional global key set after a valid offering.");
        builder.AppendLine("#     renderSpawnAreaGizmos: null # ex) false. True draws the boss spawn search area while selected.");
        builder.AppendLine("#     alertOnSpawn: null # ex) false. True calls BaseAI.Alert() on the spawned boss.");
        builder.AppendLine("#     spawnBossDelay: null # ex) 5. Seconds to wait before spawning; clamped to at least 0.");
        builder.AppendLine("#     spawnBossDistance: null # ex) 0~40 or {min: 0, max: 40}. Each side can be overridden separately.");
        builder.AppendLine("#     spawnBossMaxYDistance: null # ex) 9999. Vertical spawn search distance; clamped to at least 0.");
        builder.AppendLine("#     getSolidHeightMargin: null # ex) 1000. Terrain raycast margin; clamped to at least 0.");
        builder.AppendLine("#     enableSolidHeightCheck: null # ex) true. True requires valid ground height.");
        builder.AppendLine("#     spawnPointClearingRadius: null # ex) 0. Clearing radius before boss spawn; clamped to at least 0.");
        builder.AppendLine("#     spawnYOffset: null # ex) 1. Vertical offset added to the chosen spawn position.");
        builder.AppendLine("#     useItemStands: null # ex) true. True uses nearby ItemStands instead of direct UseItem offerings.");
        builder.AppendLine("#     itemStandPrefix: null # ex) Boss. Object-name prefix used to select nearby ItemStands.");
        builder.AppendLine("#     itemStandMaxRange: null # ex) 20. Max scan distance for nearby ItemStands; clamped to at least 0.");
        builder.AppendLine("#     respawnMinutes: null # Null/omitted becomes 0, disabling BossRules altar cooldown. Set >0 for cooldown minutes.");
        builder.AppendLine("#");
        builder.AppendLine("# itemStands");
        builder.AppendLine("#");
        builder.AppendLine("# - prefab: StartTemple");
        builder.AppendLine("#   enabled: true # Default true when omitted.");
        builder.AppendLine("#   itemStands:");
        builder.AppendLine("#   - path: null # Null/empty/omitted targets all relevant stands. ex) BossStone_Eikthyr[0]/itemstand[0] targets one reference path.");
        builder.AppendLine("#     canBeRemoved: null # ex) true. True allows players to remove the attached item.");
        builder.AppendLine("#     autoAttach: null # ex) false. True automatically attaches compatible dropped items.");
        builder.AppendLine("#     orientationType: null # ex) Vertical. ItemStand.Orientation name.");
        builder.AppendLine("#     supportedTypes: [] # ex) [OneHandedWeapon, TwoHandedWeapon]. ItemDrop.ItemType names.");
        builder.AppendLine("#     supportedItems: [] # ex) [TrophyDeer]. Explicitly allowed item prefabs.");
        builder.AppendLine("#     unsupportedItems: [] # ex) [TrophyDeer]. Explicitly blocked item prefabs.");
        builder.AppendLine("#     powerActivationDelay: null # ex) 2. Seconds before guardianPower activates; clamped to at least 0.");
        builder.AppendLine("#     guardianPower: null # ex) GP_Eikthyr. StatusEffect prefab granted when used.");
        return builder.ToString();
    }

    private static string BuildReferencePlaceholderYaml()
    {
        StringBuilder builder = new();
        builder.AppendLine("# BossRules altar reference");
        builder.AppendLine("#");
        builder.AppendLine("# This file is generated automatically after ZoneSystem location prefabs load.");
        builder.AppendLine("[]");
        return builder.ToString();
    }
}
