using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace BossRules;

internal static partial class ForsakenPowerRuntime
{
    private static readonly object Sync = new();

    private static IReadOnlyList<ForsakenPowerDefinition>? _definition;
    private static bool _pendingApply;
    private static bool _lastEnabled;

    internal static void Configure(IReadOnlyList<ForsakenPowerDefinition>? definition)
    {
        lock (Sync)
        {
            _definition = definition;
            _pendingApply = true;
        }
    }

    internal static void RequestReapply()
    {
        lock (Sync)
        {
            _pendingApply = true;
        }
    }

    internal static void ReleaseDataForgeOwnedSnapshots()
    {
        lock (Sync)
        {
            RestoreSnapshotsOwnedByDataForgeLocked();
        }
    }

    internal static void Reset()
    {
        lock (Sync)
        {
            _definition = null;
            RestoreSnapshotsNotOwnedByDataForgeLocked();
            SnapshotsByHash.Clear();
            _pendingApply = false;
            _lastEnabled = false;
        }
    }

    internal static void ProcessDeferredApply()
    {
        bool enabled = BossRulesConfig.IsForsakenPowerRulesEnabled();
        IReadOnlyList<ForsakenPowerDefinition>? definition;
        lock (Sync)
        {
            definition = _definition;
            if (!_pendingApply && _lastEnabled == enabled)
            {
                return;
            }
        }

        if (!IsGameDataReady())
        {
            return;
        }

        lock (Sync)
        {
            enabled = BossRulesConfig.IsForsakenPowerRulesEnabled();
            definition = _definition;
            RestoreSnapshotsNotOwnedByDataForgeLocked();

            if (enabled && definition is { Count: > 0 })
            {
                ApplyDefinitionLocked(definition);
            }

            _pendingApply = false;
            _lastEnabled = enabled;
        }
    }

    internal static bool TryOverrideGuardianPowerAdrenalineGain(Player? player, out float originalValue)
    {
        originalValue = player?.m_adrenalineGuardianPower ?? 0f;
        if (player == null || !BossRulesConfig.IsForsakenPowerRulesEnabled())
        {
            return false;
        }

        player.m_adrenalineGuardianPower = BossRulesConfig.GetGuardianPowerActivationAdrenaline();
        return true;
    }

