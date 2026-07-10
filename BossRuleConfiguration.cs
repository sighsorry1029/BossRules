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
    public BossDespawnConfigurationDefinition? Despawn { get; set; }

    [YamlMember(Order = 2)]
    public BossTamedPressureDefinition? BossTamedPressure { get; set; }

    [YamlMember(Order = 3)]
    public BossRuleLocalizationDefinition? Localization { get; set; }
}

internal sealed class BossDespawnConfigurationDefinition
{
    [YamlMember(Order = 1)]
    public string? Defaults { get; set; }

    [YamlMember(Order = 2)]
    public List<string>? Rules { get; set; }
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

    internal float DefaultDespawnRange { get; set; } = 64f;
    internal float DefaultDespawnDelaySeconds { get; set; } = 90f;
    internal List<BossDespawnDefinition> DespawnRules { get; } = new();
    internal List<BossTamedPressureDefinition> BossTamedPressureRules { get; } = new();
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
                $"Loaded boss rules YAML from {source}: {state.DespawnRules.Count} despawn entries, {state.BossTamedPressureRules.Count} boss tamed pressure entries.");
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
        (state.DefaultDespawnRange, state.DefaultDespawnDelaySeconds) = ParseDespawnDefaults(section.Despawn?.Defaults);
        foreach (string rawDespawnRule in section.Despawn?.Rules ?? new List<string>())
        {
            state.DespawnRules.Add(ParseDespawnRule(rawDespawnRule));
        }

        if (section.BossTamedPressure != null)
        {
            NormalizeBossTamedPressure(section.BossTamedPressure);
            state.BossTamedPressureRules.Add(section.BossTamedPressure);
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

    private static (float Range, float DelaySeconds) ParseDespawnDefaults(string? rawDefaults)
    {
        if (string.IsNullOrWhiteSpace(rawDefaults))
        {
            return (64f, 90f);
        }

        string raw = rawDefaults!.Trim();
        string[] parts = raw.Split(',');
        if (parts.Length > 2)
        {
            throw new FormatException($"despawn.defaults '{raw}' has too many values. Expected 'range, delaySeconds'.");
        }

        float range = ParseDefaultFloat(parts, 0, "range", raw, 64f);
        float delaySeconds = ParseDefaultFloat(parts, 1, "delaySeconds", raw, 90f);
        return (range, delaySeconds);
    }

    private static float ParseDefaultFloat(string[] parts, int index, string fieldName, string rawDefaults, float fallback)
    {
        if (parts.Length <= index)
        {
            return fallback;
        }

        string rawValue = parts[index].Trim();
        if (rawValue.Length == 0)
        {
            return fallback;
        }

        if (float.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float value))
        {
            return value;
        }

        throw new FormatException($"despawn.defaults '{rawDefaults}' has invalid {fieldName} value '{rawValue}'.");
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

    private static List<string>? NormalizeStringList(List<string>? values)
    {
        List<string>? normalized = values?
            .Select(value => (value ?? "").Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return normalized is { Count: > 0 } ? normalized : null;
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
        builder.AppendLine("  defaults: 64, 90 # default despawnRange, despawnDelaySeconds.");
        builder.AppendLine("  rules:");
        builder.AppendLine("  # - prefab, despawnRange, despawnDelay, refunds");
        builder.AppendLine("  #   Empty or omitted despawnRange/despawnDelay uses despawn.defaults.");
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
        builder.AppendLine("localization:");
        builder.AppendLine("  messageDespawnStart: \"{name} will despawn in {seconds}s unless someone returns.\"");
        builder.AppendLine("  messageDespawnReminder: \"{name} will despawn in {seconds}s.\"");
        builder.AppendLine("  messageDespawnCanceled: \"{name} despawn canceled.\"");
        builder.AppendLine("  messageBossTamedPressure: \"Tamed creatures near a boss are weakened.\"");
        builder.AppendLine("  messageForsakenPowerRotate: \"Rotate\"");
        return builder.ToString();
    }
}
