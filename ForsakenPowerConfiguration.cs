using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BossRules;

internal static class ForsakenPowerConfiguration
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    internal static bool TryParse(
        string yaml,
        string source,
        out IReadOnlyList<ForsakenPowerDefinition> entries)
    {
        entries = Array.Empty<ForsakenPowerDefinition>();
        try
        {
            List<ForsakenPowerDefinition>? parsed = string.IsNullOrWhiteSpace(yaml)
                ? new List<ForsakenPowerDefinition>()
                : Deserializer.Deserialize<List<ForsakenPowerDefinition>>(yaml);

            List<ForsakenPowerDefinition> normalized = Normalize(parsed);
            entries = normalized;
            BossRulesPlugin.BossRulesLogger.LogInfo(
                $"Loaded forsaken power YAML from {source}: {normalized.Count} entries.");
            return true;
        }
        catch (Exception ex)
        {
            BossRulesPlugin.BossRulesLogger.LogError(
                $"Rejected forsaken power YAML from {source}. Keeping the previous configuration. {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static List<ForsakenPowerDefinition> Normalize(List<ForsakenPowerDefinition>? entries)
    {
        if (entries == null)
        {
            return new List<ForsakenPowerDefinition>();
        }

        foreach (ForsakenPowerDefinition entry in entries)
        {
            entry.Effect = NormalizeOptionalString(entry.Effect);
            entry.Time = NormalizeOptionalString(entry.Time);
            entry.Attributes = NormalizeOptionalString(entry.Attributes);
        }

        return entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Effect))
            .ToList();
    }

    private static string? NormalizeOptionalString(string? value)
    {
        string normalized = (value ?? "").Trim();
        return normalized.Length > 0 ? normalized : null;
    }
}

