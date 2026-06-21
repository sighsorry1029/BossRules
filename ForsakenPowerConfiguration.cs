using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace BossRules;

internal sealed class ForsakenPowersDefinition
{
    [YamlMember(Order = 1)]
    public string? Mode { get; set; }

    [YamlMember(Order = 2)]
    public ForsakenPowerDefaultsDefinition? Defaults { get; set; }

    [YamlMember(Order = 3)]
    public List<ForsakenPowerDefinition>? Powers { get; set; }
}

internal sealed class ForsakenPowerDefaultsDefinition
{
    [YamlMember(Order = 1)]
    public float? DurationSeconds { get; set; }

    [YamlMember(Order = 2)]
    public float? CooldownSeconds { get; set; }

    [YamlMember(Order = 3)]
    public float? AdrenalineGain { get; set; }
}

internal sealed class ForsakenPowerDefinition
{
    [YamlMember(Order = 1)]
    public string? Prefab { get; set; }

    [YamlMember(Order = 2)]
    public float? DurationSeconds { get; set; }

    [YamlMember(Order = 3)]
    public float? CooldownSeconds { get; set; }

    [YamlMember(Order = 4)]
    public Dictionary<string, float>? StaminaCostPercent { get; set; }

    [YamlMember(Order = 5)]
    public float? BlockStaminaReturn { get; set; }

    [YamlMember(Order = 6)]
    public Dictionary<string, float>? OutgoingDamagePercent { get; set; }

    [YamlMember(Order = 7)]
    public Dictionary<string, string>? IncomingDamageModifiers { get; set; }

    [YamlMember(Order = 8)]
    public Dictionary<string, float>? RegenPercent { get; set; }

    [YamlMember(Order = 9)]
    public float? CarryWeight { get; set; }

    [YamlMember(Order = 10)]
    public ForsakenPowerArmorDefinition? Armor { get; set; }

    [YamlMember(Order = 11)]
    public ForsakenPowerMovementDefinition? Movement { get; set; }

    [YamlMember(Order = 12)]
    public Dictionary<string, float>? SkillLevels { get; set; }

    [YamlMember(Order = 13)]
    public float? AdrenalinePercent { get; set; }

    [YamlMember(Order = 14)]
    public float? StaggerGaugePercent { get; set; }

    [YamlMember(Order = 15)]
    public bool? Tailwind { get; set; }
}

internal sealed class ForsakenPowerArmorDefinition
{
    [YamlMember(Order = 1)]
    public float? Flat { get; set; }

    [YamlMember(Order = 2)]
    public float? Percent { get; set; }
}

internal sealed class ForsakenPowerMovementDefinition
{
    [YamlMember(Order = 1)]
    public float? SpeedPercent { get; set; }

    [YamlMember(Order = 2)]
    public float? JumpHeightPercent { get; set; }
}