    private static void ApplyDefinitionLocked(IReadOnlyList<ForsakenPowerDefinition> definition)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> cleared = new(StringComparer.OrdinalIgnoreCase);
        foreach (ForsakenPowerDefinition power in definition)
        {
            string prefab = (power.Effect ?? "").Trim();
            if (prefab.Length == 0)
            {
                continue;
            }

            if (DataForgeStatusEffectBridge.IsStatusEffectOwnedByDataForge(prefab))
            {
                continue;
            }

            if (!seen.Add(prefab))
            {
                BossRulesRuntime.WarnInvalidEntry($"forsakenPowers contains multiple entries for '{prefab}'. Later entries apply after earlier entries.");
            }

            StatusEffect? statusEffect = ResolveStatusEffect(prefab);
            if (statusEffect == null)
            {
                BossRulesRuntime.WarnInvalidEntry($"forsakenPowers entry '{prefab}' references an unknown StatusEffect prefab.");
                continue;
            }

            EnsureSnapshotLocked(statusEffect);
            if (cleared.Add(prefab))
            {
                ClearSupportedFields(statusEffect);
            }

            SE_Stats? stats = statusEffect as SE_Stats;

            ApplyStatusEffectFields(statusEffect, power, prefab);
            if (stats != null)
            {
                ApplyStats(stats, power, prefab);
            }
            else if (HasStatFields(power))
            {
                BossRulesRuntime.WarnInvalidEntry($"forsakenPowers entry '{prefab}' is not an SE_Stats StatusEffect. Numeric stat fields were ignored.");
            }

        }
    }

    private static void ApplyStatusEffectFields(StatusEffect statusEffect, ForsakenPowerDefinition power, string context)
    {
        float[]? time = ParseFloatTuple(power.Time, context, "time", 1, 2);
        if (time is { Length: > 0 })
        {
            statusEffect.m_ttl = Mathf.Max(0f, time[0]);
        }

        if (time is { Length: > 1 })
        {
            statusEffect.m_cooldown = Mathf.Max(0f, time[1]);
        }

        if (string.IsNullOrWhiteSpace(power.Attributes))
        {
            return;
        }

        if (Enum.TryParse(power.Attributes, true, out StatusEffect.StatusAttribute attributes))
        {
            statusEffect.m_attributes = attributes;
            return;
        }

        BossRulesRuntime.WarnInvalidEntry($"forsakenPowers entry '{context}' has unsupported attributes value '{power.Attributes}'.");
    }

    private static void ApplyStats(SE_Stats stats, ForsakenPowerDefinition power, string context)
    {
        ApplyStatsBlock(stats, power.Stats, context);
        ApplyStaminaDrainModifier(stats, power.StaminaDrainModifier);
        ApplyPercentageDamageModifiers(stats, power.PercentageDamageModifiers, context);
        ApplyDamageTakenModifiers(stats, power.DamageTakenModifiers, context);
    }

    private static void ApplyStatsBlock(SE_Stats stats, ForsakenPowerStatsDefinition? definition, string context)
    {
        if (definition == null)
        {
            return;
        }

        float[]? regenMultiplier = ParseFloatTuple(definition.RegenMultiplier, context, "stats.regenMultiplier", 1, 3);
        if (regenMultiplier is { Length: > 0 })
        {
            stats.m_healthRegenMultiplier = Mathf.Max(0f, regenMultiplier[0]);
            stats.m_staminaRegenMultiplier = Mathf.Max(0f, regenMultiplier.Length > 1 ? regenMultiplier[1] : 1f);
            stats.m_eitrRegenMultiplier = Mathf.Max(0f, regenMultiplier.Length > 2 ? regenMultiplier[2] : 1f);
        }

        if (definition.StaminaDrainPerSec.HasValue)
        {
            stats.m_staminaDrainPerSec = definition.StaminaDrainPerSec.Value;
        }

        if (definition.AdrenalineModifier.HasValue)
        {
            stats.m_adrenalineModifier = definition.AdrenalineModifier.Value;
        }

        if (definition.SpeedModifier.HasValue)
        {
            stats.m_speedModifier = definition.SpeedModifier.Value;
        }

        if (definition.SwimSpeedModifier.HasValue)
        {
            stats.m_swimSpeedModifier = definition.SwimSpeedModifier.Value;
        }

        float[]? jumpModifier = ParseFloatTuple(definition.JumpModifier, context, "stats.jumpModifier", 1, 3);
        if (jumpModifier is { Length: > 0 })
        {
            stats.m_jumpModifier.x = jumpModifier[0];
            stats.m_jumpModifier.y = jumpModifier.Length > 1 ? jumpModifier[1] : 0f;
            stats.m_jumpModifier.z = jumpModifier.Length > 2 ? jumpModifier[2] : 0f;
        }

        float[]? windRun = ParseFloatTuple(definition.WindRun, context, "stats.windRun", 1, 2);
        if (windRun is { Length: > 0 })
        {
            stats.m_windMovementModifier = windRun[0];
            stats.m_windRunStaminaModifier = windRun.Length > 1 ? windRun[1] : 0f;
        }

        float[]? armor = ParseFloatTuple(definition.Armor, context, "stats.armor", 1, 2);
        if (armor is { Length: > 0 })
        {
            stats.m_addArmor = armor[0];
            stats.m_armorMultiplier = armor.Length > 1 ? armor[1] : 0f;
        }

        float[]? block = ParseFloatTuple(definition.Block, context, "stats.block", 1, 2);
        if (block is { Length: > 0 })
        {
            stats.m_timedBlockBonus = block[0];
            stats.m_blockStaminaUseFlatValue = block.Length > 1 ? block[1] : 0f;
        }

        if (definition.StaggerModifier.HasValue)
        {
            stats.m_staggerModifier = definition.StaggerModifier.Value;
        }

        if (definition.AddMaxCarryWeight.HasValue)
        {
            stats.m_addMaxCarryWeight = definition.AddMaxCarryWeight.Value;
        }

        ApplySkillLevel(stats, definition.SkillLevel, context, "stats.skillLevel", first: true);
        ApplySkillLevel(stats, definition.SkillLevel2, context, "stats.skillLevel2", first: false);
    }

    private static void ApplyStaminaDrainModifier(
        SE_Stats stats,
        ForsakenPowerStaminaDrainModifierDefinition? definition)
    {
        if (definition == null)
        {
            return;
        }

        if (definition.Run.HasValue) stats.m_runStaminaDrainModifier = definition.Run.Value;
        if (definition.Attack.HasValue) stats.m_attackStaminaUseModifier = definition.Attack.Value;
        if (definition.Block.HasValue) stats.m_blockStaminaUseModifier = definition.Block.Value;
        if (definition.Dodge.HasValue) stats.m_dodgeStaminaUseModifier = definition.Dodge.Value;
        if (definition.Jump.HasValue) stats.m_jumpStaminaUseModifier = definition.Jump.Value;
        if (definition.Sneak.HasValue) stats.m_sneakStaminaUseModifier = definition.Sneak.Value;
        if (definition.Swim.HasValue) stats.m_swimStaminaUseModifier = definition.Swim.Value;
        if (definition.HomeItem.HasValue) stats.m_homeItemStaminaUseModifier = definition.HomeItem.Value;
    }

    private static void ApplyPercentageDamageModifiers(
        SE_Stats stats,
        Dictionary<string, float>? values,
        string context)
    {
        if (values == null)
        {
            return;
        }

        foreach (KeyValuePair<string, float> entry in values)
        {
            string rawType = entry.Key;
            if (!SetDamagePercent(ref stats.m_percentigeDamageModifiers, rawType, entry.Value))
            {
                BossRulesRuntime.WarnInvalidEntry($"forsakenPowers entry '{context}' has unsupported percentageDamageModifiers type '{rawType}'.");
            }
        }
    }

    private static void ApplyDamageTakenModifiers(
        SE_Stats stats,
        Dictionary<string, string>? values,
        string context)
    {
        if (values == null)
        {
            return;
        }

        stats.m_mods ??= new List<HitData.DamageModPair>();
        foreach (KeyValuePair<string, string> entry in values)
        {
            string rawType = entry.Key;
            string rawModifier = entry.Value;
            if (!Enum.TryParse(rawType, true, out HitData.DamageType damageType) ||
                !IsIndividualDamageType(damageType))
            {
                BossRulesRuntime.WarnInvalidEntry($"forsakenPowers entry '{context}' has unsupported damageTakenModifiers type '{rawType}'.");
                continue;
            }

            if (!Enum.TryParse(rawModifier, true, out HitData.DamageModifier modifier))
            {
                BossRulesRuntime.WarnInvalidEntry($"forsakenPowers entry '{context}' has unsupported damageTakenModifiers modifier '{rawModifier}'.");
                continue;
            }

            SetDamageModifier(stats.m_mods, damageType, modifier);
        }
    }

    private static void ApplySkillLevel(
        SE_Stats stats,
        string? value,
        string context,
        string fieldName,
        bool first)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        string rawValue = value!.Trim();
        string[] parts = rawValue.Split(',');
        if (parts.Length != 2)
        {
            BossRulesRuntime.WarnInvalidEntry($"forsakenPowers entry '{context}' has invalid {fieldName} value '{rawValue}'. Use SkillName, amount.");
            return;
        }

        string rawSkill = parts[0].Trim();
        if (!Enum.TryParse(rawSkill, true, out Skills.SkillType skill) ||
            skill == Skills.SkillType.None)
        {
            BossRulesRuntime.WarnInvalidEntry($"forsakenPowers entry '{context}' has unsupported {fieldName} skill '{rawSkill}'.");
            return;
        }

        if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float modifier))
        {
            BossRulesRuntime.WarnInvalidEntry($"forsakenPowers entry '{context}' has invalid {fieldName} amount '{parts[1].Trim()}'.");
            return;
        }

        if (first)
        {
            stats.m_skillLevel = skill;
            stats.m_skillLevelModifier = modifier;
            return;
        }

        stats.m_skillLevel2 = skill;
        stats.m_skillLevelModifier2 = modifier;
    }

    private static bool SetDamagePercent(ref HitData.DamageTypes damageTypes, string rawType, float value)
    {
        switch (NormalizeKey(rawType))
        {
            case "damage":
                damageTypes.m_damage = value;
                return true;
            case "blunt":
                damageTypes.m_blunt = value;
                return true;
            case "slash":
                damageTypes.m_slash = value;
                return true;
            case "pierce":
                damageTypes.m_pierce = value;
                return true;
            case "chop":
                damageTypes.m_chop = value;
                return true;
            case "pickaxe":
            case "pickaxes":
                damageTypes.m_pickaxe = value;
                return true;
            case "fire":
                damageTypes.m_fire = value;
                return true;
            case "frost":
                damageTypes.m_frost = value;
                return true;
            case "lightning":
                damageTypes.m_lightning = value;
                return true;
            case "poison":
                damageTypes.m_poison = value;
                return true;
            case "spirit":
                damageTypes.m_spirit = value;
                return true;
            default:
                return false;
        }
    }

    private static void SetDamageModifier(
        List<HitData.DamageModPair> mods,
        HitData.DamageType damageType,
        HitData.DamageModifier modifier)
    {
        for (int index = 0; index < mods.Count; index++)
        {
            if (mods[index].m_type != damageType)
            {
                continue;
            }

            if (modifier == HitData.DamageModifier.Normal)
            {
                mods.RemoveAt(index);
            }
            else
            {
                mods[index] = new HitData.DamageModPair
                {
                    m_type = damageType,
                    m_modifier = modifier
                };
            }

            return;
        }

        if (modifier != HitData.DamageModifier.Normal)
        {
            mods.Add(new HitData.DamageModPair
            {
                m_type = damageType,
                m_modifier = modifier
            });
        }
    }

    private static bool IsIndividualDamageType(HitData.DamageType damageType)
    {
        return damageType is HitData.DamageType.Blunt
            or HitData.DamageType.Slash
            or HitData.DamageType.Pierce
            or HitData.DamageType.Chop
            or HitData.DamageType.Pickaxe
            or HitData.DamageType.Fire
            or HitData.DamageType.Frost
            or HitData.DamageType.Lightning
            or HitData.DamageType.Poison
            or HitData.DamageType.Spirit;
    }

    private static StatusEffect? ResolveStatusEffect(string prefab)
    {
        string normalized = (prefab ?? "").Trim();
        if (normalized.Length == 0)
        {
            return null;
        }

        StatusEffect? statusEffect = ObjectDB.instance?.GetStatusEffect(normalized.GetStableHashCode());
        if (statusEffect != null)
        {
            return statusEffect;
        }

        return ObjectDB.instance?.m_StatusEffects.FirstOrDefault(effect =>
            effect != null &&
            string.Equals(Utils.GetPrefabName(effect.name), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasStatFields(ForsakenPowerDefinition power)
    {
        return power.Stats != null ||
               power.StaminaDrainModifier != null ||
               power.DamageTakenModifiers is { Count: > 0 } ||
               power.PercentageDamageModifiers is { Count: > 0 };
    }

    private static bool IsGameDataReady()
    {
        return ObjectDB.instance?.m_StatusEffects is { Count: > 0 };
    }

    private static float[]? ParseFloatTuple(string? rawValue, string context, string fieldName, int minCount, int maxCount)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        string raw = rawValue!.Trim();
        string[] parts = raw.Split(',');
        if (parts.Length < minCount || parts.Length > maxCount)
        {
            BossRulesRuntime.WarnInvalidEntry(
                $"forsakenPowers entry '{context}' has invalid {fieldName} value '{raw}'. Expected {minCount}~{maxCount} comma-separated number(s).");
            return null;
        }

        float[] values = new float[parts.Length];
        for (int index = 0; index < parts.Length; index++)
        {
            string part = parts[index].Trim();
            if (!float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out values[index]))
            {
                BossRulesRuntime.WarnInvalidEntry(
                    $"forsakenPowers entry '{context}' has invalid {fieldName} number '{part}'.");
                return null;
            }
        }

        return values;
    }

    private static string NormalizeKey(string? value)
    {
        return (value ?? "")
            .Trim()
            .Replace("_", "")
            .Replace("-", "")
            .ToLowerInvariant();
    }

}