internal static class ForsakenPowerConfigurationFiles
{
    internal static void EnsureDefaultFile()
    {
        EnsureTextFile(BossRulesPlugin.ForsakenPowersYamlFilePath, BuildDefaultForsakenPowersYaml());
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

    private static string BuildDefaultForsakenPowersYaml()
    {
        StringBuilder builder = new();
        builder.AppendLine("# BossRules Forsaken Power overrides");
        builder.AppendLine("# Uses the same compact field style as DataForge effects.yml, but only supports the fields listed here.");
        builder.AppendLine("# Rows can be copied directly into DataForge effects.yml. DataForge may support additional status effect fields.");
        builder.AppendLine("# If the same effect exists in DataForge effects.yml, DataForge owns that effect and BossRules skips it.");
        builder.AppendLine("# BossRules resets supported stat fields before applying each effect entry; list every supported effect value you want to keep.");
        builder.AppendLine("#");
        builder.AppendLine("# Schema:");
        builder.AppendLine("# - effect: GP_Eikthyr                  # GP_Eikthyr => override that Forsaken Power StatusEffect.");
        builder.AppendLine("#   time: 18, 60                        # 18, 60 => lasts 18s, then cannot reapply for 60s.");
        builder.AppendLine("#   attributes: None                    # SailingPower => grant that StatusAttribute flag.");
        builder.AppendLine("#   stats:");
        builder.AppendLine("#     regenMultiplier: 1, 1, 1          # 1.5, 0.5, 0 => health regen +50%, stamina regen x0.5, eitr regen disabled.");
        builder.AppendLine("#     staminaDrainPerSec: 0             # 2 => drain 2 stamina per second.");
        builder.AppendLine("#     adrenalineModifier: 0             # 0.25 => adrenaline gain/use value +25%.");
        builder.AppendLine("#     speedModifier: 0                  # 0.2 => movement speed +20%.");
        builder.AppendLine("#     swimSpeedModifier: 0              # 0.3 => swim speed +30%.");
        builder.AppendLine("#     jumpModifier: 0, 0, 0             # 0, 0.25, 0 => jump height +25%; 0.2, 0, 0.2 => jump distance +20%.");
        builder.AppendLine("#     windRun: 0, 0                     # 0.2, -0.25 => tailwind movement speed up to +20%, run stamina drain up to -25%.");
        builder.AppendLine("#     armor: 0, 0                       # 10, 0.25 => (armor + 10) * 1.25.");
        builder.AppendLine("#     block: 0, 0                       # 0.5, -5 => timed block/parry bonus +50%, block stamina cost -5 flat.");
        builder.AppendLine("#     staggerModifier: 0                # -0.25 => stagger taken -25%.");
        builder.AppendLine("#     addMaxCarryWeight: 0              # 100 => carry weight +100.");
        builder.AppendLine("#     Skill values for skillLevel/skillLevel2: None, Swords, Knives, Clubs, Polearms, Spears, Blocking, Axes, Bows, ElementalMagic, BloodMagic, Unarmed, Pickaxes, WoodCutting, Crossbows, Jump, Sneak, Run, Swim, Fishing, Cooking, Farming, Crafting, Dodge, Ride.");
        builder.AppendLine("#     skillLevel: None, 0               # Swords, 15 => treat Swords as +15 levels while active.");
        builder.AppendLine("#     skillLevel2: None, 0              # Blocking, 10 => treat Blocking as +10 levels while active.");
        builder.AppendLine("#   staminaDrainModifier:");
        builder.AppendLine("#     run: 0                            # -0.25 => running stamina drain -25%.");
        builder.AppendLine("#     attack: 0                         # -0.2 => attack stamina cost -20%.");
        builder.AppendLine("#     block: 0                          # -0.2 => block stamina cost -20%.");
        builder.AppendLine("#     dodge: 0                          # -0.2 => dodge stamina cost -20%.");
        builder.AppendLine("#     jump: 0                           # -0.2 => jump stamina cost -20%.");
        builder.AppendLine("#     sneak: 0                          # -0.2 => sneak stamina cost -20%.");
        builder.AppendLine("#     swim: 0                           # -0.2 => swim stamina cost -20%.");
        builder.AppendLine("#     homeItem: 0                       # -0.2 => hammer/build stamina cost -20%.");
        builder.AppendLine("#   damageTakenModifiers:");
        builder.AppendLine("#     blunt: Normal                     # Resistant => take 50% blunt damage; Normal => remove this effect's blunt modifier.");
        builder.AppendLine("#     slash: Normal                     # Weak => take 150% slash damage.");
        builder.AppendLine("#     pierce: Normal                    # VeryResistant => take 25% pierce damage.");
        builder.AppendLine("#     chop: Normal                      # SlightlyWeak => take 125% chop damage.");
        builder.AppendLine("#     pickaxe: Normal                   # Immune => take 0 pickaxe damage.");
        builder.AppendLine("#     fire: Normal                      # Resistant => take 50% fire damage.");
        builder.AppendLine("#     frost: Normal                     # VeryResistant => take 25% frost damage.");
        builder.AppendLine("#     lightning: Normal                 # SlightlyResistant => take 75% lightning damage.");
        builder.AppendLine("#     poison: Normal                    # Weak => take 150% poison damage.");
        builder.AppendLine("#     spirit: Normal                    # Immune => take 0 spirit damage.");
        builder.AppendLine("#   percentageDamageModifiers:");
        builder.AppendLine("#     blunt: 0                          # 0.25 => blunt damage modifier +25%.");
        builder.AppendLine("#     slash: 0                          # 0.25 => slash damage modifier +25%.");
        builder.AppendLine("#     pierce: 0                         # 0.25 => pierce damage modifier +25%.");
        builder.AppendLine("#     chop: 0                           # 0.25 => chop damage modifier +25%.");
        builder.AppendLine("#     pickaxe: 0                        # 0.25 => pickaxe damage modifier +25%.");
        builder.AppendLine("#     fire: 0                           # 0.25 => fire damage modifier +25%.");
        builder.AppendLine("#     frost: 0                          # 0.25 => frost damage modifier +25%.");
        builder.AppendLine("#     lightning: 0                      # 0.25 => lightning damage modifier +25%.");
        builder.AppendLine("#     poison: 0                         # 0.25 => poison damage modifier +25%.");
        builder.AppendLine("#     spirit: 0                         # 0.25 => spirit damage modifier +25%.");
        builder.AppendLine("#");
        builder.AppendLine("- effect: GP_Eikthyr");
        builder.AppendLine("  time: 16, 60");
        builder.AppendLine("  percentageDamageModifiers:");
        builder.AppendLine("    Pickaxe: 0.5");
        builder.AppendLine("  staminaDrainModifier:");
        builder.AppendLine("    run: -0.5");
        builder.AppendLine("    dodge: -0.5");
        builder.AppendLine("  stats:");
        builder.AppendLine("    speedModifier: 0.1");
        builder.AppendLine("  damageTakenModifiers:");
        builder.AppendLine("    Blunt: SlightlyResistant");
        builder.AppendLine("- effect: GP_TheElder");
        builder.AppendLine("  time: 16, 60");
        builder.AppendLine("  percentageDamageModifiers:");
        builder.AppendLine("    Chop: 0.5");
        builder.AppendLine("  staminaDrainModifier:");
        builder.AppendLine("    swim: -0.5");
        builder.AppendLine("    sneak: -0.5");
        builder.AppendLine("  stats:");
        builder.AppendLine("    regenMultiplier: 2, 1, 1");
        builder.AppendLine("  damageTakenModifiers:");
        builder.AppendLine("    Poison: SlightlyResistant");
        builder.AppendLine("- effect: GP_Bonemass");
        builder.AppendLine("  time: 16, 60");
        builder.AppendLine("  staminaDrainModifier:");
        builder.AppendLine("    block: -0.5");
        builder.AppendLine("  stats:");
        builder.AppendLine("    block: 0, -5 # timed block bonus, flat block stamina cost. -5 returns 5 stamina.");
        builder.AppendLine("    armor: 20, 0.2");
        builder.AppendLine("  damageTakenModifiers:");
        builder.AppendLine("    Frost: SlightlyResistant");
        builder.AppendLine("- effect: GP_Moder");
        builder.AppendLine("  time: 16, 60");
        builder.AppendLine("  attributes: SailingPower");
        builder.AppendLine("  staminaDrainModifier:");
        builder.AppendLine("    jump: -0.5");
        builder.AppendLine("  stats:");
        builder.AppendLine("    addMaxCarryWeight: 300");
        builder.AppendLine("    jumpModifier: 0, 0.2, 0");
        builder.AppendLine("    skillLevel: Farming, 25");
        builder.AppendLine("    skillLevel2: Fishing, 25");
        builder.AppendLine("  damageTakenModifiers:");
        builder.AppendLine("    Fire: SlightlyResistant");
        builder.AppendLine("- effect: GP_Yagluth");
        builder.AppendLine("  time: 16, 60");
        builder.AppendLine("  percentageDamageModifiers:");
        builder.AppendLine("    Fire: 0.1");
        builder.AppendLine("    Poison: 0.1");
        builder.AppendLine("    Frost: 0.1");
        builder.AppendLine("    Lightning: 0.1");
        builder.AppendLine("    Spirit: 0.1");
        builder.AppendLine("  stats:");
        builder.AppendLine("    regenMultiplier: 1, 1, 2");
        builder.AppendLine("  damageTakenModifiers:");
        builder.AppendLine("    Pierce: SlightlyResistant");
        builder.AppendLine("- effect: GP_Queen");
        builder.AppendLine("  time: 16, 60");
        builder.AppendLine("  percentageDamageModifiers:");
        builder.AppendLine("    Pierce: 0.1");
        builder.AppendLine("    Blunt: 0.1");
        builder.AppendLine("    Slash: 0.1");
        builder.AppendLine("  staminaDrainModifier:");
        builder.AppendLine("    attack: -0.1");
        builder.AppendLine("  damageTakenModifiers:");
        builder.AppendLine("    Slash: SlightlyResistant");
        builder.AppendLine("- effect: GP_Fader");
        builder.AppendLine("  time: 16, 60");
        builder.AppendLine("  stats:");
        builder.AppendLine("    adrenalineModifier: 1");
        builder.AppendLine("    staggerModifier: -0.5");
        builder.AppendLine("  damageTakenModifiers:");
        builder.AppendLine("    Lightning: SlightlyResistant");
        return builder.ToString();
    }
}

internal sealed class ForsakenPowerDefinition
{
    [YamlMember(Order = 1)]
    public string? Effect { get; set; }

