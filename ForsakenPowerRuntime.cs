using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace BossRules;

internal static partial class ForsakenPowerRuntime
{
    private const string ModePatch = "patch";
    private static readonly object Sync = new();
    private static readonly HashSet<int> TooltipPowerHashes = new();
    private static readonly HashSet<int> TailwindPowerHashes = new();
    private static readonly AccessTools.FieldRef<Ship, List<Player>> ShipPlayersRef =
        AccessTools.FieldRefAccess<Ship, List<Player>>("m_players");

    private static ForsakenPowersDefinition? _definition;
    private static bool _pendingApply;
    private static bool _lastEnabled;
    private static string _lastAppliedSignature = "";

    internal static void Configure(ForsakenPowersDefinition? definition)
    {
        lock (Sync)
        {
            _definition = definition;
            _pendingApply = true;
        }
    }

    internal static void Reset()
    {
        lock (Sync)
        {
            _definition = null;
            RestoreAllSnapshotsLocked();
            TooltipPowerHashes.Clear();
            TailwindPowerHashes.Clear();
            _pendingApply = false;
            _lastAppliedSignature = "";
            _lastEnabled = false;
        }
    }

    internal static void ProcessDeferredApply()
    {
        bool enabled = BossRulesConfig.IsForsakenPowerRulesEnabled();
        string signature;
        ForsakenPowersDefinition? definition;
        lock (Sync)
        {
            definition = _definition;
            signature = enabled ? BuildDefinitionSignature(definition) : "<disabled>";
            if (!_pendingApply &&
                _lastEnabled == enabled &&
                string.Equals(_lastAppliedSignature, signature, StringComparison.Ordinal))
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
            signature = enabled ? BuildDefinitionSignature(definition) : "<disabled>";
            RestoreAllSnapshotsLocked();
            TooltipPowerHashes.Clear();
            TailwindPowerHashes.Clear();

            if (enabled && definition?.Powers is { Count: > 0 })
            {
                ApplyDefinitionLocked(definition);
            }

            _pendingApply = false;
            _lastEnabled = enabled;
            _lastAppliedSignature = signature;
        }
    }

