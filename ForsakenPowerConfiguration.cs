using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        const string yaml = """
                            # BossRules Forsaken Power overrides
                            # Uses the same compact field style as DataForge effects.yml, but only supports the fields listed here.
                            # Rows can be copied directly into DataForge effects.yml. DataForge may support additional status effect fields.
                            # If the same effect exists in DataForge effects.yml, DataForge owns that effect and BossRules skips it.
                            # BossRules resets supported stat fields before applying each effect entry; list every supported effect value you want to keep.
                            #
                            # Schema:
                            # - effect: GP_Eikthyr                  # GP_Eikthyr => override that Forsaken Power StatusEffect.
                            #   time: 18, 60                        # 18, 60 => lasts 18s, then cannot reapply for 60s.
                            #   attributes: None                    # SailingPower => grant that StatusAttribute flag.
                            #   stats:
                            #     regenMultiplier: 1, 1, 1          # 1.5, 0.5, 0 => health regen +50%, stamina regen x0.5, eitr regen disabled.
                            #     staminaDrainPerSec: 0             # 2 => drain 2 stamina per second.
                            #     adrenalineModifier: 0             # 0.25 => adrenaline gain/use value +25%.
                            #     speedModifier: 0                  # 0.2 => movement speed +20%.
                            #     swimSpeedModifier: 0              # 0.3 => swim speed +30%.
                            #     jumpModifier: 0, 0, 0             # 0, 0.25, 0 => jump height +25%; 0.2, 0, 0.2 => jump distance +20%.
                            #     windRun: 0, 0                     # 0.2, -0.25 => tailwind movement speed up to +20%, run stamina drain up to -25%.
                            #     armor: 0, 0                       # 10, 0.25 => (armor + 10) * 1.25.
                            #     block: 0, 0                       # 0.5, -5 => timed block/parry bonus +50%, block stamina cost -5 flat.
                            #     staggerModifier: 0                # -0.25 => stagger taken -25%.
                            #     addMaxCarryWeight: 0              # 100 => carry weight +100.
                            #     Skill values for skillLevel/skillLevel2: None, Swords, Knives, Clubs, Polearms, Spears, Blocking, Axes, Bows, ElementalMagic, BloodMagic, Unarmed, Pickaxes, WoodCutting, Crossbows, Jump, Sneak, Run, Swim, Fishing, Cooking, Farming, Crafting, Dodge, Ride.
                            #     skillLevel: None, 0               # Swords, 15 => treat Swords as +15 levels while active.
                            #     skillLevel2: None, 0              # Blocking, 10 => treat Blocking as +10 levels while active.
                            #   staminaDrainModifier:
                            #     run: 0                            # -0.25 => running stamina drain -25%.
                            #     attack: 0                         # -0.2 => attack stamina cost -20%.
                            #     block: 0                          # -0.2 => block stamina cost -20%.
                            #     dodge: 0                          # -0.2 => dodge stamina cost -20%.
                            #     jump: 0                           # -0.2 => jump stamina cost -20%.
                            #     sneak: 0                          # -0.2 => sneak stamina cost -20%.
                            #     swim: 0                           # -0.2 => swim stamina cost -20%.
                            #     homeItem: 0                       # -0.2 => hammer/build stamina cost -20%.
                            #   damageTakenModifiers:
                            #     blunt: Normal                     # Resistant => take 50% blunt damage; Normal => remove this effect's blunt modifier.
                            #     slash: Normal                     # Weak => take 150% slash damage.
                            #     pierce: Normal                    # VeryResistant => take 25% pierce damage.
                            #     chop: Normal                      # SlightlyWeak => take 125% chop damage.
                            #     pickaxe: Normal                   # Immune => take 0 pickaxe damage.
                            #     fire: Normal                      # Resistant => take 50% fire damage.
                            #     frost: Normal                     # VeryResistant => take 25% frost damage.
                            #     lightning: Normal                 # SlightlyResistant => take 75% lightning damage.
                            #     poison: Normal                    # Weak => take 150% poison damage.
                            #     spirit: Normal                    # Immune => take 0 spirit damage.
                            #   percentageDamageModifiers:
                            #     blunt: 0                          # 0.25 => blunt damage modifier +25%.
                            #     slash: 0                          # 0.25 => slash damage modifier +25%.
                            #     pierce: 0                         # 0.25 => pierce damage modifier +25%.
                            #     chop: 0                           # 0.25 => chop damage modifier +25%.
                            #     pickaxe: 0                        # 0.25 => pickaxe damage modifier +25%.
                            #     fire: 0                           # 0.25 => fire damage modifier +25%.
                            #     frost: 0                          # 0.25 => frost damage modifier +25%.
                            #     lightning: 0                      # 0.25 => lightning damage modifier +25%.
                            #     poison: 0                         # 0.25 => poison damage modifier +25%.
                            #     spirit: 0                         # 0.25 => spirit damage modifier +25%.
                            #
                            - effect: GP_Eikthyr
                              time: 16, 60
                              percentageDamageModifiers:
                                Pickaxe: 0.5
                              staminaDrainModifier:
                                run: -0.5
                                dodge: -0.5
                              stats:
                                speedModifier: 0.1
                              damageTakenModifiers:
                                Blunt: SlightlyResistant
                            - effect: GP_TheElder
                              time: 16, 60
                              percentageDamageModifiers:
                                Chop: 0.5
                              staminaDrainModifier:
                                swim: -0.5
                                sneak: -0.5
                              stats:
                                regenMultiplier: 2, 1, 1
                              damageTakenModifiers:
                                Poison: SlightlyResistant
                            - effect: GP_Bonemass
                              time: 16, 60
                              staminaDrainModifier:
                                block: -0.5
                              stats:
                                block: 0, -5 # timed block bonus, flat block stamina cost. -5 returns 5 stamina.
                                armor: 20, 0.2
                              damageTakenModifiers:
                                Frost: SlightlyResistant
                            - effect: GP_Moder
                              time: 16, 60
                              attributes: SailingPower
                              staminaDrainModifier:
                                jump: -0.5
                              stats:
                                addMaxCarryWeight: 300
                                jumpModifier: 0, 0.2, 0
                                skillLevel: Farming, 25
                                skillLevel2: Fishing, 25
                              damageTakenModifiers:
                                Fire: SlightlyResistant
                            - effect: GP_Yagluth
                              time: 16, 60
                              percentageDamageModifiers:
                                Fire: 0.1
                                Poison: 0.1
                                Frost: 0.1
                                Lightning: 0.1
                                Spirit: 0.1
                              stats:
                                regenMultiplier: 1, 1, 2
                              damageTakenModifiers:
                                Pierce: SlightlyResistant
                            - effect: GP_Queen
                              time: 16, 60
                              percentageDamageModifiers:
                                Pierce: 0.1
                                Blunt: 0.1
                                Slash: 0.1
                              staminaDrainModifier:
                                attack: -0.1
                              damageTakenModifiers:
                                Slash: SlightlyResistant
                            - effect: GP_Fader
                              time: 16, 60
                              stats:
                                adrenalineModifier: 1
                                staggerModifier: -0.5
                              damageTakenModifiers:
                                Lightning: SlightlyResistant
                            """;
        return yaml
            .Replace("\r\n", "\n")
            .Replace("\n", Environment.NewLine) + Environment.NewLine;
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
