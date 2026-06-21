using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BossRules;

internal sealed class BossRuleConfigurationSection
{
    [YamlMember(Order = 1)]
    public List<string>? Despawn { get; set; }

    [YamlMember(Order = 2)]
    public BossTamedPressureDefinition? BossTamedPressure { get; set; }

    [YamlMember(Order = 3)]
    public ForsakenPowersDefinition? ForsakenPowers { get; set; }

    [YamlMember(Order = 4)]
    public BossRuleLocalizationDefinition? Localization { get; set; }
}

internal sealed class BossRuleLocalizationDefinition
{
    [YamlMember(Order = 1)]
    public string? MessageDespawnStart { get; set; }

    [YamlMember(Order = 2)]
    public string? MessageDespawnReminder { get; set; }

    [YamlMember(Order = 3)]
    public string? MessageDespawnCanceled { get; set; }

    [YamlMember(Order = 4)]
    public string? MessageBossTamedPressure { get; set; }

    [YamlMember(Order = 5)]
    public string? MessageForsakenPowerRotate { get; set; }
}

internal sealed class BossDespawnDefinition
{
    internal BossDespawnDefinition(string prefab, float? despawnRange, float? despawnDelay, bool? refunds)
    {
        Prefab = prefab;
        DespawnRange = despawnRange;
        DespawnDelay = despawnDelay;
        Refunds = refunds;
    }

    internal string Prefab { get; }

    public float? DespawnRange { get; set; }

    public float? DespawnDelay { get; set; }

    public bool? Refunds { get; set; }
}

internal sealed class BossRuleConfigurationState
{
    internal static BossRuleConfigurationState Empty => new();

    internal List<BossDespawnDefinition> DespawnRules { get; } = new();
    internal List<BossTamedPressureDefinition> BossTamedPressureRules { get; } = new();
    internal ForsakenPowersDefinition? ForsakenPowers { get; set; }
    internal string? MessageDespawnStart { get; set; }
    internal string? MessageDespawnReminder { get; set; }
    internal string? MessageDespawnCanceled { get; set; }
    internal string? MessageBossTamedPressure { get; set; }
    internal string? MessageForsakenPowerRotate { get; set; }
}

internal sealed class BossTamedPressureDefinition
{
    [YamlMember(Order = 1)]
    public List<string>? BossPrefabs { get; set; }

    [YamlMember(Order = 2)]
    public List<string>? ExcludedBossPrefabs { get; set; }

    [YamlMember(Order = 3)]
    public BossTamedPressureTargetsDefinition? Targets { get; set; }

    [YamlMember(Order = 4)]
    public BossTamedPressurePressureDefinition? Pressure { get; set; }
}

internal sealed class BossTamedPressureTargetsDefinition
{
    [YamlMember(Order = 1)]
    public float? Range { get; set; }

    [YamlMember(Order = 2)]
    public int? MaxPerBoss { get; set; }

    [YamlMember(Order = 3)]
    public List<string>? ExcludedTamedPrefabs { get; set; }

    [YamlMember(Order = 4)]
    public List<string>? ExtraPressuredPrefabs { get; set; }
}

internal sealed class BossTamedPressurePressureDefinition
{
    [YamlMember(Order = 1)]
    public float? DamagePercentPerSecond { get; set; }

    [YamlMember(Order = 2)]
    public float? IncomingDamageMultiplier { get; set; }

    [YamlMember(Order = 3)]
    public float? OutgoingDamageMultiplier { get; set; }
}