    internal static bool HasTailwindPower(Ship? ship)
    {
        if (ship == null || !BossRulesConfig.IsForsakenPowerRulesEnabled())
        {
            return false;
        }

        lock (Sync)
        {
            if (TailwindPowerHashes.Count == 0)
            {
                return false;
            }
        }

        List<Player>? players = ShipPlayersRef(ship);
        if (players == null || players.Count == 0)
        {
            return false;
        }

        lock (Sync)
        {
            foreach (Player player in players)
            {
                if (PlayerHasAnyPowerHash(player, TailwindPowerHashes))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static bool TryOverrideGuardianPowerAdrenalineGain(Player? player, out float originalValue)
    {
        originalValue = player?.m_adrenalineGuardianPower ?? 0f;
        if (player == null || !BossRulesConfig.IsForsakenPowerRulesEnabled())
        {
            return false;
        }

        float? adrenalineGain;
        lock (Sync)
        {
            adrenalineGain = _definition?.Defaults?.AdrenalineGain;
        }

        if (!adrenalineGain.HasValue)
        {
            return false;
        }

        player.m_adrenalineGuardianPower = Mathf.Max(0f, adrenalineGain.Value);
        return true;
    }

    private static void ApplyDefinitionLocked(ForsakenPowersDefinition definition)
    {
        string mode = (definition.Mode ?? "replace").Trim();
        bool replace = true;
        if (string.Equals(mode, ModePatch, StringComparison.OrdinalIgnoreCase))
        {
            replace = false;
        }
        else if (!string.Equals(mode, "replace", StringComparison.OrdinalIgnoreCase))
        {
            BossRulesRuntime.WarnInvalidEntry($"forsakenPowers.mode '{mode}' is unknown. Supported values are replace and patch. Using replace.");
        }

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (ForsakenPowerDefinition power in definition.Powers ?? new List<ForsakenPowerDefinition>())
        {
            string prefab = (power.Prefab ?? "").Trim();
            if (prefab.Length == 0)
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
            TooltipPowerHashes.Add(statusEffect.NameHash());
            SE_Stats? stats = statusEffect as SE_Stats;
            if (replace && stats != null)
            {
                ClearSupportedStats(stats);
            }

            ApplyDurationAndCooldown(statusEffect, definition.Defaults, power);
            if (stats != null)
            {
                ApplyStats(stats, power, prefab);
            }
            else if (HasStatFields(power))
            {
                BossRulesRuntime.WarnInvalidEntry($"forsakenPowers entry '{prefab}' is not an SE_Stats StatusEffect. Numeric stat fields were ignored.");
            }

            int hash = statusEffect.NameHash();
            if (power.Tailwind == true)
            {
                TailwindPowerHashes.Add(hash);
            }
        }
    }

    private static void ApplyDurationAndCooldown(
        StatusEffect statusEffect,
        ForsakenPowerDefaultsDefinition? defaults,
        ForsakenPowerDefinition power)
    {
        float? duration = power.DurationSeconds ?? defaults?.DurationSeconds;
        if (duration.HasValue)
        {
            statusEffect.m_ttl = Mathf.Max(0f, duration.Value);
        }

        float? cooldown = power.CooldownSeconds ?? defaults?.CooldownSeconds;
        if (cooldown.HasValue)
        {
            statusEffect.m_cooldown = Mathf.Max(0f, cooldown.Value);
        }
    }

    private static void ApplyStats(SE_Stats stats, ForsakenPowerDefinition power, string context)
    {
        ApplyStaminaCostPercent(stats, power.StaminaCostPercent, context);
        ApplyBlockStaminaReturn(stats, power);
        ApplyOutgoingDamagePercent(stats, power.OutgoingDamagePercent, context);
        ApplyIncomingDamageModifiers(stats, power.IncomingDamageModifiers, context);
        ApplyRegenPercent(stats, power.RegenPercent, context);

        if (power.CarryWeight.HasValue)
        {
            stats.m_addMaxCarryWeight = power.CarryWeight.Value;
        }

        if (power.Armor?.Flat.HasValue == true)
        {
            stats.m_addArmor = power.Armor.Flat.Value;
        }

        if (power.Armor?.Percent.HasValue == true)
        {
            stats.m_armorMultiplier = PercentToFactor(power.Armor.Percent.Value);
        }

        if (power.Movement?.SpeedPercent.HasValue == true)
        {
            stats.m_speedModifier = PercentToFactor(power.Movement.SpeedPercent.Value);
        }

        if (power.Movement?.JumpHeightPercent.HasValue == true)
        {
            stats.m_jumpModifier.y = PercentToFactor(power.Movement.JumpHeightPercent.Value);
        }

        ApplySkillLevels(stats, power.SkillLevels, context);

        if (power.AdrenalinePercent.HasValue)
        {
            stats.m_adrenalineModifier = PercentToFactor(power.AdrenalinePercent.Value);
        }

        if (power.StaggerGaugePercent.HasValue)
        {
            stats.m_staggerModifier = PercentToFactor(power.StaggerGaugePercent.Value);
        }
    }

    private static void ApplyBlockStaminaReturn(SE_Stats stats, ForsakenPowerDefinition power)
    {
        if (!power.BlockStaminaReturn.HasValue)
        {
            return;
        }

        stats.m_blockStaminaUseFlatValue = -Mathf.Max(0f, power.BlockStaminaReturn.Value);
    }

    private static void ApplyStaminaCostPercent(
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
            string rawKey = entry.Key;
            float percent = entry.Value;
            string key = NormalizeKey(rawKey);
            float value = PercentToFactor(percent);
            switch (key)
            {
                case "run":
                    stats.m_runStaminaDrainModifier = value;
                    break;
                case "jump":
                    stats.m_jumpStaminaUseModifier = value;
                    break;
                case "sneak":
                    stats.m_sneakStaminaUseModifier = value;
                    break;
                case "dodge":
                    stats.m_dodgeStaminaUseModifier = value;
                    break;
                case "swim":
                    stats.m_swimStaminaUseModifier = value;
                    break;
                case "block":
                    stats.m_blockStaminaUseModifier = value;
                    break;
                case "attack":
                    stats.m_attackStaminaUseModifier = value;
                    break;
                default:
                    BossRulesRuntime.WarnInvalidEntry($"forsakenPowers entry '{context}' has unsupported staminaCostPercent key '{rawKey}'.");
                    break;
            }
        }
    }

    private static void ApplyOutgoingDamagePercent(
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
            float percent = entry.Value;
            if (!SetDamagePercent(ref stats.m_percentigeDamageModifiers, rawType, PercentToFactor(percent)))
            {
                BossRulesRuntime.WarnInvalidEntry($"forsakenPowers entry '{context}' has unsupported outgoingDamagePercent type '{rawType}'.");
            }
        }
    }

    private static void ApplyIncomingDamageModifiers(
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
                BossRulesRuntime.WarnInvalidEntry($"forsakenPowers entry '{context}' has unsupported incomingDamageModifiers type '{rawType}'.");
                continue;
            }

            if (!Enum.TryParse(rawModifier, true, out HitData.DamageModifier modifier))
            {
                BossRulesRuntime.WarnInvalidEntry($"forsakenPowers entry '{context}' has unsupported incomingDamageModifiers modifier '{rawModifier}'.");
                continue;
            }

            SetDamageModifier(stats.m_mods, damageType, modifier);
        }
    }

    private static void ApplyRegenPercent(
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
            string rawKey = entry.Key;
            float percent = entry.Value;
            float multiplier = 1f + PercentToFactor(percent);
            switch (NormalizeKey(rawKey))
            {
                case "health":
                    stats.m_healthRegenMultiplier = multiplier;
                    break;
                case "stamina":
                    stats.m_staminaRegenMultiplier = multiplier;
                    break;
                case "eitr":
                    stats.m_eitrRegenMultiplier = multiplier;
                    break;
                default:
                    BossRulesRuntime.WarnInvalidEntry($"forsakenPowers entry '{context}' has unsupported regenPercent key '{rawKey}'.");
                    break;
            }
        }
    }

    private static void ApplySkillLevels(
        SE_Stats stats,
        Dictionary<string, float>? values,
        string context)
    {
        if (values == null || values.Count == 0)
        {
            return;
        }

        int index = 0;
        foreach (KeyValuePair<string, float> entry in values)
        {
            string rawSkill = entry.Key;
            float value = entry.Value;
            if (!Enum.TryParse(rawSkill, true, out Skills.SkillType skill) ||
                skill == Skills.SkillType.None)
            {
                BossRulesRuntime.WarnInvalidEntry($"forsakenPowers entry '{context}' has unsupported skillLevels key '{rawSkill}'.");
                continue;
            }

            if (index == 0)
            {
                stats.m_skillLevel = skill;
                stats.m_skillLevelModifier = value;
            }
            else if (index == 1)
            {
                stats.m_skillLevel2 = skill;
                stats.m_skillLevelModifier2 = value;
            }
            else
            {
                BossRulesRuntime.WarnInvalidEntry($"forsakenPowers entry '{context}' has more than two skillLevels entries. '{rawSkill}' was ignored because SE_Stats supports two skill level slots.");
            }

            index++;
        }
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

    private static bool PlayerHasAnyPowerHash(Player? player, HashSet<int> hashes)
    {
        if (player == null)
        {
            return false;
        }

        foreach (StatusEffect statusEffect in player.GetSEMan().GetStatusEffects())
        {
            if (statusEffect != null && hashes.Contains(statusEffect.NameHash()))
            {
                return true;
            }
        }

        return false;
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
        return power.StaminaCostPercent is { Count: > 0 } ||
               power.BlockStaminaReturn.HasValue ||
               power.OutgoingDamagePercent is { Count: > 0 } ||
               power.IncomingDamageModifiers is { Count: > 0 } ||
               power.RegenPercent is { Count: > 0 } ||
               power.CarryWeight.HasValue ||
               power.Armor != null ||
               power.Movement != null ||
               power.SkillLevels is { Count: > 0 } ||
               power.AdrenalinePercent.HasValue ||
               power.StaggerGaugePercent.HasValue;
    }

    private static bool IsGameDataReady()
    {
        return ObjectDB.instance?.m_StatusEffects is { Count: > 0 };
    }

    private static string BuildDefinitionSignature(ForsakenPowersDefinition? definition)
    {
        if (definition?.Powers == null)
        {
            return "<none>";
        }

        return string.Join(
            "\n",
            definition.Mode ?? "replace",
            definition.Defaults?.DurationSeconds?.ToString("R", CultureInfo.InvariantCulture) ?? "",
            definition.Defaults?.CooldownSeconds?.ToString("R", CultureInfo.InvariantCulture) ?? "",
            definition.Defaults?.AdrenalineGain?.ToString("R", CultureInfo.InvariantCulture) ?? "",
            string.Join("|", definition.Powers.Select(power => power.Prefab ?? "")));
    }

    private static string NormalizeKey(string? value)
    {
        return (value ?? "")
            .Trim()
            .Replace("_", "")
            .Replace("-", "")
            .ToLowerInvariant();
    }

    private static float PercentToFactor(float percent)
    {
        return percent / 100f;
    }
}