    [YamlMember(Order = 2)]
    public string? Time { get; set; }

    [YamlMember(Order = 3)]
    public string? Attributes { get; set; }

    [YamlMember(Order = 4)]
    public ForsakenPowerStatsDefinition? Stats { get; set; }

    [YamlMember(Order = 5)]
    public ForsakenPowerStaminaDrainModifierDefinition? StaminaDrainModifier { get; set; }

    [YamlMember(Order = 6)]
    public Dictionary<string, string>? DamageTakenModifiers { get; set; }

    [YamlMember(Order = 7)]
    public Dictionary<string, float>? PercentageDamageModifiers { get; set; }
}

internal sealed class ForsakenPowerStatsDefinition
{
    [YamlMember(Order = 1)]
    public string? RegenMultiplier { get; set; }

    [YamlMember(Order = 2)]
    public float? StaminaDrainPerSec { get; set; }

    [YamlMember(Order = 3)]
    public float? AdrenalineModifier { get; set; }

    [YamlMember(Order = 4)]
    public float? SpeedModifier { get; set; }

    [YamlMember(Order = 5)]
    public float? SwimSpeedModifier { get; set; }

    [YamlMember(Order = 6)]
    public string? JumpModifier { get; set; }

    [YamlMember(Order = 7)]
    public string? WindRun { get; set; }

    [YamlMember(Order = 8)]
    public string? Armor { get; set; }

    [YamlMember(Order = 9)]
    public string? Block { get; set; }

    [YamlMember(Order = 10)]
    public float? StaggerModifier { get; set; }

    [YamlMember(Order = 11)]
    public float? AddMaxCarryWeight { get; set; }

    [YamlMember(Order = 12)]
    public string? SkillLevel { get; set; }

    [YamlMember(Order = 13)]
    public string? SkillLevel2 { get; set; }
}

internal sealed class ForsakenPowerStaminaDrainModifierDefinition
{
    [YamlMember(Order = 1)]
    public float? Run { get; set; }

    [YamlMember(Order = 2)]
    public float? Attack { get; set; }

    [YamlMember(Order = 3)]
    public float? Block { get; set; }

    [YamlMember(Order = 4)]
    public float? Dodge { get; set; }

    [YamlMember(Order = 5)]
    public float? Jump { get; set; }

    [YamlMember(Order = 6)]
    public float? Sneak { get; set; }

    [YamlMember(Order = 7)]
    public float? Swim { get; set; }

    [YamlMember(Order = 8)]
    public float? HomeItem { get; set; }
}