internal static class BossRuleConfiguration
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    internal static bool TryParse(
        string yaml,
        string source,
        out BossRuleConfigurationState state)
    {
        state = BossRuleConfigurationState.Empty;
        try
        {
            BossRuleConfigurationSection? parsed = string.IsNullOrWhiteSpace(yaml)
                ? new BossRuleConfigurationSection()
                : Deserializer.Deserialize<BossRuleConfigurationSection>(yaml);

            state = Normalize(parsed ?? new BossRuleConfigurationSection());
            BossRulesPlugin.BossRulesLogger.LogInfo(
                $"Loaded boss rules YAML from {source}: {state.DespawnRules.Count} despawn entries, {state.BossTamedPressureRules.Count} boss tamed pressure entries, {state.ForsakenPowers?.Powers?.Count ?? 0} forsaken power entries.");
            return true;
        }
        catch (Exception ex)
        {
            BossRulesPlugin.BossRulesLogger.LogError(
                $"Rejected boss rules YAML from {source}. Keeping the previous configuration. {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static BossRuleConfigurationState Normalize(BossRuleConfigurationSection section)
    {
        BossRuleConfigurationState state = new();
        foreach (string rawDespawnRule in section.Despawn ?? new List<string>())
        {
            state.DespawnRules.Add(ParseDespawnRule(rawDespawnRule));
        }

        if (section.BossTamedPressure != null)
        {
            NormalizeBossTamedPressure(section.BossTamedPressure);
            state.BossTamedPressureRules.Add(section.BossTamedPressure);
        }

        if (section.ForsakenPowers != null)
        {
            NormalizeForsakenPowers(section.ForsakenPowers);
            state.ForsakenPowers = section.ForsakenPowers;
        }

        BossRuleLocalizationDefinition? localization = section.Localization;
        if (localization?.MessageDespawnStart != null)
        {
            state.MessageDespawnStart = localization.MessageDespawnStart.Trim();
        }

        if (localization?.MessageDespawnReminder != null)
        {
            state.MessageDespawnReminder = localization.MessageDespawnReminder.Trim();
        }

        if (localization?.MessageDespawnCanceled != null)
        {
            state.MessageDespawnCanceled = localization.MessageDespawnCanceled.Trim();
        }

        if (localization?.MessageBossTamedPressure != null)
        {
            state.MessageBossTamedPressure = localization.MessageBossTamedPressure.Trim();
        }

        if (localization?.MessageForsakenPowerRotate != null)
        {
            state.MessageForsakenPowerRotate = localization.MessageForsakenPowerRotate.Trim();
        }

        return state;
    }

    private static BossDespawnDefinition ParseDespawnRule(string rawRule)
    {
        string raw = (rawRule ?? "").Trim();
        if (raw.Length == 0)
        {
            throw new FormatException("Empty despawn row. Expected '- prefab, despawnRange, despawnDelay, refunds'.");
        }

        string[] parts = raw.Split(',');
        if (parts.Length > 4)
        {
            throw new FormatException($"Despawn row '{raw}' has too many values. Expected '- prefab, despawnRange, despawnDelay, refunds'.");
        }

        string prefab = parts[0].Trim();
        if (prefab.Length == 0)
        {
            throw new FormatException($"Despawn row '{raw}' has an empty prefab name.");
        }

        return new BossDespawnDefinition(
            prefab,
            ParseOptionalFloat(parts, 1, "despawnRange", raw),
            ParseOptionalFloat(parts, 2, "despawnDelay", raw),
            ParseOptionalBool(parts, 3, "refunds", raw));
    }

    private static float? ParseOptionalFloat(string[] parts, int index, string fieldName, string rawRule)
    {
        if (parts.Length <= index)
        {
            return null;
        }

        string rawValue = parts[index].Trim();
        if (rawValue.Length == 0)
        {
            return null;
        }

        if (float.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float value))
        {
            return value;
        }

        throw new FormatException($"Despawn row '{rawRule}' has invalid {fieldName} value '{rawValue}'.");
    }

    private static bool? ParseOptionalBool(string[] parts, int index, string fieldName, string rawRule)
    {
        if (parts.Length <= index)
        {
            return null;
        }

        string rawValue = parts[index].Trim();
        if (rawValue.Length == 0)
        {
            return null;
        }

        if (bool.TryParse(rawValue, out bool value))
        {
            return value;
        }

        throw new FormatException($"Despawn row '{rawRule}' has invalid {fieldName} value '{rawValue}'. Use true or false.");
    }

    private static void NormalizeBossTamedPressure(BossTamedPressureDefinition? definition)
    {
        if (definition == null)
        {
            return;
        }

        definition.BossPrefabs = NormalizeStringList(definition.BossPrefabs);
        definition.ExcludedBossPrefabs = NormalizeStringList(definition.ExcludedBossPrefabs);
        if (definition.Targets != null)
        {
            definition.Targets.ExcludedTamedPrefabs = NormalizeStringList(definition.Targets.ExcludedTamedPrefabs);
            definition.Targets.ExtraPressuredPrefabs = NormalizeStringList(definition.Targets.ExtraPressuredPrefabs);
        }
    }

    private static void NormalizeForsakenPowers(ForsakenPowersDefinition? definition)
    {
        if (definition == null)
        {
            return;
        }

        definition.Mode = NormalizeOptionalString(definition.Mode);
        if (definition.Powers == null)
        {
            return;
        }

        foreach (ForsakenPowerDefinition power in definition.Powers)
        {
            power.Prefab = NormalizeOptionalString(power.Prefab);
        }

        definition.Powers = definition.Powers
            .Where(power => !string.IsNullOrWhiteSpace(power.Prefab))
            .ToList();
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
        string normalized = (value ?? "").Trim();
        return normalized.Length > 0 ? normalized : null;
    }

}

internal static class BossRuleConfigurationFiles
{
    internal static void EnsureDefaultFile()
    {
        EnsureTextFile(BossRulesPlugin.RulesYamlFilePath, BuildDefaultRulesYaml());
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

    private static string BuildDefaultRulesYaml()
    {
        StringBuilder builder = new();
        builder.AppendLine("# BossRules runtime rules");
        builder.AppendLine("#");
        builder.AppendLine("despawn:");
        builder.AppendLine("  # - prefab, despawnRange, despawnDelay, refunds");
        builder.AppendLine("  #   Empty or omitted despawnRange/despawnDelay uses the BepInEx config defaults.");
        builder.AppendLine("  #   despawnRange: 0 disables despawn for that prefab.");
        builder.AppendLine("  #   refunds omitted or empty: true. Use false to disable altar offering refunds.");
        builder.AppendLine("  - Fader, 64, 90, true # Boss prefabs are auto-detected, but non-boss Character prefabs can also be listed here for despawn rules.");
        builder.AppendLine();
        builder.AppendLine("bossTamedPressure:");
        builder.AppendLine("  bossPrefabs: [Eikthyr] # Extra source boss prefabs added to the auto-detected boss set");
        builder.AppendLine("  excludedBossPrefabs: [] # Boss prefabs to ignore from auto-detected and bossPrefabs sources");
        builder.AppendLine("  targets:");
        builder.AppendLine("    range: 32 # Clamp: 0~128. Horizontal XZ range around each boss");
        builder.AppendLine("    maxPerBoss: 4 # Clamp: 1~128. Maximum pressured targets per boss per scan");
        builder.AppendLine("    excludedTamedPrefabs: [] # Tamed MonsterAI prefabs excluded from the default pressured target set");
        builder.AppendLine("    extraPressuredPrefabs: [] # Character prefabs pressured even when not tamed");
        builder.AppendLine("  pressure:");
        builder.AppendLine("    damagePercentPerSecond: 0.01 # Clamp: 0~1. 0.01 = 1% of max health per second");
        builder.AppendLine("    incomingDamageMultiplier: 1.25 # Clamp: 0~10. Multiplies damage received while affected");
        builder.AppendLine("    outgoingDamageMultiplier: 0.75 # Clamp: 0~10. Multiplies damage dealt while affected");
        builder.AppendLine();
        builder.AppendLine("# movement keys: speedPercent, jumpHeightPercent");
        builder.AppendLine("# staminaCostPercent keys: run, jump, sneak, dodge, swim, block, attack");
        builder.AppendLine("# incomingDamageModifiers keys: Blunt, Slash, Pierce, Chop, Pickaxe, Fire, Frost, Lightning, Poison, Spirit");
        builder.AppendLine("# incomingDamageModifiers values: Normal, SlightlyResistant, Resistant, VeryResistant, Immune, SlightlyWeak, Weak, VeryWeak, Ignore");
        builder.AppendLine("# outgoingDamagePercent keys: Blunt, Slash, Pierce, Chop, Pickaxe, Fire, Frost, Lightning, Poison, Spirit");
        builder.AppendLine("# regenPercent keys: health, stamina, eitr");
        builder.AppendLine("# skillLevels keys: Swords, Knives, Clubs, Polearms, Spears, Blocking, Axes, Bows, FireMagic, FrostMagic, Unarmed, Pickaxes, WoodCutting, Jump, Sneak, Run, Swim, Fishing, BloodMagic, ElementalMagic, Crossbows, Cooking, Farming, Crafting, Sailing, Dodge.");
        builder.AppendLine("# Vanilla SE_Stats applies the first 2 entries.");
        builder.AppendLine("forsakenPowers:");
        builder.AppendLine("  mode: replace # replace clears supported vanilla fields before applying; patch keeps omitted vanilla fields.");
        builder.AppendLine("  defaults:");
        builder.AppendLine("    durationSeconds: 18 # Applies to every listed power unless overridden on that power.");
        builder.AppendLine("    cooldownSeconds: 60 # Applies to every listed power unless overridden on that power.");
        builder.AppendLine("    adrenalineGain: 0 # Guardian power activation adrenaline gain for every power; omit to keep vanilla 10. Negative values clamp to 0.");
        builder.AppendLine("  powers:");
        builder.AppendLine("  - prefab: GP_Eikthyr");
        builder.AppendLine("    staminaCostPercent: # Percent values: -50 halves the cost.");
        builder.AppendLine("      run: -50");
        builder.AppendLine("      jump: -50");
        builder.AppendLine("      sneak: -50");
        builder.AppendLine("      dodge: -50");
        builder.AppendLine("    incomingDamageModifiers: # DamageModifier names: SlightlyResistant is vanilla -25% damage.");
        builder.AppendLine("      Blunt: SlightlyResistant");
        builder.AppendLine("  - prefab: GP_TheElder");
        builder.AppendLine("    outgoingDamagePercent:");
        builder.AppendLine("      Chop: 50");
        builder.AppendLine("      Pickaxe: 50");
        builder.AppendLine("    staminaCostPercent:");
        builder.AppendLine("      swim: -50");
        builder.AppendLine("    regenPercent:");
        builder.AppendLine("      health: 100");
        builder.AppendLine("    incomingDamageModifiers:");
        builder.AppendLine("      Poison: SlightlyResistant");
        builder.AppendLine("  - prefab: GP_Bonemass");
        builder.AppendLine("    staminaCostPercent:");
        builder.AppendLine("      block: -50");
        builder.AppendLine("    blockStaminaReturn: 5 # Vanilla Bonemass-style flat block stamina return.");
        builder.AppendLine("    carryWeight: 300");
        builder.AppendLine("    armor:");
        builder.AppendLine("      flat: 10");
        builder.AppendLine("      percent: 10");
        builder.AppendLine("    incomingDamageModifiers:");
        builder.AppendLine("      Frost: SlightlyResistant");
        builder.AppendLine("  - prefab: GP_Moder");
        builder.AppendLine("    tailwind: true");
        builder.AppendLine("    movement:");
        builder.AppendLine("      speedPercent: 10");
        builder.AppendLine("      jumpHeightPercent: 20");
        builder.AppendLine("    skillLevels:");
        builder.AppendLine("      Farming: 25");
        builder.AppendLine("    incomingDamageModifiers:");
        builder.AppendLine("      Fire: SlightlyResistant");
        builder.AppendLine("  - prefab: GP_Yagluth");
        builder.AppendLine("    outgoingDamagePercent:");
        builder.AppendLine("      Fire: 10");
        builder.AppendLine("      Poison: 10");
        builder.AppendLine("      Frost: 10");
        builder.AppendLine("      Lightning: 10");
        builder.AppendLine("      Spirit: 10");
        builder.AppendLine("    regenPercent:");
        builder.AppendLine("      eitr: 100");
        builder.AppendLine("    incomingDamageModifiers:");
        builder.AppendLine("      Pierce: SlightlyResistant");
        builder.AppendLine("  - prefab: GP_Queen");
        builder.AppendLine("    outgoingDamagePercent:");
        builder.AppendLine("      Pierce: 10");
        builder.AppendLine("      Blunt: 10");
        builder.AppendLine("      Slash: 10");
        builder.AppendLine("    staminaCostPercent:");
        builder.AppendLine("      attack: -10");
        builder.AppendLine("    incomingDamageModifiers:");
        builder.AppendLine("      Slash: SlightlyResistant");
        builder.AppendLine("  - prefab: GP_Fader");
        builder.AppendLine("    adrenalinePercent: 100");
        builder.AppendLine("    staggerGaugePercent: -50");
        builder.AppendLine("    incomingDamageModifiers:");
        builder.AppendLine("      Lightning: SlightlyResistant");
        builder.AppendLine();
        builder.AppendLine("localization:");
        builder.AppendLine("  messageDespawnStart: \"{name} will despawn in {seconds}s unless someone returns.\"");
        builder.AppendLine("  messageDespawnReminder: \"{name} will despawn in {seconds}s.\"");
        builder.AppendLine("  messageDespawnCanceled: \"{name} despawn canceled.\"");
        builder.AppendLine("  messageBossTamedPressure: \"Tamed creatures near a boss are weakened.\"");
        builder.AppendLine("  messageForsakenPowerRotate: \"Rotate\"");
        return builder.ToString();
    }
}
